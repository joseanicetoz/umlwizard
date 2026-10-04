using CommunityToolkit.Mvvm.Input;
using UmlWizard.App.Resources;
using UmlWizard.Core.Model;

namespace UmlWizard.App.ViewModels.Dialogs;

public sealed class AttributeRowViewModel
{
    public AttributeRowViewModel(
        UmlAttribute attribute, Action<AttributeRowViewModel> remove, Action<AttributeRowViewModel>? editTaggedValues = null)
    {
        Visibility = attribute.Visibility;
        Name = attribute.Name;
        Type = attribute.Type;
        DefaultValue = attribute.DefaultValue;
        IsStatic = attribute.IsStatic;
        TaggedValues = new TaggedValuesEditorViewModel(attribute.TaggedValues);
        RemoveCommand = new RelayCommand(() => remove(this));
        EditTaggedValuesCommand = new RelayCommand(() => editTaggedValues?.Invoke(this));
    }

    public UmlVisibility Visibility { get; set; }

    public string Name { get; set; }

    public string? Type { get; set; }

    public string? DefaultValue { get; set; }

    public bool IsStatic { get; set; }

    public TaggedValuesEditorViewModel TaggedValues { get; }

    public IRelayCommand RemoveCommand { get; }

    /// <summary>Shows the attribute's tagged values below the member lists.</summary>
    public IRelayCommand EditTaggedValuesCommand { get; }

    public UmlAttribute ToModel() => new()
    {
        Visibility = Visibility,
        Name = Name.Trim(),
        Type = NullIfBlank(Type),
        DefaultValue = NullIfBlank(DefaultValue),
        IsStatic = IsStatic,
        TaggedValues = TaggedValues.ToModel(),
    };

    internal static string? NullIfBlank(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();
}
