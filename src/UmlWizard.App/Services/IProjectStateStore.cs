namespace UmlWizard.App.Services;

/// <summary>Persists each user's <see cref="ProjectUserState"/>, by project file.</summary>
public interface IProjectStateStore
{
    /// <summary>The user's state of the project file at <paramref name="projectPath"/>; <c>null</c> when there is none.</summary>
    ProjectUserState? Load(string projectPath);

    void Save(string projectPath, ProjectUserState state);

    /// <summary>Forgets the state of the project file at <paramref name="projectPath"/>, e.g. when the file is deleted.</summary>
    void Delete(string projectPath);
}
