using Avalonia.Controls;

namespace UmlWizard.App.Controls;

/// <summary>
/// The tab strip of the open diagrams. Unlike a plain <see cref="TabControl"/>, which always keeps a tab selected, it
/// can have no tab selected: the explorer shows a diagram that is not open (see <c>MainWindowViewModel</c>).
/// </summary>
public sealed class DocumentTabControl : TabControl
{
    static DocumentTabControl() =>
        SelectionModeProperty.OverrideDefaultValue<DocumentTabControl>(SelectionMode.Single);

    /// <summary>Styled like any tab control.</summary>
    protected override Type StyleKeyOverride => typeof(TabControl);
}
