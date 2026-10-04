using CommunityToolkit.Mvvm.Input;
using UmlWizard.App.Resources;
using UmlWizard.Core.Model;
using UmlWizard.Core.Notation;

namespace UmlWizard.App.ViewModels.Dialogs;

public sealed class OperationRowViewModel
{
    public OperationRowViewModel(
        UmlOperation operation, Action<OperationRowViewModel> remove, Action<OperationRowViewModel>? editTaggedValues = null)
    {
        Visibility = operation.Visibility;
        Name = operation.Name;
        Parameters = UmlNotation.FormatParameters(operation.Parameters);
        ReturnType = operation.ReturnType;
        IsStatic = operation.IsStatic;
        IsAbstract = operation.IsAbstract;
        HttpMethod = operation.HttpMethod?.Trim().ToUpperInvariant() ?? string.Empty;
        Path = operation.Path;
        TaggedValues = new TaggedValuesEditorViewModel(operation.TaggedValues);
        RemoveCommand = new RelayCommand(() => remove(this));
        EditTaggedValuesCommand = new RelayCommand(() => editTaggedValues?.Invoke(this));
    }

    public UmlVisibility Visibility { get; set; }

    public string Name { get; set; }

    /// <summary>Parameters in UML notation, e.g. <c>id: int, name: string</c>.</summary>
    public string? Parameters { get; set; }

    public string? ReturnType { get; set; }

    public bool IsStatic { get; set; }

    public bool IsAbstract { get; set; }

    /// <summary>The HTTP method of a web API endpoint, e.g. GET; empty when the operation is not one.</summary>
    public string HttpMethod { get; set; }

    /// <summary>The URL path of a web API endpoint, e.g. <c>/orders/{id}</c>.</summary>
    public string? Path { get; set; }

    public TaggedValuesEditorViewModel TaggedValues { get; }

    public IRelayCommand RemoveCommand { get; }

    /// <summary>Shows the operation's tagged values below the member lists.</summary>
    public IRelayCommand EditTaggedValuesCommand { get; }

    public UmlOperation ToModel() => new()
    {
        Visibility = Visibility,
        Name = Name.Trim(),
        Parameters = UmlNotation.ParseParameters(Parameters ?? string.Empty),
        ReturnType = AttributeRowViewModel.NullIfBlank(ReturnType),
        IsStatic = IsStatic,
        IsAbstract = IsAbstract,
        HttpMethod = AttributeRowViewModel.NullIfBlank(HttpMethod),
        Path = AttributeRowViewModel.NullIfBlank(Path),
        TaggedValues = TaggedValues.ToModel(),
    };
}
