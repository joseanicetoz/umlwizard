using CommunityToolkit.Mvvm.ComponentModel;
using UmlWizard.App.Resources;
using UmlWizard.Core.Model;

namespace UmlWizard.App.ViewModels.Dialogs;

/// <param name="allowEmpty">Accept an empty text, e.g. to remove an optional label.</param>
public sealed partial class TextInputViewModel(string title, string prompt, string text, bool allowEmpty = false) : DialogViewModel
{
    public override string Title => title;

    public string Prompt => prompt;

    [ObservableProperty]
    public partial string Text { get; set; } = text;

    protected override string? Validate() => !allowEmpty && string.IsNullOrWhiteSpace(Text) ? Strings.Dialog_TextInput_NeedsValue : null;
}
