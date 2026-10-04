namespace UmlWizard.Core.Model;

/// <summary>A folder of the project hierarchy: subfolders and the ids of the diagrams it contains, in display order.</summary>
public sealed class ProjectFolder
{
    public string Id { get; set; } = ModelElement.NewId();

    public string Name { get; set; } = "Folder";

    public List<ProjectFolder> Folders { get; set; } = [];

    public List<string> DiagramIds { get; set; } = [];

    public IEnumerable<ProjectFolder> SelfAndDescendants() => Folders.SelectMany(f => f.SelfAndDescendants()).Prepend(this);

    public ProjectFolder Clone() => new()
    {
        Id = Id,
        Name = Name,
        Folders = Folders.Select(f => f.Clone()).ToList(),
        DiagramIds = [.. DiagramIds],
    };
}
