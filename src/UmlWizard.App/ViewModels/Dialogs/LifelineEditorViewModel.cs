using CommunityToolkit.Mvvm.ComponentModel;
using UmlWizard.App.Resources;
using UmlWizard.Core.Model;

namespace UmlWizard.App.ViewModels.Dialogs;

public sealed partial class LifelineEditorViewModel : DialogViewModel
{
    public LifelineEditorViewModel(Lifeline lifeline)
    {
        Name = lifeline.Name;
        Type = lifeline.Type;
        Kind = lifeline.Kind;
    }

    public override string Title => Strings.Dialog_EditLifeline_Title;

    [ObservableProperty]
    public partial string? Name { get; set; }

    [ObservableProperty]
    public partial string? Type { get; set; }

    [ObservableProperty]
    public partial LifelineKind Kind { get; set; }

    public void ApplyTo(Lifeline lifeline)
    {
        lifeline.Name = Name?.Trim() ?? string.Empty;
        lifeline.Type = AttributeRowViewModel.NullIfBlank(Type);
        lifeline.Kind = Kind;
    }

    protected override string? Validate() =>
        string.IsNullOrWhiteSpace(Name) && string.IsNullOrWhiteSpace(Type)
            ? Strings.Dialog_EditLifeline_NeedsNameOrType
            : null;
}
