using CommunityToolkit.Mvvm.ComponentModel;
using Material.Icons;

namespace UmlWizard.App.ViewModels;

/// <summary>A node of the project explorer tree: a folder or a diagram.</summary>
public abstract partial class ProjectNodeViewModel : ObservableObject
{
    public abstract string Id { get; }

    public abstract string Name { get; }

    public abstract MaterialIconKind Icon { get; }

    /// <summary>The folder node that contains this node; <c>null</c> at the top level.</summary>
    public FolderNodeViewModel? Parent { get; init; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Icon))]
    public partial bool IsExpanded { get; set; }
}
