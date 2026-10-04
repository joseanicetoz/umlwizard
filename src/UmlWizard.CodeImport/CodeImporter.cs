using UmlWizard.Core.Model;
using UmlWizard.Core.Rendering;

namespace UmlWizard.CodeImport;

/// <summary>The stages of an import, in order.</summary>
public enum CodeImportStage
{
    /// <summary>Reading the source files from disk; <see cref="CodeImportProgress.Done"/> files of <see cref="CodeImportProgress.Total"/>.</summary>
    Reading,

    /// <summary>The language plug-in reads the declarations of the <see cref="CodeImportProgress.Total"/> files.</summary>
    Parsing,

    /// <summary>The diagram of the <see cref="CodeImportProgress.Total"/> classes is made and laid out.</summary>
    LayingOut,
}

/// <summary>How far an import is: its stage and, while reading files, how many are read.</summary>
public sealed record CodeImportProgress(CodeImportStage Stage, int Done, int Total);

/// <summary>The class diagram of an import and what the import could not show.</summary>
/// <param name="Diagram">The class diagram, named after the source; <c>null</c> when the code declares no types.</param>
/// <param name="Summary">What was imported, e.g. "12 classes, 3 interfaces and 2 enumerations from Shop.Domain.csproj (40 files)."</param>
/// <param name="Notes">What was left out or shown differently, one sentence each.</param>
public sealed record CodeImportResult(DiagramDocument? Diagram, string Summary, IReadOnlyList<string> Notes);

/// <summary>
/// Imports source code as a class diagram: an <see cref="ICodeImporter"/> reads the declarations of the files, then the
/// parts of each type are merged, base types that are types of the import become inheritance (realization for an
/// interface of a class), fields and properties whose type is another imported class become directed associations
/// (<c>*</c> for collections, <c>0..1</c> for optional values) instead of attributes, and the diagram is laid out.
/// <para>
/// The tagged values the plug-in read are kept on the classes, attributes and operations they belong to, in order. Of
/// those with the same name, the first is kept: the first part of a merged type, the first mention of a merged attribute.
/// Those without a name are dropped. <see cref="NamespaceTag"/> is set from <see cref="ImportedType.Namespace"/>, over
/// one the plug-in gave. Associations have no tagged values, so those of attributes shown as associations are left out,
/// and a note says so.
/// </para>
/// </summary>
public static class CodeImporter
{
    /// <summary>The name of the tagged value that keeps the namespace (package, module) of an imported type.</summary>
    public const string NamespaceTag = "namespace";

    private const int MaxDefaultValueLength = 40;

    public static CodeImportResult Import(CodeSource source, ICodeImporter importer) => Import(source, importer, null);

    /// <summary>
    /// Imports as <see cref="Import(CodeSource, ICodeImporter)"/> does, telling <paramref name="progress"/> how far it is:
    /// each stage, and the files read as the share of them changes by a percent. Cancelling stops it between files and
    /// stages, with an <see cref="OperationCanceledException"/>; the plug-in's parsing is not interrupted.
    /// </summary>
    public static CodeImportResult Import(
        CodeSource source, ICodeImporter importer, IProgress<CodeImportProgress>? progress, CancellationToken cancellationToken = default)
    {
        var unreadable = 0;
        var files = new List<SourceFile>();
        var total = source.Files.Count;
        progress?.Report(new CodeImportProgress(CodeImportStage.Reading, 0, total));
        for (var i = 0; i < total; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var path = source.Files[i];
            try
            {
                files.Add(new SourceFile(path, File.ReadAllText(path)));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                unreadable++;
            }

            if (progress is not null && (i + 1) * 100 / total != i * 100 / total)
            {
                progress.Report(new CodeImportProgress(CodeImportStage.Reading, i + 1, total));
            }
        }

        var notes = new List<string>
        {
            $"Only {importer.DisplayName} source files ({string.Join(", ", importer.FileExtensions)}) are read, and without building the code: " +
            "method bodies, comments and documentation are not imported.",
        };
        AddSkippedFiles(source, notes);
        Add(notes, unreadable, "file could not be read", "files could not be read");

        cancellationToken.ThrowIfCancellationRequested();
        progress?.Report(new CodeImportProgress(CodeImportStage.Parsing, 0, files.Count));
        CodeImportParseResult parsed;
        try
        {
            parsed = importer.Parse(files);
        }
        catch (Exception ex)
        {
            notes.Add($"The {importer.DisplayName} importer failed: {ex.Message}");
            return new CodeImportResult(null, Summary([], source), notes);
        }

        cancellationToken.ThrowIfCancellationRequested();
        notes.AddRange(parsed.Notes);
        var diagram = Build(source.Name, source.Label, parsed.Types, notes, progress);
        return new CodeImportResult(diagram, Summary(diagram is null ? [] : ((ClassDiagramModel)diagram.Model).Classes, source), notes);
    }

