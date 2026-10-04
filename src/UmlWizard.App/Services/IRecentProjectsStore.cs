namespace UmlWizard.App.Services;

/// <summary>Persists the paths of recently used projects, most recent first.</summary>
public interface IRecentProjectsStore
{
    IReadOnlyList<string> Load();

    void Save(IReadOnlyList<string> paths);
}
