using UmlWizard.CodeGeneration;

namespace UmlWizard.CodeGeneration.Java;

/// <summary>The Java language, offered by the Generate Code dialog when this plug-in is in the plugins folder.</summary>
public sealed class JavaCodeGenerator : ICodeGenerator
{
    public string Id => "Java";

    public string DisplayName => "Java";

    public NamespaceKind NamespaceKind => NamespaceKind.Package;

    public bool MultipleInheritance => false;

    public string DefaultNamespace(string projectIdentifier) => projectIdentifier.ToLowerInvariant();

    public IEnumerable<GeneratedFile> Generate(CodeModel model, CodeGenerationOptions options, IList<string> warnings)
    {
        var writer = new JavaWriter(model, options, warnings);
        return model.Types.SelectMany(writer.Generate);
    }
}
