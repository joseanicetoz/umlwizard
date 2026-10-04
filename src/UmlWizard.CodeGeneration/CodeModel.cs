using UmlWizard.Core.Model;

namespace UmlWizard.CodeGeneration;

/// <summary>The types of a class diagram, with the relationships resolved into what code needs.</summary>
public sealed class CodeModel
{
    private readonly Dictionary<string, TypeModel> _byName = new(StringComparer.Ordinal);

    private CodeModel(ClassDiagramModel diagram, bool multipleInheritance, List<string> warnings)
    {
        foreach (var cls in diagram.Classes)
        {
            var name = Naming.Identifier(cls.Name);
            var unique = name;
            for (var i = 2; Types.Any(t => t.Name == unique); i++)
            {
                unique = name + i;
            }

            if (unique != name)
            {
                warnings.Add($"Two classes are named {name}; the second one is generated as {unique}.");
            }

            var type = new TypeModel(cls, unique);
            Types.Add(type);
            _byName.TryAdd(cls.Name, type);
            _byName.TryAdd(unique, type);
        }

        foreach (var type in Types)
        {
            type.Attributes.AddRange(type.Class.Attributes.Select(a => new AttributeModel(a)));
            type.Operations.AddRange(type.Class.Operations.Select(o =>
                new OperationModel(o, !type.IsEnum && Naming.Identifier(o.Name) == type.Name)
                {
                    IsAbstract = o.IsAbstract || (type.IsInterface && !o.IsStatic),
                }));
            type.Documentation.AddRange(Documentation(diagram, type.Class));
        }

        foreach (var relationship in diagram.Relationships)
        {
            if (Find(diagram.FindClass(relationship.SourceId)) is { } source && Find(diagram.FindClass(relationship.TargetId)) is { } target)
            {
                AddRelationship(relationship, source, target, warnings);
            }
        }

        foreach (var type in Types.Where(t => t.BaseClasses.Count > 1 && !multipleInheritance))
        {
            warnings.Add($"{type.Name}: the language allows one base class; {string.Join(", ", type.BaseClasses.Skip(1).Select(b => b.Name))} " +
                         $"{(type.BaseClasses.Count > 2 ? "are" : "is")} not inherited.");
            type.BaseClasses.RemoveRange(1, type.BaseClasses.Count - 1);
        }

        foreach (var type in Types)
        {
            foreach (var basis in type.BaseClasses.Concat(type.Interfaces))
            {
                basis.IsBase = true;
            }
        }

        foreach (var type in Types.Where(t => !t.IsInterface && !t.IsEnum))
        {
            ResolveOperations(type);
            AddInterfaceAttributes(type);
        }

        WarnAboutUnknownTypes(warnings);
    }

    /// <summary>
    /// Names types that are neither built in nor classes of the diagram, e.g. <c>Part</c> in <c>parts: List&lt;Part&gt;</c>:
    /// the generated code uses them as written, so they have to exist elsewhere.
    /// </summary>
    private void WarnAboutUnknownTypes(List<string> warnings)
    {
        var unknown = new SortedDictionary<string, SortedSet<string>>(StringComparer.Ordinal);
        foreach (var type in Types.Where(t => !t.IsEnum))
        {
            var used = type.Class.Attributes.Select(a => a.Type)
                .Concat(type.Class.Operations.SelectMany(o => o.Parameters.Select(p => p.Type).Append(o.ReturnType)));
            foreach (var name in used.Select(TypeExpression.Parse).OfType<TypeExpression>().SelectMany(e => e.Names()))
            {
                if (Find(name) is null && BuiltInTypes.Key(name) is null)
                {
                    if (!unknown.TryGetValue(name, out var users))
                    {
                        unknown[name] = users = new SortedSet<string>(StringComparer.Ordinal);
                    }

                    users.Add(type.Name);
                }
            }
        }

        foreach (var (name, users) in unknown)
        {
            warnings.Add($"{name} (used by {string.Join(", ", users)}) is not a class of the diagram; it is used as written.");
        }
    }

