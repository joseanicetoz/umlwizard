using CommunityToolkit.Mvvm.ComponentModel;
using UmlWizard.App.Resources;
using UmlWizard.Core.Model;

namespace UmlWizard.App.ViewModels.Dialogs;

public sealed partial class NewDiagramViewModel : DialogViewModel
{
    public NewDiagramViewModel(string name, DiagramType type)
    {
        Name = name;
        Type = type;
    }

    public override string Title => Strings.Dialog_NewDiagram_Title;

    [ObservableProperty]
    public partial string Name { get; set; }

    [ObservableProperty]
    public partial DiagramType Type { get; set; }

    protected override string? Validate() => string.IsNullOrWhiteSpace(Name) ? Strings.Dialog_NewDiagram_NeedsName : null;
}
