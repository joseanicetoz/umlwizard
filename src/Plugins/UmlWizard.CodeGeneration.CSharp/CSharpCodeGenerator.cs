using UmlWizard.CodeGeneration;

namespace UmlWizard.CodeGeneration.CSharp;

/// <summary>The C# language, offered by the Generate Code dialog when this plug-in is in the plugins folder.</summary>
public sealed class CSharpCodeGenerator : ICodeGenerator
{
    public string Id => "CSharp";

    public string DisplayName => "C#";

    public NamespaceKind NamespaceKind => NamespaceKind.Namespace;

    public bool MultipleInheritance => false;

    public string DefaultNamespace(string projectIdentifier) => projectIdentifier;

    public IEnumerable<GeneratedFile> Generate(CodeModel model, CodeGenerationOptions options, IList<string> warnings)
    {
        var writer = new CSharpWriter(model, options, warnings);
        return model.Types.SelectMany(writer.Generate);
    }
}
