namespace UmlWizard.Core.Model;

/// <summary>A project holds descriptive information and all of its diagrams in a single file.</summary>
public sealed class UmlProject
{
    public const string FileExtension = ".umlwiz";

    /// <summary>Extension used by earlier versions; such projects still open and can be saved in place.</summary>
    public const string LegacyFileExtension = ".umlproj";

    /// <summary>File-dialog patterns for all project files, current extension first.</summary>
    public static IReadOnlyList<string> FilePatterns { get; } = ["*" + FileExtension, "*" + LegacyFileExtension];

    public static bool IsProjectFile(string path) =>
        path.EndsWith(FileExtension, StringComparison.OrdinalIgnoreCase) ||
        path.EndsWith(LegacyFileExtension, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Schema 1 referenced separate diagram files; schema 2 embeds the diagrams; schema 3 adds
    /// <see cref="Folders"/>.
    /// </summary>
    public const int CurrentSchemaVersion = 3;

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;

    public string Name { get; set; } = "Untitled";

    /// <summary>
    /// The project's own version as the user writes it, e.g. <c>1.2.0</c>; <c>null</c> when not set. Not the version of
    /// the file format, which is <see cref="SchemaVersion"/>.
    /// </summary>
    public string? Version { get; set; }

    public string? Description { get; set; }

    public string? Author { get; set; }

    public DateTimeOffset Created { get; set; } = DateTimeOffset.Now;

    public DateTimeOffset Modified { get; set; } = DateTimeOffset.Now;

    /// <summary>All diagrams of the project. Their place in the hierarchy is described by <see cref="Folders"/>.</summary>
    public List<DiagramDocument> Diagrams { get; set; } = [];

    /// <summary>
    /// Top-level folders. Diagrams that no folder lists are shown at the top level, in the order of
    /// <see cref="Diagrams"/>.
    /// </summary>
    public List<ProjectFolder> Folders { get; set; } = [];

    public DiagramDocument? FindDiagram(string id) => Diagrams.Find(d => d.Id == id);

    public ProjectFolder? FindFolder(string id) => AllFolders().FirstOrDefault(f => f.Id == id);

    /// <summary>All folders, depth first.</summary>
    public IEnumerable<ProjectFolder> AllFolders() => Folders.SelectMany(f => f.SelfAndDescendants());

    /// <summary>The folder that contains the diagram, or <c>null</c> when it is at the top level.</summary>
    public ProjectFolder? FolderOfDiagram(string diagramId) => AllFolders().FirstOrDefault(f => f.DiagramIds.Contains(diagramId));

    /// <summary>The folder that contains the folder, or <c>null</c> when it is at the top level.</summary>
    public ProjectFolder? ParentOfFolder(string folderId) => AllFolders().FirstOrDefault(f => f.Folders.Any(c => c.Id == folderId));

    /// <summary>Diagrams at the top level, in project order.</summary>
    public IEnumerable<DiagramDocument> TopLevelDiagrams()
    {
        var inFolders = AllFolders().SelectMany(f => f.DiagramIds).ToHashSet();
        return Diagrams.Where(d => !inFolders.Contains(d.Id));
    }

    public ProjectFolder AddFolder(string name, string? parentId = null)
    {
        var folder = new ProjectFolder { Name = name };
        ChildFolders(parentId).Add(folder);
        return folder;
    }

    /// <summary>
    /// Moves a diagram into a folder, or to the top level when <paramref name="folderId"/> is <c>null</c>. With
    /// <paramref name="index"/> it is placed at that position among the other diagrams there (the end when past it);
    /// without, it goes last in a folder and keeps its project order at the top level.
    /// </summary>
    public void MoveDiagram(string diagramId, string? folderId, int? index = null)
    {
        var diagram = FindDiagram(diagramId)
            ?? throw new ArgumentException($"The diagram '{diagramId}' is not part of the project.", nameof(diagramId));
        var target = folderId is null ? null : FindFolder(folderId)
            ?? throw new ArgumentException($"The folder '{folderId}' does not exist.", nameof(folderId));
        FolderOfDiagram(diagramId)?.DiagramIds.Remove(diagramId);
        if (target is not null)
        {
            target.DiagramIds.Insert(Math.Clamp(index ?? int.MaxValue, 0, target.DiagramIds.Count), diagramId);
        }
        else if (index is { } position)
        {
            // Top-level diagrams are shown in project order: put it before the one now at that position.
            Diagrams.Remove(diagram);
            var others = TopLevelDiagrams().ToList();
            Diagrams.Insert(position < others.Count ? Diagrams.IndexOf(others[Math.Max(position, 0)]) : Diagrams.Count, diagram);
        }
    }

    /// <summary>True when <paramref name="folderId"/> may be moved into <paramref name="targetId"/> (not into itself or a subfolder).</summary>
    public bool CanMoveFolder(string folderId, string? targetId) =>
        FindFolder(folderId) is { } folder &&
        (targetId is null || (FindFolder(targetId) is not null && folder.SelfAndDescendants().All(f => f.Id != targetId)));

    /// <summary>Moves a folder with its contents into another folder, or to the top level when <paramref name="targetId"/> is <c>null</c>.</summary>
    public void MoveFolder(string folderId, string? targetId)
    {
        if (!CanMoveFolder(folderId, targetId))
        {
            throw new InvalidOperationException("A folder cannot be moved into itself or one of its subfolders.");
        }

        var folder = FindFolder(folderId)!;
        ChildFolders(ParentOfFolder(folderId)?.Id).Remove(folder);
        ChildFolders(targetId).Add(folder);
    }

    /// <summary>Removes a folder; its diagrams and subfolders move to the folder's parent.</summary>
    public void RemoveFolder(string folderId)
    {
        var folder = FindFolder(folderId)
            ?? throw new ArgumentException($"The folder '{folderId}' does not exist.", nameof(folderId));
        var parent = ParentOfFolder(folderId);
        var siblings = ChildFolders(parent?.Id);
        var index = siblings.IndexOf(folder);
        siblings.RemoveAt(index);
        siblings.InsertRange(index, folder.Folders);
        parent?.DiagramIds.AddRange(folder.DiagramIds);
    }

    /// <summary>Removes a diagram from the project and from its folder.</summary>
    public void RemoveDiagram(string diagramId)
    {
        FolderOfDiagram(diagramId)?.DiagramIds.Remove(diagramId);
        Diagrams.RemoveAll(d => d.Id == diagramId);
    }

    /// <summary>
    /// Repairs the hierarchy of a loaded project: drops references to unknown diagrams, lists each diagram
    /// and folder only once, and gives folders without an id a new one.
    /// </summary>
    public void NormalizeFolders()
    {
        var known = Diagrams.Select(d => d.Id).ToHashSet();
        var placed = new HashSet<string>();
        var folderIds = new HashSet<string>();
        Folders = Normalize(Folders);

        List<ProjectFolder> Normalize(List<ProjectFolder>? folders)
        {
            var result = new List<ProjectFolder>();
            foreach (var folder in folders ?? [])
            {
                if (folder is null)
                {
                    continue;
                }

                if (string.IsNullOrWhiteSpace(folder.Id) || !folderIds.Add(folder.Id))
                {
                    folder.Id = ModelElement.NewId();
                    folderIds.Add(folder.Id);
                }

                folder.Name ??= string.Empty;
                folder.DiagramIds = (folder.DiagramIds ?? []).Where(id => id is not null && known.Contains(id) && placed.Add(id)).ToList();
                folder.Folders = Normalize(folder.Folders);
                result.Add(folder);
            }

            return result;
        }
    }

    private List<ProjectFolder> ChildFolders(string? folderId) => folderId is null
        ? Folders
        : (FindFolder(folderId) ?? throw new ArgumentException($"The folder '{folderId}' does not exist.", nameof(folderId))).Folders;
}
