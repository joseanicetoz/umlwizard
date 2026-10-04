using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using UmlWizard.Core.Model;

namespace UmlWizard.App.ViewModels.Dialogs;

/// <summary>Edits a copy of an element's tagged values as name/value rows.</summary>
public sealed partial class TaggedValuesEditorViewModel : ObservableObject
{
    public TaggedValuesEditorViewModel(IEnumerable<TaggedValue> values)
    {
        foreach (var value in values)
        {
            Rows.Add(new TaggedValueRowViewModel(value.Name, value.Value, Remove));
        }
    }

    public ObservableCollection<TaggedValueRowViewModel> Rows { get; } = [];

    [RelayCommand]
    private void Add() => Rows.Add(new TaggedValueRowViewModel(string.Empty, string.Empty, Remove));

    /// <summary>The rows with a name, trimmed; rows without a name are dropped.</summary>
    public List<TaggedValue> ToModel() => Rows
        .Where(r => !string.IsNullOrWhiteSpace(r.Name))
        .Select(r => new TaggedValue { Name = r.Name.Trim(), Value = r.Value ?? string.Empty })
        .ToList();

    /// <summary>True when the rows say something other than <paramref name="values"/>.</summary>
    public bool Differs(IReadOnlyList<TaggedValue> values)
    {
        var edited = ToModel();
        return edited.Count != values.Count || edited.Zip(values).Any(p => p.First.Name != p.Second.Name || p.First.Value != p.Second.Value);
    }

    private void Remove(TaggedValueRowViewModel row) => Rows.Remove(row);
}

public sealed class TaggedValueRowViewModel
{
    public TaggedValueRowViewModel(string name, string value, Action<TaggedValueRowViewModel> remove)
    {
        Name = name;
        Value = value;
        RemoveCommand = new RelayCommand(() => remove(this));
    }

    public string Name { get; set; }

    public string Value { get; set; }

    public IRelayCommand RemoveCommand { get; }
}
