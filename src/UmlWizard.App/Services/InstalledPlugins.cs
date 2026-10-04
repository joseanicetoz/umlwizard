using UmlWizard.CodeGeneration.Plugins;
using UmlWizard.CodeImport.Plugins;
using UmlWizard.Core.Plugins;

namespace UmlWizard.App.Services;

/// <summary>
/// The installed plug-ins, each loaded once when first needed: its code generators (Generate Code) and code importers
/// (Import Source Folder). They are found in the <c>plugins</c> folder next to the application (the built-in ones)
/// and in the user's own <c>plugins</c> folder, which can be written to even where the application's folder cannot:
/// inside a macOS app bundle, whose signature a change would break, or a Linux installation under <c>/usr</c> or
/// <c>/opt</c>.
/// </summary>
public static class InstalledPlugins
{
    /// <summary>The plug-ins installed with the application.</summary>
    public static string ApplicationFolder { get; } = Path.Combine(AppContext.BaseDirectory, "plugins");

    /// <summary>
    /// The user's plug-ins, next to the settings: <c>%APPDATA%\UmlWizard\plugins</c> on Windows,
    /// <c>~/.config/UmlWizard/plugins</c> on macOS and Linux.
    /// </summary>
    public static string UserFolder { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "UmlWizard", "plugins");

    /// <summary>The folders plug-ins are loaded from, in order: the application's wins when two have the same plug-in.</summary>
    public static IReadOnlyList<string> Folders { get; } = [ApplicationFolder, UserFolder];

    private static readonly Lazy<PluginAssemblies> Assemblies = new(() => PluginAssemblies.Load(Folders));

    private static readonly Lazy<CodeGeneratorCatalog> Generators = new(() => CodeGeneratorCatalog.From(Assemblies.Value));

    private static readonly Lazy<CodeImporterCatalog> Importers = new(() => CodeImporterCatalog.From(Assemblies.Value));

    public static CodeGeneratorCatalog CodeGenerators => Generators.Value;

    public static CodeImporterCatalog CodeImporters => Importers.Value;
}