    #region Building the diagram

    /// <summary>An imported type with its parts merged, and the class it becomes.</summary>
    private sealed class Entry(ImportedType first, UmlClass cls)
    {
        public List<ImportedType> Parts { get; } = [first];

        public ImportedType First => Parts[0];

        public UmlClass Class { get; } = cls;

        public (string Name, int Arity) Key => (First.Name, First.TypeParameters.Count);
    }

    private static DiagramDocument? Build(
        string name, string label, IReadOnlyList<ImportedType> types, List<string> notes, IProgress<CodeImportProgress>? progress)
    {
        // Merge the parts of each type: partial classes, classes reopened in several files.
        var entries = new List<Entry>();
        var byFullName = new Dictionary<string, Entry>(StringComparer.Ordinal);
        foreach (var type in types)
        {
            var fullName = $"{type.Namespace}|{string.Join('.', type.DeclaringTypes)}|{type.Name}|{type.TypeParameters.Count}";
            if (byFullName.TryGetValue(fullName, out var entry))
            {
                entry.Parts.Add(type);
                continue;
            }

            entry = new Entry(type, new UmlClass { Name = DisplayName(type) });
            byFullName[fullName] = entry;
            entries.Add(entry);
        }

        if (entries.Count == 0)
        {
            return null;
        }

        progress?.Report(new CodeImportProgress(CodeImportStage.LayingOut, 0, entries.Count));

        // Types of different namespaces with the same name are told apart by their namespace.
        foreach (var clash in entries.GroupBy(e => e.Class.Name).Where(g => g.Count() > 1).SelectMany(g => g))
        {
            if (clash.First.Namespace.Length > 0)
            {
                clash.Class.Name = $"{clash.First.Namespace}.{clash.Class.Name}";
            }
        }

        var document = DiagramDocument.Create(name, DiagramType.Class);
        document.Description = $"Imported from {label}.";
        var model = (ClassDiagramModel)document.Model;
        var lookup = entries.ToLookup(e => e.Key);
        var externalBases = new SortedSet<string>(StringComparer.Ordinal);
        var longDefaults = 0;
        var taggedAssociations = 0;
        var associations = new Dictionary<(string Source, string Target), (UmlRelationship Relationship, List<string> Names)>();

        foreach (var entry in entries)
        {
            Fill(entry);
            model.Classes.Add(entry.Class);
        }

        foreach (var entry in entries)
        {
            foreach (var baseType in entry.Parts.SelectMany(p => p.BaseTypes))
            {
                if (Resolve(baseType, entry) is not { } target)
                {
                    externalBases.Add(baseType.Text);
                    continue;
                }

                // A class inherits from a class and realizes interfaces; an interface generalizes interfaces.
                var kind = target.First.Kind == ImportedTypeKind.Interface && entry.First.Kind != ImportedTypeKind.Interface
                    ? RelationshipKind.Realization
                    : RelationshipKind.Inheritance;
                if (target != entry && !model.Relationships.Any(r => r.SourceId == entry.Class.Id && r.TargetId == target.Class.Id))
                {
                    model.Relationships.Add(new UmlRelationship { Kind = kind, SourceId = entry.Class.Id, TargetId = target.Class.Id });
                }
            }
        }

        foreach (var (relationship, names) in associations.Values)
        {
            relationship.Label = string.Join(", ", names);
            model.Relationships.Add(relationship);
        }

        if (externalBases.Count > 0)
        {
            var shown = externalBases.Take(6).ToList();
            var more = externalBases.Count > shown.Count ? $" and {externalBases.Count - shown.Count} more" : string.Empty;
            notes.Add($"Base types that are not part of the import are not shown: {string.Join(", ", shown)}{more}.");
        }

        Add(notes, entries.Count(e => e.First.DeclaringTypes.Count > 0), "nested type is shown as a separate class named Outer.Inner",
            "nested types are shown as separate classes named Outer.Inner");
        Add(notes, longDefaults, "initial value was too long to show and was left out", "initial values were too long to show and were left out");
        Add(notes, taggedAssociations, "attribute shown as an association had tagged values, which were left out",
            "attributes shown as associations had tagged values, which were left out");

        ClassDiagramLayout.Arrange(document);
        return document;

        // The imported type a reference names, preferring the same namespace; null for other types.
        Entry? Resolve(TypeRef reference, Entry from)
        {
            if (reference.Name is null)
            {
                return null;
            }

            var candidates = lookup[(reference.Name, reference.Arity)].ToList();
            return candidates.FirstOrDefault(c => c.First.Namespace == from.First.Namespace) ?? candidates.FirstOrDefault();
        }

        void Fill(Entry entry)
        {
            var cls = entry.Class;
            var first = entry.First;
            cls.Kind = entry.Parts.Any(p => p.Kind == ImportedTypeKind.AbstractClass) && first.Kind == ImportedTypeKind.Class
                ? ClassifierKind.AbstractClass
                : first.Kind switch
                {
                    ImportedTypeKind.Interface => ClassifierKind.Interface,
                    ImportedTypeKind.Enumeration => ClassifierKind.Enumeration,
                    ImportedTypeKind.AbstractClass => ClassifierKind.AbstractClass,
                    _ => ClassifierKind.Class,
                };
            cls.Stereotype = entry.Parts.Select(p => p.Stereotype).FirstOrDefault(s => s is not null);
            if (first.Namespace.Length > 0)
            {
                cls.TaggedValues.Set(NamespaceTag, first.Namespace);
            }

            foreach (var part in entry.Parts)
            {
                AddTaggedValues(cls.TaggedValues, part.TaggedValues);
            }

            foreach (var part in entry.Parts)
            {
                foreach (var literal in part.Literals)
                {
                    var uml = new UmlAttribute { Name = literal.Name, Visibility = UmlVisibility.Public, DefaultValue = literal.Value };
                    AddTaggedValues(uml.TaggedValues, literal.TaggedValues);
                    cls.Attributes.Add(uml);
                }

                foreach (var operation in part.Operations)
                {
                    var uml = new UmlOperation
                    {
                        Name = operation.Name,
                        ReturnType = operation.ReturnType,
                        Visibility = operation.Visibility,
                        IsStatic = operation.IsStatic,
                        IsAbstract = operation.IsAbstract,
                    };
                    uml.Parameters.AddRange(operation.Parameters);
                    AddTaggedValues(uml.TaggedValues, operation.TaggedValues);
                    cls.Operations.Add(uml);
                }
            }

            // Attributes after the other parts, so that associations can refer to every class.
            // One attribute per name: parts of a type may each mention it (e.g. a reopened Ruby class), with the
            // tagged values of every mention.
            foreach (var mentions in entry.Parts.SelectMany(p => p.Attributes).GroupBy(a => a.Name))
            {
                AddAttribute(entry, mentions.First(), mentions.SelectMany(a => a.TaggedValues));
            }
        }

        // A field or property whose type is another imported class is an association; others are attributes.
        void AddAttribute(Entry entry, ImportedAttribute attribute, IEnumerable<TaggedValue> taggedValues)
        {
            if (attribute.Type is { } type && Resolve(type, entry) is { } target &&
                target.First.Kind != ImportedTypeKind.Enumeration && target != entry)
            {
                var key = (entry.Class.Id, target.Class.Id);
                if (!associations.TryGetValue(key, out var association))
                {
                    association = (new UmlRelationship
                    {
                        Kind = RelationshipKind.DirectedAssociation,
                        SourceId = entry.Class.Id,
                        TargetId = target.Class.Id,
                        TargetMultiplicity = type.IsMany ? "*" : type.IsOptional ? "0..1" : null,
                    }, []);
                    associations[key] = association;
                }

                association.Names.Add(attribute.Name);
                if (taggedValues.Any(t => !string.IsNullOrWhiteSpace(t?.Name)))
                {
                    taggedAssociations++;
                }

                return;
            }

            var value = attribute.DefaultValue;
            if (value is not null && (value.Length > MaxDefaultValueLength || value.Contains('\n')))
            {
                longDefaults++;
                value = null;
            }

            var uml = new UmlAttribute
            {
                Name = attribute.Name,
                Type = attribute.Type?.Text,
                Visibility = attribute.Visibility,
                IsStatic = attribute.IsStatic,
                DefaultValue = value,
            };
            AddTaggedValues(uml.TaggedValues, taggedValues);
            entry.Class.Attributes.Add(uml);
        }
    }

