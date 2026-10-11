using System.Globalization;
using Avalonia.Controls.Templates;
using UmlWizard.App.Resources;

namespace UmlWizard.App;

/// <summary>
/// Text of the user interface that is put together at run time: the names of enum values and formatted messages. The
/// text itself is in <c>Resources/Strings.resx</c>, and its translations in <c>Resources/Strings.&lt;culture&gt;.resx</c>.
/// </summary>
public static class Loc
{
    /// <summary>
    /// The languages the user interface can be shown in, as culture names with each language's name in itself. English
    /// is <c>Strings.resx</c>; every other one needs a <c>Strings.&lt;culture&gt;.resx</c>.
    /// </summary>
    public static IReadOnlyList<(string Culture, string Name)> DisplayLanguages { get; } =
    [
        ("en", "English"),
        ("id", "Bahasa Indonesia"),
        ("ms", "Bahasa Melayu"),
        ("de", "Deutsch"),
        ("es", "Español"),
        ("fil", "Filipino"),
        ("fr", "Français"),
    ];

    /// <summary>The culture name of the language the user interface is shown in; see <see cref="SetDisplayLanguage"/>.</summary>
    public static string DisplayLanguage { get; private set; } = "en";

    /// <summary>
    /// Shows the user interface in a language of <see cref="DisplayLanguages"/>, English for any other. Called at
    /// start-up, before the first window is made: text already shown does not change. Numbers and dates keep the
    /// operating system's format.
    /// </summary>
    public static void SetDisplayLanguage(string? culture)
    {
        DisplayLanguage = DisplayLanguages.Any(l => l.Culture == culture) ? culture! : "en";
        var uiCulture = CultureInfo.GetCultureInfo(DisplayLanguage);

        // Set even for English, so that a German operating system does not pick Strings.de.resx by itself.
        Strings.Culture = uiCulture;
        CultureInfo.CurrentUICulture = uiCulture;
        CultureInfo.DefaultThreadCurrentUICulture = uiCulture;
    }

    /// <summary>
    /// The display name of an enum value, e.g. of a <c>RelationshipKind</c>: the resource
    /// <c>Enum_&lt;type&gt;_&lt;value&gt;</c>, or the value's own name when there is none.
    /// </summary>
    public static string Of(Enum value) =>
        Strings.ResourceManager.GetString($"Enum_{value.GetType().Name}_{value}", Strings.Culture) ?? value.ToString();

    /// <summary>Fills in the placeholders of a resource text for the current culture.</summary>
    public static string Format(string format, params object?[] args) => string.Format(CultureInfo.CurrentCulture, format, args);
}
