using CommunityToolkit.Mvvm.ComponentModel;
using UmlWizard.App.Resources;

namespace UmlWizard.App.ViewModels.Dialogs;

/// <summary>Chooses the folder a diagram or folder is moved to.</summary>
public sealed partial class MoveToViewModel : DialogViewModel
{
    public MoveToViewModel(string itemName, IReadOnlyList<MoveTarget> targets, MoveTarget? current)
    {
        ItemName = itemName;
        Targets = targets;
        Current = current;
        Selected = current;
    }

    public override string Title => Strings.Dialog_MoveTo_Title;

    public string ItemName { get; }

    public string Prompt => Loc.Format(Strings.Dialog_MoveTo_Prompt, ItemName);

    public IReadOnlyList<MoveTarget> Targets { get; }

    /// <summary>Where the item is now.</summary>
    public MoveTarget? Current { get; }

    [ObservableProperty]
    public partial MoveTarget? Selected { get; set; }

    protected override string? Validate() => Selected is null ? Strings.Dialog_MoveTo_NeedsDestination : null;
}
