using System.Text.Json;
using UmlWizard.App.ViewModels;
using UmlWizard.Core.Model;

namespace UmlWizard.App.Services;

/// <summary>
/// One user's state of a project: the diagrams open in tabs, in tab order, the active tab, and the folders expanded in
/// the Project Explorer. It is kept per user in the application data folder (see <see cref="IProjectStateStore"/>),
/// never in the project file, which stays the same for everyone who shares it.
/// </summary>
public sealed class ProjectUserState
{
    /// <summary>The project file the state belongs to, as a full path; set by the store.</summary>
    public string? ProjectPath { get; set; }

    /// <summary>Ids of the diagrams open in tabs, in tab order.</summary>
    public List<string> OpenDiagramIds { get; set; } = [];

    /// <summary>Id of the active tab; <c>null</c> when no diagram is open.</summary>
    public string? ActiveDiagramId { get; set; }

    /// <summary>
    /// Ids of the folders expanded in the Project Explorer, and <see cref="ProjectRootNodeViewModel.RootId"/> when the
    /// project itself is; <c>null</c> when never saved, which expands everything.
    /// </summary>
    public List<string>? ExpandedFolderIds { get; set; }

    /// <summary>
    /// Repairs the state for <paramref name="project"/>: drops unknown and repeated diagram and folder ids, and an active
    /// tab that is not open.
    /// </summary>
    public void Normalize(UmlProject project)
    {
        var known = project.Diagrams.Select(d => d.Id).ToHashSet();
        OpenDiagramIds = (OpenDiagramIds ?? []).Where(id => id is not null && known.Contains(id)).Distinct().ToList();
        if (ActiveDiagramId is not null && !OpenDiagramIds.Contains(ActiveDiagramId))
        {
            ActiveDiagramId = null;
        }

        if (ExpandedFolderIds is not null)
        {
            var folders = project.AllFolders().Select(f => f.Id).Append(ProjectRootNodeViewModel.RootId).ToHashSet();
            ExpandedFolderIds = ExpandedFolderIds.Where(id => id is not null && folders.Contains(id)).Distinct().ToList();
        }
    }

    /// <summary>
    /// The tabs that earlier versions kept in the project file itself, read until the user has a state of their own for
    /// the project; <c>null</c> when the file has none or cannot be read.
    /// </summary>
    public static ProjectUserState? ReadFromProjectFile(string projectPath)
    {
        try
        {
            var state = JsonSerializer.Deserialize<ProjectUserState>(File.ReadAllText(projectPath), JsonSerializerOptions.Web);
            return state is { OpenDiagramIds.Count: > 0 } ? state : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }
}
