using System.Collections.ObjectModel;
using Material.Icons;
using UmlWizard.Core.Model;

namespace UmlWizard.App.ViewModels;

/// <summary>A folder of the project explorer.</summary>
public sealed class FolderNodeViewModel(ProjectFolder folder) : ProjectNodeViewModel
{
    public ProjectFolder Folder { get; } = folder;

    public override string Id => Folder.Id;

    public override string Name => Folder.Name;

    public override MaterialIconKind Icon => IsExpanded ? MaterialIconKind.FolderOpenOutline : MaterialIconKind.FolderOutline;

    public ObservableCollection<ProjectNodeViewModel> Children { get; } = [];
}
