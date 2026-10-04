using System.Collections.ObjectModel;
using Material.Icons;
using UmlWizard.Core.Model;

namespace UmlWizard.App.ViewModels;

/// <summary>
/// The root of the project explorer, named after the project. Its children are the top-level folders and diagrams,
/// whose <see cref="ProjectNodeViewModel.Parent"/> stays <c>null</c>: the root is not a folder of the project.
/// </summary>
public sealed class ProjectRootNodeViewModel(UmlProject project) : ProjectNodeViewModel
{
    /// <summary>Id of the root node; it cannot clash with the ids of folders and diagrams.</summary>
    public const string RootId = "$project";

    public UmlProject Project { get; } = project;

    public override string Id => RootId;

    public override string Name => Project.Name;

    public override MaterialIconKind Icon => MaterialIconKind.BriefcaseOutline;

    public ObservableCollection<ProjectNodeViewModel> Children { get; } = [];

    /// <summary>Shows the current name of the project.</summary>
    public void Refresh() => OnPropertyChanged(nameof(Name));
}
