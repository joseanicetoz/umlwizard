using CommunityToolkit.Mvvm.ComponentModel;
using UmlWizard.App.Resources;
using UmlWizard.Core.Model;

namespace UmlWizard.App.ViewModels.Dialogs;

/// <summary>Text, alignment and font of a text box or a note.</summary>
public sealed partial class TextBoxEditorViewModel : DialogViewModel
{
    /// <param name="font">The text box's own font; <c>null</c> when it uses the diagram font.</param>
    /// <param name="diagramFont">The diagram font (<c>null</c> is the default font).</param>
    public TextBoxEditorViewModel(TextElement textBox, DiagramFont? font, DiagramFont? diagramFont)
    {
        Title = textBox is NoteElement ? Strings.Dialog_EditNote_Title : Strings.Dialog_EditTextBox_Title;
        Text = textBox.Text;
        HorizontalAlignment = textBox.HorizontalAlignment;
        VerticalAlignment = textBox.VerticalAlignment;
        Font = new FontEditorViewModel(Strings.Dialog_Font_Title, font, diagramFont, Strings.Dialog_Font_UseDiagramFont);
    }

    public override string Title { get; }

    [ObservableProperty]
    public partial string? Text { get; set; }

    [ObservableProperty]
    public partial HorizontalTextAlignment HorizontalAlignment { get; set; }

    [ObservableProperty]
    public partial VerticalTextAlignment VerticalAlignment { get; set; }

    public FontEditorViewModel Font { get; }

    public void ApplyTo(TextElement textBox)
    {
        textBox.Text = (Text ?? string.Empty).Replace("\r\n", "\n").TrimEnd();
        textBox.HorizontalAlignment = HorizontalAlignment;
        textBox.VerticalAlignment = VerticalAlignment;
    }

    protected override string? Validate() => Font.TryAccept() ? null : Font.ErrorMessage;
}
