using UmlWizard.CodeGeneration;

namespace UmlWizard.CodeGeneration.Kotlin;

/// <summary>The Kotlin language, offered by the Generate Code dialog when this plug-in is in the plugins folder.</summary>
public sealed class KotlinCodeGenerator : ICodeGenerator
{
    public string Id => "Kotlin";

    public string DisplayName => "Kotlin";

    public NamespaceKind NamespaceKind => NamespaceKind.Package;

    public bool MultipleInheritance => false;

    public string DefaultNamespace(string projectIdentifier) => projectIdentifier.ToLowerInvariant();

    public IEnumerable<GeneratedFile> Generate(CodeModel model, CodeGenerationOptions options, IList<string> warnings)
    {
        var writer = new KotlinWriter(model, options, warnings);
        return model.Types.SelectMany(writer.Generate);
    }
}
