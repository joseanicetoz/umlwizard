using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Reflection;
using System.Runtime.InteropServices;
using UmlWizard.App.Resources;
using UmlWizard.App.Services;
using UmlWizard.CodeGeneration.Plugins;
using UmlWizard.CodeImport.Plugins;

namespace UmlWizard.App.ViewModels;

/// <summary>Application name, version, the environment it runs in and the installed language plug-ins, for the About dialog.</summary>
public sealed partial class AboutViewModel(
    IClipboardService clipboard, CodeGeneratorCatalog? codeGenerators = null, CodeImporterCatalog? codeImporters = null) : ObservableObject
{
    private static readonly Assembly AppAssembly = typeof(AboutViewModel).Assembly;

    public string ProductName { get; } =
        AppAssembly.GetCustomAttribute<AssemblyProductAttribute>()?.Product ?? Strings.App_Name;

    public string Description { get; } = Strings.About_Description;

    /// <summary>The informational version without source-control metadata (the part after '+').</summary>
    public string Version { get; } = FormatVersion(
        AppAssembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? AppAssembly.GetName().Version?.ToString());

    public string OperatingSystem { get; } = RuntimeInformation.OSDescription.Trim();

    public string Architecture { get; } = RuntimeInformation.OSArchitecture == RuntimeInformation.ProcessArchitecture
        ? RuntimeInformation.OSArchitecture.ToString().ToLowerInvariant()
        : Loc.Format(
            Strings.About_ProcessArchitecture,
            RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant(),
            RuntimeInformation.OSArchitecture.ToString().ToLowerInvariant());

    public string Runtime { get; } = RuntimeInformation.FrameworkDescription;

    public string UiFramework { get; } = $"Avalonia {FormatVersion(typeof(Avalonia.Application).Assembly
        .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion)}";

    /// <summary>UML Wizard's own licence, from the <c>Licence</c> assembly metadata in the project file.</summary>
    public string Licence { get; } = Metadata("Licence");

    /// <summary>UML Wizard's web address, from the <c>Website</c> assembly metadata in the project file.</summary>
    public string Website { get; } = Metadata("Website");

    /// <summary>The website as a link, or <c>null</c> while it is not a web address (e.g. still the placeholder).</summary>
    public Uri? WebsiteUri => WebLink(Website);

    public bool HasWebsiteLink => WebsiteUri is not null;

    /// <summary>The UML version whose notation is drawn; only part of it is supported (see README, "UML support").</summary>
    public string UmlSupport { get; } = Strings.About_UmlSupport;

    /// <summary>The main third-party components, for the Acknowledgements section; the notices file lists them all.</summary>
    public IReadOnlyList<Acknowledgement> Acknowledgements { get; } =
    [
        new("Material Design Icons (Pictogrammers)", "Apache License 2.0", "https://pictogrammers.com/library/mdi/"),
        new("Material.Icons", "MIT", "https://github.com/SKProCH/Material.Icons"),
        new("Avalonia", "MIT", "https://avaloniaui.net/"),
        new("Inter font", "SIL Open Font License 1.1", "https://github.com/rsms/inter"),
        new("SkiaSharp and Skia", "MIT, BSD 3-Clause", "https://github.com/mono/SkiaSharp"),
        new("HarfBuzzSharp and HarfBuzz", "MIT", "https://github.com/harfbuzz/harfbuzz"),
        new(".NET Community Toolkit", "MIT", "https://github.com/CommunityToolkit/dotnet"),
        new("YamlDotNet", "MIT", "https://github.com/aaubry/YamlDotNet"),
        new("Roslyn (.NET Compiler Platform)", "MIT", "https://github.com/dotnet/roslyn"),
    ];

    /// <summary>The installed code generators and code importers, by language, generation before import.</summary>
    public IReadOnlyList<LanguagePlugin> LanguagePlugins { get; } =
    [
        .. (codeGenerators?.Generators ?? []).Select(g => LanguagePlugin.Of(g, g.DisplayName, Strings.About_Languages_Generation))
            .Concat((codeImporters?.Importers ?? []).Select(i => LanguagePlugin.Of(i, i.DisplayName, Strings.About_Languages_Import)))
            .OrderBy(p => p.Language, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(p => p.Kind == Strings.About_Languages_Generation ? 0 : 1),
    ];

    public bool HasLanguagePlugins => LanguagePlugins.Count > 0;

    /// <summary>Plug-ins that could not be loaded, and why.</summary>
    public IReadOnlyList<string> PluginProblems { get; } =
        [.. (codeGenerators?.Problems ?? []).Concat(codeImporters?.Problems ?? []).Distinct()];

    public bool HasPluginProblems => PluginProblems.Count > 0;

    /// <summary>The full license texts of every third-party component, installed next to the application.</summary>
    public static string NoticesPath { get; } = Path.Combine(AppContext.BaseDirectory, "THIRD-PARTY-NOTICES.txt");

    public bool HasNotices => File.Exists(NoticesPath);

    public string Title => Loc.Format(Strings.About_Title, ProductName);

    /// <summary>All details as plain text, e.g. for bug reports.</summary>
    public string Details => string.Join(
        '\n',
        $"{ProductName} {Version}",
        Line(Strings.About_Uml, UmlSupport),
        Line(Strings.About_OperatingSystem, OperatingSystem),
        Line(Strings.About_Architecture, Architecture),
        Line(Strings.About_Runtime, Runtime),
        Line(Strings.About_UiFramework, UiFramework),
        Line(Strings.About_Licence, Licence),
        Line(Strings.About_Website, Website),
        Line(Strings.About_Languages, HasLanguagePlugins ? string.Join("; ", LanguagePlugins) : Strings.About_Languages_None));

    [ObservableProperty]
    public partial bool Copied { get; set; }

    [RelayCommand]
    private async Task CopyDetails()
    {
        await clipboard.SetTextAsync(Details);
        Copied = true;
    }

    /// <summary>An http(s) address as a link, or <c>null</c> for anything else.</summary>
    internal static Uri? WebLink(string text) =>
        Uri.TryCreate(text, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
            ? uri
            : null;

    private static string Metadata(string key) =>
        AppAssembly.GetCustomAttributes<AssemblyMetadataAttribute>().FirstOrDefault(a => a.Key == key)?.Value is { Length: > 0 } value
            ? value
            : Strings.About_NotSpecified;

    private static string FormatVersion(string? version) =>
        string.IsNullOrWhiteSpace(version) ? Strings.About_UnknownVersion : version.Split('+')[0];

    private static string Line(string label, string value) => Loc.Format(Strings.About_DetailLine, label, value);
}
