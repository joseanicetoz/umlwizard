using System.Text.Json;
using System.Text.Json.Serialization;

namespace UmlWizard.App.Services;

/// <summary>Stores the settings as a JSON object in the user's application data folder, next to the recent projects.</summary>
public sealed class SettingsFileStore(string filePath) : ISettingsStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public static string DefaultFilePath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "UmlWizard", "settings.json");

    public string FilePath { get; } = filePath;

    /// <summary>Returns the defaults when the file is missing or unreadable.</summary>
    public AppSettings Load()
    {
        try
        {
            return File.Exists(FilePath)
                ? JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath), Options) ?? new AppSettings()
                : new AppSettings();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return new AppSettings();
        }
    }

    /// <summary>Failures are ignored: losing a preference must never stop the user from working.</summary>
    public void Save(AppSettings settings)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(settings, Options));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }
}
