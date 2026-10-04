using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using UmlWizard.App.Resources;
using UmlWizard.App.Services;

namespace UmlWizard.App.ViewModels.Dialogs;

/// <summary>Where and how to export a diagram as a PNG image.</summary>
public sealed partial class ExportPngViewModel : DialogViewModel
{
    private readonly IDialogService _dialogs;
    private readonly string _diagramName;

    public ExportPngViewModel(IDialogService dialogs, string diagramName, string outputFolder, string fileName, bool transparentBackground)
    {
        _dialogs = dialogs;
        _diagramName = diagramName;
        OutputFolder = outputFolder;
        FileName = fileName;
        TransparentBackground = transparentBackground;
    }

    public override string Title => Strings.Dialog_Export_Title;

    public string Prompt => Loc.Format(Strings.Dialog_Export_Prompt, _diagramName);

    [ObservableProperty]
    public partial string OutputFolder { get; set; }

    [ObservableProperty]
    public partial string FileName { get; set; }

    /// <summary>Leave the background transparent instead of white.</summary>
    [ObservableProperty]
    public partial bool TransparentBackground { get; set; }

    /// <summary>True when the file to export to already exists; updated by <see cref="CheckFileExists"/>.</summary>
    [ObservableProperty]
    public partial bool FileExists { get; set; }

    /// <summary>The file name with the <c>.png</c> extension added when it has none.</summary>
    public string PngFileName
    {
        get
        {
            var name = (FileName ?? string.Empty).Trim();
            return name.Length == 0 || Path.HasExtension(name) ? name : name + ".png";
        }
    }

    /// <summary>The file to export to, or <c>null</c> while the folder or the file name is missing or invalid.</summary>
    public string? FullPath
    {
        get
        {
            var folder = (OutputFolder ?? string.Empty).Trim();
            var name = PngFileName;
            if (folder.Length == 0 || name.Length == 0 || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            {
                return null;
            }

            try
            {
                return Path.GetFullPath(Path.Combine(folder, name));
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
            {
                return null;
            }
        }
    }

    /// <summary>Shows or hides the "File already exists" warning; called when the user leaves the name or folder.</summary>
    public void CheckFileExists() => FileExists = FullPath is { } path && File.Exists(path);

    [RelayCommand]
    private async Task BrowseFolder()
    {
        if (await _dialogs.PickFolderAsync(Strings.Picker_OutputFolder) is { } folder)
        {
            OutputFolder = folder;
            CheckFileExists();
        }
    }

    protected override string? Validate()
    {
        CheckFileExists();
        if (string.IsNullOrWhiteSpace(OutputFolder))
        {
            return Strings.Dialog_Export_NeedsFolder;
        }

        if (string.IsNullOrWhiteSpace(FileName))
        {
            return Strings.Dialog_Export_NeedsFileName;
        }

        if (PngFileName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            return Strings.Dialog_Export_InvalidFileName;
        }

        return FullPath is { } path && Directory.Exists(Path.GetDirectoryName(path))
            ? null
            : Strings.Dialog_Export_FolderMissing;
    }
}