    public List<TypeModel> Types { get; } = [];

    /// <summary>Resolves the classes, interfaces and enumerations of a class diagram.</summary>
    /// <param name="multipleInheritance">The language allows several base classes (C++); others keep the first.</param>
    public static CodeModel Build(ClassDiagramModel diagram, bool multipleInheritance, List<string> warnings) =>
        new(diagram, multipleInheritance, warnings);

    /// <summary>The type of a class of the diagram, by its name as written in a type (e.g. an attribute's).</summary>
    public TypeModel? Find(string name) => _byName.GetValueOrDefault(name);

    private TypeModel? Find(UmlClass? cls) => cls is null ? null : Types.Find(t => t.Class == cls);

    private static IEnumerable<string> Documentation(ClassDiagramModel diagram, UmlClass cls)
    {
        var lines = new List<string>();
        foreach (var attachment in diagram.NoteAttachments.Where(a => a.TargetId == cls.Id))
        {
            if (diagram.FindNote(attachment.NoteId) is { } note && !string.IsNullOrWhiteSpace(note.Text))
            {
                if (lines.Count > 0)
                {
                    lines.Add(string.Empty);
                }

                lines.AddRange(note.Text.Replace("\r\n", "\n").Trim().Split('\n').Select(l => l.TrimEnd()));
            }
        }

        if (!string.IsNullOrWhiteSpace(cls.Stereotype))
        {
            if (lines.Count > 0)
            {
                lines.Add(string.Empty);
            }

            lines.Add($"Stereotype: «{cls.Stereotype.Trim()}»");
        }

        return lines;
    }

    private void AddRelationship(UmlRelationship relationship, TypeModel source, TypeModel target, List<string> warnings)
    {
        switch (relationship.Kind)
        {
            case RelationshipKind.Inheritance or RelationshipKind.Realization:
                if (source.IsEnum || target.IsEnum || source == target)
                {
                    warnings.Add($"{source.Name}: an enumeration cannot inherit or be inherited; {relationship.Kind} to {target.Name} is not generated.");
                }
                else if (target.IsInterface)
                {
                    AddOnce(source.Interfaces, target);
                }
                else if (source.IsInterface)
                {
                    warnings.Add($"{source.Name}: an interface cannot inherit from the class {target.Name}; the {relationship.Kind} is not generated.");
                }
                else
                {
                    AddOnce(source.BaseClasses, target);
                }

                break;

            case RelationshipKind.Dependency:
                AddOnce(source.Dependencies, target);
                break;

            case RelationshipKind.Association:
                // Navigable both ways: each end becomes a field of the class at the other end.
                AddEnd(source, target, relationship.TargetMultiplicity, composition: false, relationship.Label);
                if (source != target)
                {
                    AddEnd(target, source, relationship.SourceMultiplicity, composition: false, relationship.Label);
                }

                break;

            default:
                // Directed associations point at the target; aggregations and compositions have the whole as source.
                AddEnd(source, target, relationship.TargetMultiplicity, relationship.Kind == RelationshipKind.Composition, relationship.Label);
                break;
        }

        static void AddOnce(List<TypeModel> list, TypeModel type)
        {
            if (!list.Contains(type))
            {
                list.Add(type);
            }
        }
    }