    /// <summary>
    /// Adds copies of the tagged values a plug-in read, in order: those without a name are dropped, and of those with the
    /// same name (or one <paramref name="target"/> already has), the first is kept.
    /// </summary>
    private static void AddTaggedValues(List<TaggedValue> target, IEnumerable<TaggedValue?> values)
    {
        foreach (var value in values)
        {
            if (value is not null && !string.IsNullOrWhiteSpace(value.Name) && target.Get(value.Name) is null)
            {
                target.Add(new TaggedValue { Name = value.Name, Value = value.Value ?? string.Empty });
            }
        }
    }

    /// <summary>The name a type has in the diagram, e.g. <c>Outer.Inner</c> or <c>Repository&lt;T&gt;</c>.</summary>
    private static string DisplayName(ImportedType type)
    {
        var name = string.Join('.', type.DeclaringTypes.Append(type.Name));
        return type.TypeParameters.Count == 0 ? name : $"{name}<{string.Join(", ", type.TypeParameters)}>";
    }

    #endregion

    #region Report

    private static void AddSkippedFiles(CodeSource source, List<string> notes)
    {
        var skipped = source.SkippedFiles.Select(f => (Extension: f.Key, Count: f.Value))
            .OrderByDescending(g => g.Count).ThenBy(g => g.Extension, StringComparer.Ordinal).ToList();
        if (skipped.Count == 0)
        {
            return;
        }

        var shown = skipped.Take(5).Select(s => $"{s.Count} {s.Extension}").ToList();
        if (skipped.Count > 5)
        {
            shown.Add($"{skipped.Skip(5).Sum(s => s.Count)} other");
        }

        notes.Add($"{Count(skipped.Sum(s => s.Count), "other file was", "other files were")} skipped: {string.Join(", ", shown)}.");
    }

    private static string Summary(IReadOnlyList<UmlClass> classes, CodeSource source) =>
        $"{Count(classes.Count(c => c.Kind is ClassifierKind.Class or ClassifierKind.AbstractClass), "class", "classes")}, " +
        $"{Count(classes.Count(c => c.Kind == ClassifierKind.Interface), "interface", "interfaces")} and " +
        $"{Count(classes.Count(c => c.Kind == ClassifierKind.Enumeration), "enumeration", "enumerations")} from " +
        $"{source.Label} ({Count(source.Files.Count, "file", "files")}).";

    /// <summary>Adds "<paramref name="count"/> <paramref name="one"/>." (or <paramref name="many"/>) when the count is not zero.</summary>
    public static void Add(ICollection<string> notes, int count, string one, string many)
    {
        if (count > 0)
        {
            notes.Add($"{Count(count, one, many)}.");
        }
    }

    /// <summary>"1 file", "2 files".</summary>
    public static string Count(int count, string one, string many) => $"{count} {(count == 1 ? one : many)}";

    #endregion
}
