using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using UmlWizard.App.Resources;
using UmlWizard.App.Services;
using UmlWizard.OpenApi;

namespace UmlWizard.App.ViewModels.Dialogs;

/// <summary>Where to export a class diagram as an OpenAPI description, and the API's title and version.</summary>
public sealed partial class ExportOpenApiViewModel : DialogViewModel
{
    private readonly IDialogService _dialogs;
    private readonly string _diagramName;

    public ExportOpenApiViewModel(IDialogService dialogs, string diagramName, string outputFolder, string fileName, string apiTitle, string apiVersion)
    {
        _dialogs = dialogs;
        _diagramName = diagramName;
        OutputFolder = outputFolder;
        FileName = fileName;
        ApiTitle = apiTitle;
        ApiVersion = apiVersion;
    }

    public override string Title => Strings.Dialog_ExportOpenApi_Title;

    public string Prompt => Loc.Format(Strings.Dialog_ExportOpenApi_Prompt, _diagramName);

    [ObservableProperty]
    public partial string OutputFolder { get; set; }

    [ObservableProperty]
    public partial string FileName { get; set; }

    [ObservableProperty]
    public partial string ApiTitle { get; set; }

    [ObservableProperty]
    public partial string ApiVersion { get; set; }

    /// <summary>True when the file to export to already exists; updated by <see cref="CheckFileExists"/>.</summary>
    [ObservableProperty]
    public partial bool FileExists { get; set; }

    /// <summary>The file name with <c>.yaml</c> added when it has no extension.</summary>
    public string FullFileName
    {
        get
        {
            var name = (FileName ?? string.Empty).Trim();
            return name.Length == 0 || Path.HasExtension(name) ? name : name + ".yaml";
        }
    }

    /// <summary>JSON for a <c>.json</c> file, YAML otherwise.</summary>
    public OpenApiFormat Format =>
        Path.GetExtension(FullFileName).Equals(".json", StringComparison.OrdinalIgnoreCase) ? OpenApiFormat.Json : OpenApiFormat.Yaml;

    /// <summary>The file to export to, or <c>null</c> while the folder or the file name is missing or invalid.</summary>
    public string? FullPath
    {
        get
        {
            var folder = (OutputFolder ?? string.Empty).Trim();
            var name = FullFileName;
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

    public OpenApiExportOptions Options => new(ApiTitle.Trim(), ApiVersion.Trim(), Format);

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

        if (FullFileName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            return Strings.Dialog_Export_InvalidFileName;
        }

        if (string.IsNullOrWhiteSpace(ApiTitle))
        {
            return Strings.Dialog_ExportOpenApi_NeedsTitle;
        }

        if (string.IsNullOrWhiteSpace(ApiVersion))
        {
            return Strings.Dialog_ExportOpenApi_NeedsVersion;
        }

        return FullPath is { } path && Directory.Exists(Path.GetDirectoryName(path))
            ? null
            : Strings.Dialog_Export_FolderMissing;
    }
}