    /// <summary>
    /// Adds a field for an association end, named after the class at the other end ("customer", "orderLines"), unless
    /// an attribute of the owner already has that class as (part of) its type, e.g. <c>observers: List&lt;Observer&gt;</c>.
    /// </summary>
    private void AddEnd(TypeModel owner, TypeModel target, string? multiplicity, bool composition, string? label)
    {
        if (owner.IsEnum || owner.Attributes.Any(a => TypeExpression.Parse(a.Attribute.Type)?.Names().Any(n => Find(n) == target) == true))
        {
            return;
        }

        var parsed = Multiplicity.Parse(multiplicity);
        var words = Naming.Words(target.Name);
        var baseName = Naming.Camel(target.Name);
        if (parsed.IsMany && words.Count > 0)
        {
            baseName = Naming.Camel(string.Join(' ', words.Take(words.Count - 1).Append(Naming.Plural(words[^1].ToLowerInvariant()))));
        }

        var taken = owner.Attributes.Select(a => Naming.Snake(a.Attribute.Name)).Concat(owner.Ends.Select(e => Naming.Snake(e.Name))).ToHashSet();
        var name = baseName;
        for (var i = 2; taken.Contains(Naming.Snake(name)); i++)
        {
            name = baseName + i;
        }

        owner.Ends.Add(new AssociationEnd(target, name, parsed.IsMany, parsed.IsOptional, composition, string.IsNullOrWhiteSpace(label) ? null : label.Trim()));
    }

    /// <summary>
    /// Marks overrides (and makes the overridden operations virtual), and adds the operations a class has to implement:
    /// abstract operations of its base classes and operations of its interfaces that nothing in the class hierarchy
    /// implements. Abstract classes declare the interface operations they leave open as abstract.
    /// </summary>
    private static void ResolveOperations(TypeModel type)
    {
        var ancestors = type.Ancestors().ToList();
        var interfaceOperations = type.AllInterfaces().SelectMany(i => i.Operations).Where(o => !o.IsStatic).ToList();

        foreach (var operation in type.Operations.Where(o => !o.IsStatic && !o.IsConstructor))
        {
            var overridden = ancestors.SelectMany(a => a.Operations)
                .FirstOrDefault(o => o.Key == operation.Key && !o.IsStatic && !o.IsConstructor && o.Operation.Visibility != UmlVisibility.Private);
            if (overridden is not null)
            {
                operation.IsOverride = true;
                overridden.IsVirtual |= !overridden.IsAbstract;
            }

            operation.ImplementsInterface = interfaceOperations.Any(o => o.Key == operation.Key);
        }

        // What the class and its base classes implement, and the abstract operations still open along the way.
        var implemented = type.Operations.Where(o => !o.IsAbstract).Select(o => o.Key).ToHashSet();
        var open = new List<OperationModel>();
        foreach (var ancestor in ancestors)
        {
            foreach (var operation in ancestor.Operations.Where(o => !o.IsStatic && !o.IsConstructor))
            {
                if (operation.IsAbstract && !implemented.Contains(operation.Key) && open.All(o => o.Key != operation.Key))
                {
                    open.Add(operation);
                }
                else if (!operation.IsAbstract)
                {
                    implemented.Add(operation.Key);
                }
            }
        }

        open.RemoveAll(o => implemented.Contains(o.Key));
        var declared = type.Operations.Select(o => o.Key).ToHashSet();
        foreach (var operation in open.Concat(interfaceOperations))
        {
            if (implemented.Contains(operation.Key) || !declared.Add(operation.Key) ||
                (type.IsAbstract && ancestors.SelectMany(a => a.Operations).Any(o => o.Key == operation.Key)))
            {
                continue;
            }

            var fromInterface = interfaceOperations.Contains(operation);
            type.Operations.Add(new OperationModel(operation.Operation, isConstructor: false, isImplicit: true)
            {
                IsAbstract = type.IsAbstract,
                IsOverride = !fromInterface,
                ImplementsInterface = fromInterface,
            });
        }
    }

    /// <summary>Adds the attributes of the implemented interfaces that the class hierarchy does not have (for C#).</summary>
    private static void AddInterfaceAttributes(TypeModel type)
    {
        var have = type.Ancestors().Prepend(type).SelectMany(t => t.Attributes).Select(a => Naming.Snake(a.Attribute.Name)).ToHashSet();
        foreach (var attribute in type.AllInterfaces().SelectMany(i => i.Class.Attributes).Where(a => !a.IsStatic))
        {
            if (have.Add(Naming.Snake(attribute.Name)))
            {
                type.Attributes.Add(new AttributeModel(attribute, IsImplicit: true));
            }
        }
    }
}
