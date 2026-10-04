using CommunityToolkit.Mvvm.ComponentModel;
using UmlWizard.App.Resources;
using UmlWizard.App.ViewModels;
using UmlWizard.Core.Model;

namespace UmlWizard.App.ViewModels.Dialogs;

/// <summary>The descriptive information of a diagram: name, description, author and tagged values.</summary>
public sealed partial class DiagramPropertiesViewModel : DialogViewModel
{
    public DiagramPropertiesViewModel(DiagramDocument document)
    {
        Name = document.Name;
        Description = document.Description;
        Author = document.Author;
        TypeName = DiagramTypeInfo.For(document.DiagramType).DisplayName;
        TaggedValues = new TaggedValuesEditorViewModel(document.TaggedValues);
    }

    public override string Title => Strings.Dialog_DiagramProperties_Title;

    public string TypeName { get; }

    /// <summary>The diagram's tagged values, e.g. the servers of an imported OpenAPI description.</summary>
    public TaggedValuesEditorViewModel TaggedValues { get; }

    [ObservableProperty]
    public partial string Name { get; set; }

    [ObservableProperty]
    public partial string? Description { get; set; }

    [ObservableProperty]
    public partial string? Author { get; set; }

    public string TrimmedName => (Name ?? string.Empty).Trim();

    /// <summary>True when the dialog's values differ from those of <paramref name="document"/>.</summary>
    public bool Differs(DiagramDocument document) =>
        TrimmedName != document.Name ||
        AttributeRowViewModel.NullIfBlank(Description) != document.Description ||
        AttributeRowViewModel.NullIfBlank(Author) != document.Author ||
        TaggedValues.Differs(document.TaggedValues);

    public void ApplyTo(DiagramDocument document)
    {
        document.Name = TrimmedName;
        document.Description = AttributeRowViewModel.NullIfBlank(Description);
        document.Author = AttributeRowViewModel.NullIfBlank(Author);
        document.TaggedValues = TaggedValues.ToModel();
    }

    protected override string? Validate() => TrimmedName.Length == 0 ? Strings.Dialog_DiagramProperties_NeedsName : null;
}
