using UmlWizard.CodeGeneration;

namespace UmlWizard.CodeGeneration.Cpp;

/// <summary>The C++ language, offered by the Generate Code dialog when this plug-in is in the plugins folder.</summary>
public sealed class CppCodeGenerator : ICodeGenerator
{
    public string Id => "Cpp";

    public string DisplayName => "C++";

    public NamespaceKind NamespaceKind => NamespaceKind.Namespace;

    public bool MultipleInheritance => true;

    public string DefaultNamespace(string projectIdentifier) => projectIdentifier;

    public IEnumerable<GeneratedFile> Generate(CodeModel model, CodeGenerationOptions options, IList<string> warnings)
    {
        var writer = new CppWriter(model, options, warnings);
        return model.Types.SelectMany(writer.Generate);
    }
}
