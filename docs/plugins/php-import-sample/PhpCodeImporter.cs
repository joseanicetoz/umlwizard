using UmlWizard.CodeImport;

namespace Acme.UmlWizard.PhpImport;

/// <summary>
/// Reads PHP 8 declarations: classes, abstract classes, interfaces, traits («trait») and enumerations with their
/// properties, constants, methods and constructors (with promoted properties), in their namespaces. Method bodies are
/// skipped.
/// </summary>
public sealed class PhpCodeImporter : ICodeImporter
{
    public string Id => "Php";                                         // unique among code importers

    public string DisplayName => "PHP";                                // shown in the Import Source Folder dialog

    public IReadOnlyList<string> FileExtensions { get; } = [".php"];  // the files it reads, with the dot

    /// <summary>The tagged value <c>php.final = true</c>: a <c>final</c> class or method.</summary>
    public const string FinalTag = "php.final";

    /// <summary>The tagged value <c>php.readonly = true</c>: a <c>readonly</c> class or property.</summary>
    public const string ReadonlyTag = "php.readonly";

    public CodeImportParseResult Parse(IReadOnlyList<SourceFile> files)
    {
        var loss = new Losses();
        var types = new List<ImportedType>();
        foreach (var file in files)
        {
            try
            {
                new PhpParser(PhpTokenizer.Tokenize(file.Text), types, loss).ParseFile();
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
        public int UnreadableFiles, Unreadable, Functions, Attributes, TraitRules, Hooks, ParameterDetails, ArrayProperties;

        public List<string> Notes()
        {
            var notes = new List<string>();
            CodeImporter.Add(notes, UnreadableFiles, "file could not be read completely; its declarations were read as far as possible",
                "files could not be read completely; their declarations were read as far as possible");
            CodeImporter.Add(notes, Unreadable, "declaration could not be read and was skipped", "declarations could not be read and were skipped");
            CodeImporter.Add(notes, Functions, "function outside a class was left out", "functions outside classes were left out");
            CodeImporter.Add(notes, Attributes, "PHP attribute (e.g. #[ORM\\Entity]) was left out", "PHP attributes (e.g. #[ORM\\Entity]) were left out");
            CodeImporter.Add(notes, TraitRules, "trait conflict rule (insteadof, as) was left out", "trait conflict rules (insteadof, as) were left out");
            CodeImporter.Add(notes, Hooks, "property hook (get, set) was left out", "property hooks (get, set) were left out");
            CodeImporter.Add(notes, ParameterDetails, "parameter lost its default value, & (by reference) or ... (variadic)",
                "parameters lost their default values, & (by reference) or ... (variadic)");
            CodeImporter.Add(notes, ArrayProperties,
                "array property is shown as an attribute, not an association: PHP does not declare what an array holds",
                "array properties are shown as attributes, not associations: PHP does not declare what an array holds");
            return notes;
        }
    }
}
