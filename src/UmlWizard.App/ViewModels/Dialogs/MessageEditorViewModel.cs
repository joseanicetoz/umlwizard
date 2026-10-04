using CommunityToolkit.Mvvm.ComponentModel;
using UmlWizard.App.Resources;
using UmlWizard.Core.Model;

namespace UmlWizard.App.ViewModels.Dialogs;

public sealed partial class MessageEditorViewModel : DialogViewModel
{
    public MessageEditorViewModel(SequenceMessage message, IReadOnlyList<LifelineOption> lifelines)
    {
        Lifelines = lifelines;
        Text = message.Text;
        Kind = message.Kind;
        Source = lifelines.FirstOrDefault(l => l.Id == message.SourceId);
        Target = lifelines.FirstOrDefault(l => l.Id == message.TargetId);
    }

    public override string Title => Strings.Dialog_EditMessage_Title;

    public IReadOnlyList<LifelineOption> Lifelines { get; }

    [ObservableProperty]
    public partial string? Text { get; set; }

    [ObservableProperty]
    public partial MessageKind Kind { get; set; }

    [ObservableProperty]
    public partial LifelineOption? Source { get; set; }

    [ObservableProperty]
    public partial LifelineOption? Target { get; set; }

    public void ApplyTo(SequenceMessage message)
    {
        message.Text = Text?.Trim() ?? string.Empty;
        message.Kind = Kind;
        message.SourceId = Source!.Id;
        message.TargetId = Target!.Id;
    }

    protected override string? Validate() =>
        Source is null || Target is null ? Strings.Dialog_EditMessage_NeedsLifelines : null;
}
