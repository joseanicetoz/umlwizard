using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using UmlWizard.App.Resources;
using UmlWizard.App.Services;

namespace UmlWizard.App.ViewModels.Dialogs;

/// <summary>
/// Project name, version, description and author; also asks for a location when creating a project, and shows the
/// folder of an existing one, which a click copies to the clipboard.
/// </summary>
public sealed partial class ProjectPropertiesViewModel : DialogViewModel
{
    /// <summary>How long the dialog shows that the folder was copied.</summary>
    internal static readonly TimeSpan CopiedDuration = TimeSpan.FromSeconds(2);

    /// <summary>Waits while the cue is shown; tests replace it to decide when the cue fades.</summary>
    internal Func<TimeSpan, Task> Delay { get; set; } = duration => Task.Delay(duration);

    private readonly IDialogService _dialogs;
    private readonly IClipboardService? _clipboard;
    private int _copies;

    public ProjectPropertiesViewModel(IDialogService dialogs, bool isNew, IClipboardService? clipboard = null)
    {
        _dialogs = dialogs;
        _clipboard = clipboard;
        IsNew = isNew;
        Name = isNew ? Strings.Dialog_NewProject_DefaultName : string.Empty;
        Location = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
    }

    public override string Title => IsNew ? Strings.Dialog_NewProject_Title : Strings.Dialog_ProjectProperties_Title;

    public bool IsNew { get; }

    [ObservableProperty]
    public partial string Name { get; set; }

    /// <summary>The project's own version, e.g. 1.2.0; empty for none.</summary>
    [ObservableProperty]
    public partial string? Version { get; set; }

    [ObservableProperty]
    public partial string? Description { get; set; }

    [ObservableProperty]
    public partial string? Author { get; set; }

    /// <summary>Parent folder in which a new project folder is created.</summary>
    [ObservableProperty]
    public partial string Location { get; set; }

    /// <summary>The folder the project file of an existing project is in; read-only.</summary>
    public string? Folder { get; init; }

    public bool ShowsFolder => !IsNew && !string.IsNullOrEmpty(Folder);

    /// <summary>True for a moment after the folder was copied to the clipboard.</summary>
    [ObservableProperty]
    public partial bool Copied { get; set; }

    [RelayCommand]
    private async Task CopyFolder()
    {
        if (_clipboard is null || string.IsNullOrEmpty(Folder))
        {
            return;
        }

        await _clipboard.SetTextAsync(Folder);
        Copied = true;

        // Another click restarts the time the cue is shown.
        var copy = ++_copies;
        await Delay(CopiedDuration);
        if (copy == _copies)
        {
            Copied = false;
        }
    }

    [RelayCommand]
    private async Task BrowseLocation()
    {
        if (await _dialogs.PickFolderAsync(Strings.Picker_ProjectLocation) is { } folder)
        {
            Location = folder;
        }
    }

    protected override string? Validate()
    {
        if (string.IsNullOrWhiteSpace(Name))
        {
            return Strings.Dialog_Project_NeedsName;
        }

        if (IsNew && (string.IsNullOrWhiteSpace(Location) || !Directory.Exists(Location)))
        {
            return Strings.Dialog_Project_NeedsLocation;
        }

        return null;
    }
}
