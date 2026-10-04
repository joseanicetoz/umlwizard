using CommunityToolkit.Mvvm.ComponentModel;
using UmlWizard.App.Resources;
using UmlWizard.Core.Model;

namespace UmlWizard.App.ViewModels.Dialogs;

public sealed partial class RelationshipEditorViewModel : DialogViewModel
{
    public RelationshipEditorViewModel(UmlRelationship relationship, string sourceName, string targetName)
    {
        Kind = relationship.Kind;
        Label = relationship.Label;
        SourceMultiplicity = relationship.SourceMultiplicity;
        TargetMultiplicity = relationship.TargetMultiplicity;
        SourceName = sourceName;
        TargetName = targetName;
    }

    public override string Title => Strings.Dialog_EditRelationship_Title;

    public string SourceName { get; }

    public string TargetName { get; }

    [ObservableProperty]
    public partial RelationshipKind Kind { get; set; }

    [ObservableProperty]
    public partial string? Label { get; set; }

    [ObservableProperty]
    public partial string? SourceMultiplicity { get; set; }

    [ObservableProperty]
    public partial string? TargetMultiplicity { get; set; }

    public void ApplyTo(UmlRelationship relationship)
    {
        relationship.Kind = Kind;
        relationship.Label = AttributeRowViewModel.NullIfBlank(Label);
        relationship.SourceMultiplicity = AttributeRowViewModel.NullIfBlank(SourceMultiplicity);
        relationship.TargetMultiplicity = AttributeRowViewModel.NullIfBlank(TargetMultiplicity);
    }
}
