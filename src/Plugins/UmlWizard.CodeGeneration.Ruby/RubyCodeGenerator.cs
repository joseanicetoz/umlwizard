using UmlWizard.CodeGeneration;

namespace UmlWizard.CodeGeneration.Ruby;

/// <summary>The Ruby language, offered by the Generate Code dialog when this plug-in is in the plugins folder.</summary>
public sealed class RubyCodeGenerator : ICodeGenerator
{
    public string Id => "Ruby";

    public string DisplayName => "Ruby";

    public NamespaceKind NamespaceKind => NamespaceKind.Module;

    public bool MultipleInheritance => false;

    public string DefaultNamespace(string projectIdentifier) => projectIdentifier;

    public IEnumerable<GeneratedFile> Generate(CodeModel model, CodeGenerationOptions options, IList<string> warnings)
    {
        var writer = new RubyWriter(model, options, warnings);
        return model.Types.SelectMany(writer.Generate);
    }
}
