using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using UmlWizard.App.Resources;
using UmlWizard.Core.Model;

namespace UmlWizard.App.ViewModels.Dialogs;

/// <summary>Edits a copy of a class; <see cref="ApplyTo"/> writes the result back.</summary>
public sealed partial class ClassEditorViewModel : DialogViewModel
{
    public ClassEditorViewModel(UmlClass cls)
    {
        Name = cls.Name;
        Kind = cls.Kind;
        Stereotype = cls.Stereotype;
        TaggedValues = new TaggedValuesEditorViewModel(cls.TaggedValues);
        SelectedTaggedValues = TaggedValues;
        TaggedValuesOwner = Strings.Dialog_EditClass_TheClass;
        foreach (var attribute in cls.Attributes)
        {
            Attributes.Add(new AttributeRowViewModel(attribute, RemoveAttribute, ShowTaggedValues));
        }

        foreach (var operation in cls.Operations)
        {
            Operations.Add(new OperationRowViewModel(operation, RemoveOperation, ShowTaggedValues));
        }
    }

    public override string Title => Strings.Dialog_EditClass_Title;

    [ObservableProperty]
    public partial string Name { get; set; }

    [ObservableProperty]
    public partial ClassifierKind Kind { get; set; }

    [ObservableProperty]
    public partial string? Stereotype { get; set; }

    public ObservableCollection<AttributeRowViewModel> Attributes { get; } = [];

    public ObservableCollection<OperationRowViewModel> Operations { get; } = [];

    /// <summary>The class's own tagged values.</summary>
    public TaggedValuesEditorViewModel TaggedValues { get; }

    /// <summary>The tagged values shown below the member lists: the class's, or those of the member last picked.</summary>
    [ObservableProperty]
    public partial TaggedValuesEditorViewModel SelectedTaggedValues { get; set; }

    /// <summary>Whose tagged values are shown, e.g. "the class" or "attribute name".</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TaggedValuesHeader))]
    public partial string TaggedValuesOwner { get; set; }

    public string TaggedValuesHeader => Loc.Format(Strings.Dialog_EditClass_TaggedValuesOf, TaggedValuesOwner);

    [RelayCommand]
    private void AddAttribute() =>
        Attributes.Add(new AttributeRowViewModel(new UmlAttribute { Name = Loc.Format(Strings.Dialog_EditClass_NewAttributeName, Attributes.Count + 1) }, RemoveAttribute, ShowTaggedValues));

    [RelayCommand]
    private void AddOperation() =>
        Operations.Add(new OperationRowViewModel(new UmlOperation { Name = Loc.Format(Strings.Dialog_EditClass_NewOperationName, Operations.Count + 1) }, RemoveOperation, ShowTaggedValues));

    public void ApplyTo(UmlClass cls)
    {
        cls.Name = Name.Trim();
        cls.Kind = Kind;
        cls.Stereotype = string.IsNullOrWhiteSpace(Stereotype) ? null : Stereotype.Trim();
        cls.Attributes = Attributes.Select(a => a.ToModel()).ToList();
        cls.Operations = Operations.Select(o => o.ToModel()).ToList();
        cls.TaggedValues = TaggedValues.ToModel();
    }

    protected override string? Validate()
    {
        if (string.IsNullOrWhiteSpace(Name))
        {
            return Strings.Dialog_EditClass_NeedsName;
        }

        if (Attributes.Any(a => string.IsNullOrWhiteSpace(a.Name)))
        {
            return Strings.Dialog_EditClass_AttributeNeedsName;
        }

        return Operations.Any(o => string.IsNullOrWhiteSpace(o.Name)) ? Strings.Dialog_EditClass_OperationNeedsName : null;
    }

    [RelayCommand]
    private void ShowClassTaggedValues()
    {
        SelectedTaggedValues = TaggedValues;
        TaggedValuesOwner = Strings.Dialog_EditClass_TheClass;
    }

    private void ShowTaggedValues(AttributeRowViewModel row)
    {
        SelectedTaggedValues = row.TaggedValues;
        TaggedValuesOwner = Loc.Format(Strings.Dialog_EditClass_AttributeOwner, row.Name.Trim());
    }

    private void ShowTaggedValues(OperationRowViewModel row)
    {
        SelectedTaggedValues = row.TaggedValues;
        TaggedValuesOwner = Loc.Format(Strings.Dialog_EditClass_OperationOwner, row.Name.Trim());
    }

    private void RemoveAttribute(AttributeRowViewModel row)
    {
        Attributes.Remove(row);
        if (SelectedTaggedValues == row.TaggedValues)
        {
            ShowClassTaggedValues();
        }
    }

    private void RemoveOperation(OperationRowViewModel row)
    {
        Operations.Remove(row);
        if (SelectedTaggedValues == row.TaggedValues)
        {
            ShowClassTaggedValues();
        }
    }
}
