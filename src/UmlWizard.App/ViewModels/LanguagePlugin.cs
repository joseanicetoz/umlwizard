using System.Reflection;
using UmlWizard.App.Resources;

namespace UmlWizard.App.ViewModels;

/// <summary>
/// A language plug-in, for the Languages tab of the About dialog: a code generator or a code importer, with the version
/// and provider of the DLL it comes from.
/// </summary>
/// <param name="Language">The language, as the plug-in names it (e.g. <c>C#</c>).</param>
/// <param name="Kind">What it does: generates code or imports it.</param>
/// <param name="Version">The DLL's informational version, without source-control metadata.</param>
/// <param name="Provider">The DLL's company (<c>&lt;Company&gt;</c> in its project file), or "Not specified" when it has none of its own.</param>
/// <param name="File">The DLL's file name.</param>
public sealed record LanguagePlugin(string Language, string Kind, string Version, string Provider, string File)
{
    /// <summary>A line for the About dialog's copied details.</summary>
    public override string ToString() => $"{Language} {Kind.ToLowerInvariant()} {Version} ({Provider}, {File})";

    /// <summary>Describes a plug-in object of the given kind from the assembly it was loaded from.</summary>
    public static LanguagePlugin Of(object plugin, string language, string kind)
    {
        var assembly = plugin.GetType().Assembly;
        var name = assembly.GetName();
        var version = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion?.Split('+')[0]
            ?? name.Version?.ToString()
            ?? Strings.About_UnknownVersion;

        // The SDK sets the company to the assembly name when a project does not give one: that is no provider.
        var company = assembly.GetCustomAttribute<AssemblyCompanyAttribute>()?.Company;
        var provider = string.IsNullOrWhiteSpace(company) || company == name.Name ? Strings.About_Languages_NoProvider : company;
        return new LanguagePlugin(language, kind, version, provider, $"{name.Name}.dll");
    }
}
