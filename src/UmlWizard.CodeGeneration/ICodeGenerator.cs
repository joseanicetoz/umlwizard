namespace UmlWizard.CodeGeneration;

/// <summary>
/// A target language of code generation. Implementations are found in the plug-in DLLs of the application's
/// <c>plugins</c> folder (see <see cref="Plugins.CodeGeneratorCatalog"/>); they need a public parameterless constructor.
/// </summary>
public interface ICodeGenerator
{
    /// <summary>Identifies the language, e.g. <c>CSharp</c>; also names the suggested output folder <c>Generated/&lt;Id&gt;</c>.</summary>
    string Id { get; }

    /// <summary>The language as shown in the Generate Code dialog, e.g. <c>C#</c>.</summary>
    string DisplayName { get; }

    /// <summary>What the language calls a namespace; labels the namespace box of the dialog.</summary>
    NamespaceKind NamespaceKind { get; }

    /// <summary>
    /// The language allows several base classes (C++). Otherwise the code model keeps the first base class of a class
    /// and warns about the others.
    /// </summary>
    bool MultipleInheritance { get; }

    /// <summary>The namespace suggested for a project, from the project name as an identifier (e.g. lower case for Java).</summary>
    string DefaultNamespace(string projectIdentifier);

    /// <summary>
    /// The files for the types of <paramref name="model"/>, with paths relative to the output folder using <c>/</c>.
    /// Anything that cannot be expressed in the language is reported in <paramref name="warnings"/>.
    /// </summary>
    IEnumerable<GeneratedFile> Generate(CodeModel model, CodeGenerationOptions options, IList<string> warnings);
}
