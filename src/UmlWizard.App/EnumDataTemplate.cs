using Avalonia.Controls;
using Avalonia.Controls.Templates;

namespace UmlWizard.App;

/// <summary>Shows enum values, e.g. the items of a combo box, by their display name (<see cref="Loc.Of"/>).</summary>
public sealed class EnumDataTemplate : IDataTemplate
{
    public bool Match(object? data) => data is Enum;

    public Control? Build(object? param) => new TextBlock { Text = param is Enum value ? Loc.Of(value) : null };
}
