using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace UmlWizard.App.Services;

/// <summary>
/// Stores each project's state as a JSON file in the user's application data folder, named after a hash of the project
/// file's path. The file also holds the path, to tell which project it belongs to.
/// </summary>
public sealed class ProjectStateFileStore(string folder) : IProjectStateStore
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public static string DefaultFolder { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "UmlWizard", "projects");

    /// <summary>Compares project paths as the file system does: ignoring case on Windows and macOS.</summary>
    public static StringComparer PathComparer { get; } =
        OperatingSystem.IsWindows() || OperatingSystem.IsMacOS() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    public string Folder { get; } = folder;

    /// <summary>The file that holds the state of the project file at <paramref name="projectPath"/>.</summary>
    public string FileFor(string projectPath)
    {
        var key = Path.GetFullPath(projectPath);
        if (PathComparer == StringComparer.OrdinalIgnoreCase)
        {
            key = key.ToUpperInvariant();
        }

        return Path.Combine(Folder, Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key)))[..16].ToLowerInvariant() + ".json");
    }

    /// <summary>Returns <c>null</c> when the file is missing, unreadable or of another project; the state is a convenience only.</summary>
    public ProjectUserState? Load(string projectPath)
    {
        try
        {
            var file = FileFor(projectPath);
            var state = File.Exists(file) ? JsonSerializer.Deserialize<ProjectUserState>(File.ReadAllText(file), Options) : null;
            return state?.ProjectPath is { } path && PathComparer.Equals(path, Path.GetFullPath(projectPath)) ? state : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    /// <summary>Failures are ignored: a state left behind is never read for another project.</summary>
    public void Delete(string projectPath)
    {
        try
        {
            File.Delete(FileFor(projectPath));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    /// <summary>Failures are ignored: losing the state must never stop the user from working.</summary>
    public void Save(string projectPath, ProjectUserState state)
    {
        try
        {
            state.ProjectPath = Path.GetFullPath(projectPath);
            Directory.CreateDirectory(Folder);
            File.WriteAllText(FileFor(projectPath), JsonSerializer.Serialize(state, Options));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }
}
