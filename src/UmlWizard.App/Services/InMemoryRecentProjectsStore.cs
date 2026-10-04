namespace UmlWizard.App.Services;

/// <summary>Keeps the list in memory only (the default when no store is given, e.g. in tests).</summary>
public sealed class InMemoryRecentProjectsStore(IEnumerable<string>? paths = null) : IRecentProjectsStore
{
    private List<string> _paths = paths?.ToList() ?? [];

    public IReadOnlyList<string> Load() => _paths;

    public void Save(IReadOnlyList<string> paths) => _paths = [.. paths];
}
