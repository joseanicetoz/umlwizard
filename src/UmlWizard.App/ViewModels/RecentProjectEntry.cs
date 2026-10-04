using System.Windows.Input;

namespace UmlWizard.App.ViewModels;

/// <summary>An entry of the File → Recent Projects menu. A <see cref="Header"/> of "-" is shown as a separator.</summary>
public sealed record RecentProjectEntry(string Header, ICommand? Command, object? Parameter = null, string? ToolTip = null)
{
    public static RecentProjectEntry Separator { get; } = new("-", null);

    /// <summary>Menu text for a project: an access key (1–9, then 0 for the tenth), the name and its folder.</summary>
    public static string FormatHeader(int index, string path)
    {
        var key = index < 9 ? $"_{index + 1}" : "1_0";
        var name = Path.GetFileNameWithoutExtension(path);

        // Menus have a limited width: the longer the name, the shorter the folder.
        var folder = Shorten(Path.GetDirectoryName(path) ?? string.Empty, Math.Max(16, 50 - name.Length));
        return $"{key}  {name.Replace("_", "__")}    {folder.Replace("_", "__")}";
    }

    /// <summary>Shortens a long path in the middle so both the drive and the last folder stay visible.</summary>
    public static string Shorten(string path, int maxLength)
    {
        if (path.Length <= maxLength)
        {
            return path;
        }

        var tail = maxLength * 2 / 3;
        return path[..(maxLength - tail - 1)] + "…" + path[^tail..];
    }
}
