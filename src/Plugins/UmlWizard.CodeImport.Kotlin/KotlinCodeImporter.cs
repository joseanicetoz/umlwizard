using UmlWizard.CodeImport;

namespace UmlWizard.CodeImport.Kotlin;

/// <summary>
/// Reads Kotlin declarations: classes, abstract and sealed classes, interfaces, enumerations and objects, with their
/// properties (also those of primary constructors), functions and constructors, in their packages. Data classes get
/// «data», objects «object», sealed classes and interfaces «sealed», value classes «value»; the members of a companion
/// object are static. Function bodies and initializers are skipped.
/// </summary>
public sealed class KotlinCodeImporter : ICodeImporter
{
    public string Id => "Kotlin";

    public string DisplayName => "Kotlin";

    public IReadOnlyList<string> FileExtensions { get; } = [".kt"];

    public CodeImportParseResult Parse(IReadOnlyList<SourceFile> files)
    {
        var loss = new Losses();
        var types = new List<ImportedType>();
        foreach (var file in files)
        {
            try
            {
                new KotlinParser(KotlinTokenizer.Tokenize(file.Text), types, loss).ParseFile();
            }
            catch (ArgumentOutOfRangeException)
            {
                // Code the parser does not expect: keep what was read, and go on with the next file.
                loss.UnreadableFiles++;
            }
        }

        return new CodeImportParseResult(types, loss.Notes());
    }

    /// <summary>What the import left out, counted while reading and reported once.</summary>
    internal sealed class Losses
    {
        public int UnreadableFiles, Unreadable, TopLevel, Extensions, Annotations, AnnotationClasses, InitBlocks, TypeAliases, Accessors, ParameterDetails, TypeBounds;

        public List<string> Notes()
        {
            var notes = new List<string>();
            CodeImporter.Add(notes, UnreadableFiles, "file could not be read completely; its declarations were read as far as possible",
                "files could not be read completely; their declarations were read as far as possible");
            CodeImporter.Add(notes, Unreadable, "declaration could not be read and was skipped", "declarations could not be read and were skipped");
            CodeImporter.Add(notes, TopLevel, "function or property outside a class was left out", "functions and properties outside classes were left out");
            CodeImporter.Add(notes, Extensions, "extension function or property was left out", "extension functions and properties were left out");
            CodeImporter.Add(notes, Annotations, "annotation (e.g. @Serializable) was left out", "annotations (e.g. @Serializable) were left out");
            CodeImporter.Add(notes, AnnotationClasses, "annotation class was left out", "annotation classes were left out");
            CodeImporter.Add(notes, InitBlocks, "init block was left out", "init blocks were left out");
            CodeImporter.Add(notes, TypeAliases, "type alias (typealias) was left out", "type aliases (typealias) were left out");
            CodeImporter.Add(notes, Accessors, "custom getter or setter was left out", "custom getters and setters were left out");
            CodeImporter.Add(notes, ParameterDetails, "parameter lost its default value or vararg", "parameters lost their default values or vararg");
            CodeImporter.Add(notes, TypeBounds, "bound of a type parameter (: … or where …) was left out", "bounds of type parameters (: … or where …) were left out");
            return notes;
        }
    }
}
