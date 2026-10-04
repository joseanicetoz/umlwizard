using System.Globalization;
using Avalonia.Controls.Templates;
using UmlWizard.App.Resources;

namespace UmlWizard.App;

/// <summary>
/// Text of the user interface that is put together at run time: the names of enum values and formatted messages. The
/// text itself is in <c>Resources/Strings.resx</c>.
/// </summary>
public static class Loc
{
    /// <summary>
    /// The display name of an enum value, e.g. of a <c>RelationshipKind</c>: the resource
    /// <c>Enum_&lt;type&gt;_&lt;value&gt;</c>, or the value's own name when there is none.
    /// </summary>
    public static string Of(Enum value) =>
        Strings.ResourceManager.GetString($"Enum_{value.GetType().Name}_{value}", Strings.Culture) ?? value.ToString();

    /// <summary>Fills in the placeholders of a resource text for the current culture.</summary>
    public static string Format(string format, params object?[] args) => string.Format(CultureInfo.CurrentCulture, format, args);
}
