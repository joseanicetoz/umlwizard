using UmlWizard.CodeImport;

namespace UmlWizard.CodeImport.TypeScript;

/// <summary>
/// Reads TypeScript declarations: classes, abstract classes, interfaces and enumerations, with their properties,
/// accessors, methods and constructors (whose parameter properties are attributes), in their namespaces. Function
/// bodies and initializers are skipped.
/// </summary>
public sealed class TypeScriptCodeImporter : ICodeImporter
{
    public string Id => "TypeScript";

    public string DisplayName => "TypeScript";

    public IReadOnlyList<string> FileExtensions { get; } = [".ts", ".tsx", ".mts", ".cts"];

    public CodeImportParseResult Parse(IReadOnlyList<SourceFile> files)
    {
        var loss = new Losses();
        var types = new List<ImportedType>();
        foreach (var file in files)
        {
            try
            {
                new TypeScriptParser(TypeScriptTokenizer.Tokenize(file.Text), Path.GetFileNameWithoutExtension(file.Path), types, loss).ParseFile();
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
        public int UnreadableFiles, Unreadable, Functions, TypeAliases, Decorators, IndexSignatures, CallSignatures, StaticBlocks, ParameterDetails, TypeBounds;

        public List<string> Notes()
        {
            var notes = new List<string>();
            CodeImporter.Add(notes, UnreadableFiles, "file could not be read completely; its declarations were read as far as possible",
                "files could not be read completely; their declarations were read as far as possible");
            CodeImporter.Add(notes, Unreadable, "declaration could not be read and was skipped", "declarations could not be read and were skipped");
            CodeImporter.Add(notes, Functions, "function or variable outside a class was left out", "functions and variables outside classes were left out");
            CodeImporter.Add(notes, TypeAliases, "type alias (type X = …) was left out", "type aliases (type X = …) were left out");
            CodeImporter.Add(notes, Decorators, "decorator (e.g. @Component) was left out", "decorators (e.g. @Component) were left out");
            CodeImporter.Add(notes, IndexSignatures, "index signature ([key: string]: …) was left out", "index signatures ([key: string]: …) were left out");
            CodeImporter.Add(notes, CallSignatures, "call or construct signature of an interface was left out", "call and construct signatures of interfaces were left out");
            CodeImporter.Add(notes, StaticBlocks, "static block was left out", "static blocks were left out");
            CodeImporter.Add(notes, ParameterDetails, "parameter lost its default value, ? (optional) or ... (rest)",
                "parameters lost their default values, ? (optional) or ... (rest)");
            CodeImporter.Add(notes, TypeBounds, "constraint of a type parameter (extends …) was left out", "constraints of type parameters (extends …) were left out");
            return notes;
        }
    }
}
