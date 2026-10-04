using System.Windows.Input;

namespace UmlWizard.App.ViewModels;

/// <summary>A project of the Recent Projects section of the left panel.</summary>
/// <param name="Copy">Copies the project next to it, given its path.</param>
/// <param name="Delete">Deletes the project file, given its path.</param>
/// <remarks>
/// The commands are on the item because its right-click menu, which opens in a popup of its own, cannot reach the
/// window's commands.
/// </remarks>
public sealed record RecentProjectItem(string Path, ICommand? Copy = null, ICommand? Delete = null)
{
    public string Name => System.IO.Path.GetFileNameWithoutExtension(Path);

    /// <summary>The folder of the project file, shortened in the middle when it is long.</summary>
    public string Folder => RecentProjectEntry.Shorten(System.IO.Path.GetDirectoryName(Path) ?? string.Empty, 40);
}
