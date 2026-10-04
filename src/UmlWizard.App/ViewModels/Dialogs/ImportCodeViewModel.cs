using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using UmlWizard.App.Resources;
using UmlWizard.App.Services;
using UmlWizard.CodeImport;

namespace UmlWizard.App.ViewModels.Dialogs;

/// <summary>A language a source folder can be imported as, and how many of its files the folder has.</summary>
public sealed record ImportLanguage(ICodeImporter Importer, int FileCount)
{
    /// <summary>E.g. "Java (12 files)".</summary>
    public string Label => FileCount == 1
        ? Loc.Format(Strings.Dialog_ImportCode_LanguageLine_One, Importer.DisplayName)
        : Loc.Format(Strings.Dialog_ImportCode_LanguageLine, Importer.DisplayName, FileCount);

    public override string ToString() => Label;
}

/// <summary>
/// Imports a folder of source files as a new project, in a language the user picks among those it has files of. Says
/// up front what the import leaves out, and asks for the new project's name, author, description and location.
/// </summary>
public sealed partial class ImportCodeViewModel : DialogViewModel
{
    private readonly IDialogService _dialogs;

    private ImportCodeViewModel(IDialogService dialogs, string folder, IReadOnlyList<ImportLanguage> languages)
    {
        _dialogs = dialogs;
        Source = folder;
        Languages = languages;
        SelectedLanguage = languages[0];
        Name = Path.GetFileName(folder.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        Description = Loc.Format(Strings.Dialog_ImportCode_DefaultDescription, Name);
        Location = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
    }

    /// <summary>The import of a folder, in one of the <paramref name="languages"/> it has files of (the first is chosen).</summary>
    public static ImportCodeViewModel ForFolder(IDialogService dialogs, string folder, IReadOnlyList<ImportLanguage> languages) =>
        new(dialogs, folder, languages);

    public override string Title => Strings.Dialog_ImportFolder_Title;

    /// <summary>The folder the import reads.</summary>
    public string Source { get; }

    public IReadOnlyList<ImportLanguage> Languages { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FilesLine), nameof(Notice))]
    public partial ImportLanguage SelectedLanguage { get; set; }

    /// <summary>What will be read, e.g. "12 Java files".</summary>
    public string FilesLine => SelectedLanguage.FileCount == 1
        ? Loc.Format(Strings.Dialog_ImportCode_FilesLine_One, SelectedLanguage.Importer.DisplayName)
        : Loc.Format(Strings.Dialog_ImportCode_FilesLine, SelectedLanguage.FileCount, SelectedLanguage.Importer.DisplayName);

    /// <summary>What the import leaves out, said before it runs.</summary>
    public string Notice => Loc.Format(
        Strings.Dialog_ImportCode_Notice,
        SelectedLanguage.Importer.DisplayName,
        string.Join(", ", SelectedLanguage.Importer.FileExtensions));

    [ObservableProperty]
    public partial string Name { get; set; }

    [ObservableProperty]
    public partial string? Author { get; set; }

    [ObservableProperty]
    public partial string? Description { get; set; }

    /// <summary>Parent folder in which the new project's folder is created.</summary>
    [ObservableProperty]
    public partial string Location { get; set; }

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

        return string.IsNullOrWhiteSpace(Location) || !Directory.Exists(Location) ? Strings.Dialog_Project_NeedsLocation : null;
    }
}
