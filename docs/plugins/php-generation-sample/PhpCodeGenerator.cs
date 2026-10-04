using UmlWizard.CodeGeneration;

namespace Acme.UmlWizard.PhpGeneration;

/// <summary>PHP 8.1+: one PSR-4 file per class, interface and enumeration.</summary>
public sealed class PhpCodeGenerator : ICodeGenerator
{
    public string Id => "Php";

    public string DisplayName => "PHP";

    public NamespaceKind NamespaceKind => NamespaceKind.Namespace;

    public bool MultipleInheritance => false;

    public string DefaultNamespace(string projectIdentifier) => projectIdentifier;

    public IEnumerable<GeneratedFile> Generate(CodeModel model, CodeGenerationOptions options, IList<string> warnings)
    {
        var writer = new PhpWriter(model, options, warnings);
        return model.Types.SelectMany(writer.Generate);
    }
}
