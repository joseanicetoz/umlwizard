namespace UmlWizard.App.Services;

/// <summary>Keeps the states in memory only (the default when no store is given, e.g. in tests).</summary>
public sealed class InMemoryProjectStateStore : IProjectStateStore
{
    private readonly Dictionary<string, ProjectUserState> _states = new(ProjectStateFileStore.PathComparer);

    public ProjectUserState? Load(string projectPath) => _states.GetValueOrDefault(Path.GetFullPath(projectPath));

    public void Save(string projectPath, ProjectUserState state)
    {
        state.ProjectPath = Path.GetFullPath(projectPath);
        _states[state.ProjectPath] = state;
    }

    public void Delete(string projectPath) => _states.Remove(Path.GetFullPath(projectPath));
}
