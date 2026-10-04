using System.Text.Json;

namespace UmlWizard.App.Services;

/// <summary>Stores the list as a JSON array in the user's application data folder.</summary>
public sealed class RecentProjectsFileStore(string filePath) : IRecentProjectsStore
{
    public static string DefaultFilePath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "UmlWizard", "recent-projects.json");

    public string FilePath { get; } = filePath;

    /// <summary>Returns an empty list when the file is missing or unreadable; the list is a convenience only.</summary>
    public IReadOnlyList<string> Load()
    {
        try
        {
            return File.Exists(FilePath)
                ? JsonSerializer.Deserialize<List<string>>(File.ReadAllText(FilePath))?.Where(p => !string.IsNullOrWhiteSpace(p)).ToList() ?? []
                : [];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return [];
        }
    }

    /// <summary>Failures are ignored: losing the list must never stop the user from working.</summary>
    public void Save(IReadOnlyList<string> paths)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(paths, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }
}
