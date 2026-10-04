using UmlWizard.CodeGeneration;

namespace UmlWizard.CodeGeneration.TypeScript;

/// <summary>The TypeScript language, offered by the Generate Code dialog when this plug-in is in the plugins folder.</summary>
public sealed class TypeScriptCodeGenerator : ICodeGenerator
{
    public string Id => "TypeScript";

    public string DisplayName => "TypeScript";

    /// <summary>ES modules: the namespace becomes the folders of the files, e.g. <c>shop/domain/Order.ts</c>.</summary>
    public NamespaceKind NamespaceKind => NamespaceKind.Module;

    public bool MultipleInheritance => false;

    public string DefaultNamespace(string projectIdentifier) => projectIdentifier.ToLowerInvariant();

    public IEnumerable<GeneratedFile> Generate(CodeModel model, CodeGenerationOptions options, IList<string> warnings)
    {
        var writer = new TypeScriptWriter(model, options, warnings);
        return model.Types.SelectMany(writer.Generate);
    }
}
