using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using UmlWizard.Ai;
using UmlWizard.App.Resources;
using UmlWizard.App.Services;
using UmlWizard.App.ViewModels.Dialogs;
using UmlWizard.CodeGeneration.Plugins;
using UmlWizard.CodeImport;
using UmlWizard.CodeImport.Plugins;
using UmlWizard.Core.Model;
using UmlWizard.Core.Rendering;
using UmlWizard.Core.Serialization;
using UmlWizard.OpenApi;
using UmlWizard.PlantUml;

namespace UmlWizard.App.ViewModels;

public sealed partial class MainWindowViewModel : ObservableObject
{
    private static readonly string[] DiagramPatterns = ["*" + DiagramSerializer.FileExtension, "*.json"];

    private static readonly string[] OpenApiPatterns = ["*.yaml", "*.yml", "*.json"];

    private readonly IDialogService _dialogs;
    private readonly IClipboardService _clipboard;
    private readonly IRecentProjectsStore _recentStore;
    private readonly CodeGeneratorCatalog _codeGenerators;
    private readonly CodeImporterCatalog _codeImporters;
    private readonly ISettingsStore _settings;
    private readonly ISecretStore _secrets;
    private readonly IProjectStateStore _projectStates;
    private readonly Func<AiServiceSettings, string?, IAiProvider?> _aiProviders;
    private CancellationTokenSource? _busy;

    /// <summary>The last choice of the export dialog, offered again on the next export.</summary>
    private bool _exportTransparent;

    /// <summary>The folder of the last export, offered again; <c>null</c> offers the project folder.</summary>
    private string? _exportFolder;

    /// <summary>The choices of the last code generation, offered again; <c>null</c> offers the suggestions.</summary>
    private string _codeLanguage = "CSharp";
    private string? _codeFolder;
    private string? _codeNamespace;
    private List<string> _recentProjects;

    /// <param name="recentProjects">Where the recent projects list is kept; in memory only when omitted.</param>
    /// <param name="codeGenerators">The languages of Generate Code; the installed plug-ins when omitted.</param>
    /// <param name="settings">Where the application settings (e.g. the theme) are kept; in memory only when omitted.</param>
    /// <param name="codeImporters">The languages Import Source Folder reads; the installed plug-ins when omitted.</param>
    /// <param name="secrets">Where the API key of the AI service is kept; in memory only when omitted.</param>
    /// <param name="aiProviders">Makes the provider of the AI service; the real services when omitted.</param>
    /// <param name="projectStates">Where each project's open tabs are kept for the user; in memory only when omitted.</param>
    public MainWindowViewModel(
        IDialogService dialogs, IClipboardService clipboard, IRecentProjectsStore? recentProjects = null,
        CodeGeneratorCatalog? codeGenerators = null, ISettingsStore? settings = null, CodeImporterCatalog? codeImporters = null,
        ISecretStore? secrets = null, Func<AiServiceSettings, string?, IAiProvider?>? aiProviders = null,
        IProjectStateStore? projectStates = null)
    {
        _projectStates = projectStates ?? new InMemoryProjectStateStore();
        _secrets = secrets ?? new InMemorySecretStore();
        _aiProviders = aiProviders ?? ((s, key) => AiProviders.Create(s, key));
        _settings = settings ?? new InMemorySettingsStore();
        ColorPickerViewModel.CustomColorStore = _settings;
        var saved = _settings.Load();
        Theme = saved.Theme;
        IsProjectExplorerVisible = saved.ShowProjectExplorer;
        UiFontFamily = saved.UiFontFamily;
        UiFontSize = saved.UiFontSize;
        _dialogs = dialogs;
        _clipboard = clipboard;
        _recentStore = recentProjects ?? new InMemoryRecentProjectsStore();
        _codeGenerators = codeGenerators ?? InstalledPlugins.CodeGenerators;
        _codeImporters = codeImporters ?? InstalledPlugins.CodeImporters;
        _recentProjects = Normalize(_recentStore.Load());
        BuildRecentProjectsMenu();
        OpenDiagrams.CollectionChanged += (_, e) =>
        {
            foreach (DiagramViewModel diagram in e.OldItems ?? Array.Empty<DiagramViewModel>())
            {
                diagram.PropertyChanged -= OnOpenDiagramPropertyChanged;
            }

            foreach (DiagramViewModel diagram in e.NewItems ?? Array.Empty<DiagramViewModel>())
            {
                diagram.PropertyChanged += OnOpenDiagramPropertyChanged;
            }

            OnPropertyChanged(nameof(HasOpenDiagrams));
            OnPropertyChanged(nameof(PlaceholderText));
            CloseAllTabsCommand.NotifyCanExecuteChanged();
            OnSaveStateChanged();
        };
    }

    public event EventHandler? ExitRequested;

    /// <summary>
    /// Asks for the application to close and start again (for a new display language): the main window closes as
    /// File → Exit does, asking to save changes, and a new instance opens the same project.
    /// </summary>
    public event EventHandler? RestartRequested;

    /// <summary>The colour theme of View → Theme; saved when it changes. The application applies it (see App.axaml.cs).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSystemTheme), nameof(IsLightTheme), nameof(IsDarkTheme))]
    public partial AppTheme Theme { get; set; }

    // The View → Theme radio items: checking one chooses its theme; the others are unchecked by the menu.
    public bool IsSystemTheme
    {
        get => Theme == AppTheme.System;
        set => SetThemeWhen(value, AppTheme.System);
    }

    public bool IsLightTheme
    {
        get => Theme == AppTheme.Light;
        set => SetThemeWhen(value, AppTheme.Light);
    }

    public bool IsDarkTheme
    {
        get => Theme == AppTheme.Dark;
        set => SetThemeWhen(value, AppTheme.Dark);
    }

    private void SetThemeWhen(bool isChecked, AppTheme theme)
    {
        if (isChecked)
        {
            Theme = theme;
        }
    }

    partial void OnThemeChanged(AppTheme value) => SaveSettings(s => s with { Theme = value });

    /// <summary>Whether the Project Explorer panel on the left is shown (View → Project Explorer); saved when it changes.</summary>
    [ObservableProperty]
    public partial bool IsProjectExplorerVisible { get; set; }

    partial void OnIsProjectExplorerVisibleChanged(bool value) => SaveSettings(s => s with { ShowProjectExplorer = value });

    /// <summary>Shows or hides the Project Explorer (the <see cref="Shortcuts.ToggleProjectExplorer"/> shortcut).</summary>
    [RelayCommand]
    private void ToggleProjectExplorer() => IsProjectExplorerVisible = !IsProjectExplorerVisible;

    /// <summary>The font of the user interface (Preferences → Appearance); <c>null</c> is the default. The application applies it.</summary>
    [ObservableProperty]
    public partial string? UiFontFamily { get; set; }

    /// <summary>The font size of the user interface (Preferences → Appearance); <c>null</c> is the default. The application applies it.</summary>
    [ObservableProperty]
    public partial double? UiFontSize { get; set; }

    /// <summary>
    /// Opens the Preferences window; every change in it applies at once and is saved. When the user chose to restart
    /// there for a new display language, the application restarts once the window has closed.
    /// </summary>
    [RelayCommand]
    private async Task Preferences()
    {
        var preferences = new PreferencesViewModel(
            _settings.Load(), _dialogs, ApplySettings,
            Avalonia.Application.Current?.ActualThemeVariant == Avalonia.Styling.ThemeVariant.Dark ? RenderTheme.Dark : RenderTheme.Light,
            _secrets, _aiProviders);
        await _dialogs.ShowPreferencesAsync(preferences);
        if (preferences.IsRestartRequested)
        {
            RestartRequested?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>Applies preferences as they are changed in the Preferences window, and saves them.</summary>
    internal void ApplySettings(AppSettings settings)
    {
        Theme = settings.Theme;
        UiFontFamily = settings.UiFontFamily;
        UiFontSize = settings.UiFontSize;
        foreach (var diagram in OpenDiagrams)
        {
            diagram.Defaults = settings.StyleFor(diagram.Document.DiagramType);
            diagram.Chat?.Refresh();
        }

        // The Project Explorer's visibility and the custom colours of the colour picker are not on the Preferences window
        // (although the picker can be opened from it): keep their current values.
        SaveSettings(current => settings with
        {
            ShowProjectExplorer = current.ShowProjectExplorer,
            CustomColors = current.CustomColors,
            AiAllowedProjects = current.AiAllowedProjects,
        });
    }

    /// <summary>Saves the settings with a change applied, unless it changes nothing (e.g. while loading them).</summary>
    private void SaveSettings(Func<AppSettings, AppSettings> change)
    {
        var settings = _settings.Load();
        var changed = change(settings);
        if (changed != settings)
        {
            _settings.Save(changed);
        }
    }

    /// <summary>The nodes the explorer shows: the project root node while a project is open.</summary>
    public ObservableCollection<ProjectNodeViewModel> ExplorerNodes { get; } = [];

    /// <summary>The root of the explorer tree, named after the project; <c>null</c> while no project is open.</summary>
    public ProjectRootNodeViewModel? ProjectRoot => ExplorerNodes.OfType<ProjectRootNodeViewModel>().FirstOrDefault();

    /// <summary>Top-level folders and diagrams of the project: the children of <see cref="ProjectRoot"/>.</summary>
    public IReadOnlyList<ProjectNodeViewModel> ProjectTree => ProjectRoot?.Children ?? [];

    public ObservableCollection<DiagramViewModel> OpenDiagrams { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasProject), nameof(WindowTitle))]
    public partial UmlProject? Project { get; set; }

    public string? ProjectPath { get; private set; }

    /// <summary>The user's tabs of the project as last read from or written to the project state store.</summary>
    private ProjectUserState _userState = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SelectedDiagramItem), nameof(IsProjectRootSelected))]
    public partial ProjectNodeViewModel? SelectedNode { get; set; }

    public DiagramItemViewModel? SelectedDiagramItem => SelectedNode as DiagramItemViewModel;

    /// <summary>The project root is selected in the explorer: it can be renamed, but not moved or deleted.</summary>
    public bool IsProjectRootSelected => SelectedNode is ProjectRootNodeViewModel;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasActiveDiagram), nameof(PlaceholderText))]
    public partial DiagramViewModel? ActiveDiagram { get; set; }

    /// <summary>True when a diagram tab is active; tabs can be open with none active (see <see cref="OnSelectedNodeChanged"/>).</summary>
    public bool HasActiveDiagram => ActiveDiagram is not null;

    /// <summary>Shown in the diagram area while no tab is active.</summary>
    public string PlaceholderText => HasOpenDiagrams
        ? Strings.Placeholder_DiagramNotOpen
        : Strings.Placeholder_NoDiagram;

    public bool HasProject => Project is not null;

    public bool HasOpenDiagrams => OpenDiagrams.Count > 0;

    public bool IsProjectEmpty => Project is not null && Project.Diagrams.Count == 0 && Project.Folders.Count == 0;

    public string WindowTitle => Project is null ? Strings.App_Name : Loc.Format(Strings.App_WindowTitleWithProject, Project.Name);

    #region Project

    [RelayCommand]
    private async Task NewProject()
    {
        if (!await TryCloseProjectAsync())
        {
            return;
        }

        var dialog = new ProjectPropertiesViewModel(_dialogs, isNew: true) { Author = Environment.UserName, Location = _settings.Load().ProjectsFolderOrDefault };
        if (!await _dialogs.ShowDialogAsync(dialog))
        {
            return;
        }

        var name = dialog.Name.Trim();
        var path = NewProjectPath(dialog.Location, name);
        if (File.Exists(path))
        {
            await _dialogs.ShowErrorAsync(Strings.Dialog_NewProject_Title, Loc.Format(Strings.Message_ProjectExists, path));
            return;
        }

        try
        {
            var project = new UmlProject
            {
                Name = name,
                Version = AttributeRowViewModel.NullIfBlank(dialog.Version),
                Description = AttributeRowViewModel.NullIfBlank(dialog.Description),
                Author = AttributeRowViewModel.NullIfBlank(dialog.Author),
            };
            ProjectSerializer.Save(project, path);
            LoadProject(project, path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            await _dialogs.ShowErrorAsync(Strings.Dialog_NewProject_Title, Loc.Format(Strings.Message_ProjectNotCreated, ex.Message));
        }
    }

    /// <summary>
    /// Imports a folder of source files as a new project with a class diagram of their types, in a language of the
    /// installed code import plug-ins (C#, C++, Java, Kotlin, Ruby, TypeScript, …), then lists what the import left out.
    /// </summary>
    [RelayCommand]
    private async Task ImportFolder()
    {
        if (!await TryCloseProjectAsync() || await _dialogs.PickFolderAsync(Strings.Picker_ImportFolder) is not { } folder)
        {
            return;
        }

        if (_codeImporters.Importers.Count == 0)
        {
            await _dialogs.ShowErrorAsync(Strings.Dialog_ImportFolder_Title, NoImporterMessage(Path.GetFileName(folder)));
            return;
        }

        // The languages the folder has files of, the most first; a large folder takes a moment to go through.
        IReadOnlyDictionary<ICodeImporter, int> counts;
        try
        {
            counts = await RunBusyAsync(
                Loc.Format(Strings.Message_FindingSourceFiles, Path.GetFileName(folder)),
                (_, token) => Task.Run(() => SourceFolder.Count(folder, _codeImporters.Importers), token),
                ShowBusyAfter);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        var languages = counts.Where(c => c.Value > 0).OrderByDescending(c => c.Value)
            .Select(c => new ImportLanguage(c.Key, c.Value)).ToList();
        if (languages.Count == 0)
        {
            await _dialogs.ShowErrorAsync(
                Strings.Dialog_ImportFolder_Title,
                Loc.Format(Strings.Message_NoSourceFiles, folder, string.Join(", ", _codeImporters.Importers.Select(i => i.DisplayName))));
            return;
        }

        var dialog = ImportCodeViewModel.ForFolder(_dialogs, folder, languages);
        dialog.Location = _settings.Load().ProjectsFolderOrDefault;
        dialog.Author = Environment.UserName;
        if (await _dialogs.ShowDialogAsync(dialog))
        {
            await CreateImportedProjectAsync(dialog, folder);
        }
    }

    /// <summary>
    /// Imports the folder's source in the dialog's language, showing how far it is, and creates, saves and opens the new
    /// project with the diagram, then shows what was imported and what was left out. Nothing is created when the code
    /// declares no types, or when the import is cancelled.
    /// </summary>
    private async Task CreateImportedProjectAsync(ImportCodeViewModel dialog, string folder)
    {
        var title = dialog.Title;
        var name = dialog.Name.Trim();
        var sourceName = Path.GetFileName(folder);
        var path = NewProjectPath(dialog.Location, name);
        if (File.Exists(path))
        {
            await _dialogs.ShowErrorAsync(title, Loc.Format(Strings.Message_ProjectExists, path));
            return;
        }

        // Reading and parsing every source file can take a while on large code bases.
        var importer = dialog.SelectedLanguage.Importer;
        CodeImportResult result;
        try
        {
            result = await RunBusyAsync(Loc.Format(Strings.Message_FindingSourceFiles, sourceName), (progress, token) => Task.Run(() =>
            {
                var source = SourceFolder.Read(folder, importer.FileExtensions);
                token.ThrowIfCancellationRequested();
                return CodeImporter.Import(source, importer, BusyStep.Map<CodeImportProgress>(progress, p => ImportStep(p, importer.DisplayName)), token);
            }, token));
        }
        catch (OperationCanceledException)
        {
            return;
        }

        try
        {
            if (result.Diagram is not { } diagram)
            {
                await _dialogs.ShowErrorAsync(title, Loc.Format(Strings.Message_NoTypesFound, sourceName));
                return;
            }

            var project = new UmlProject
            {
                Name = name,
                Description = AttributeRowViewModel.NullIfBlank(dialog.Description),
                Author = AttributeRowViewModel.NullIfBlank(dialog.Author),
            };
            diagram.Author = project.Author;
            project.Diagrams.Add(diagram);

            ProjectSerializer.Save(project, path);
            LoadProject(project, path);
            if (AllNodes().OfType<DiagramItemViewModel>().FirstOrDefault() is { } first)
            {
                OpenDiagram(first);
            }

            await _dialogs.ShowMessageAsync(
                title,
                Loc.Format(Strings.Message_CodeImported, result.Summary, string.Join('\n', result.Notes.Select(n => "• " + n))));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            await _dialogs.ShowErrorAsync(title, Loc.Format(Strings.Message_ProjectNotCreated, ex.Message));
        }
    }

    /// <summary>What the busy overlay shows for a stage of an import: the files read so far as a share, then moving bars.</summary>
    private static BusyStep ImportStep(CodeImportProgress progress, string language) => progress.Stage switch
    {
        CodeImportStage.Reading => new(
            Loc.Format(Strings.Message_ImportReading, progress.Done, progress.Total),
            progress.Total == 0 ? null : (double)progress.Done / progress.Total),
        CodeImportStage.Parsing => new(Loc.Format(Strings.Message_ImportParsing, progress.Total, language)),
        _ => new(Loc.Format(Strings.Message_ImportLayingOut, progress.Total)),
    };

    /// <summary>Why a folder cannot be imported: no code import plug-in is installed, and the problems loading them.</summary>
    private string NoImporterMessage(string sourceName)
    {
        var message = Loc.Format(Strings.Message_NoCodeImporter, sourceName, Strings.Message_NoCodeImporter_AnyLanguage,
            InstalledPlugins.ApplicationFolder, InstalledPlugins.UserFolder);
        return string.Join(Environment.NewLine + Environment.NewLine, _codeImporters.Problems.Prepend(message));
    }

    /// <summary>The file of a new project: <c>Location/Name/Name.umlwiz</c>, with characters that files cannot have replaced.</summary>
    private static string NewProjectPath(string location, string name) =>
        Path.Combine(location, SafeFileName(name), SafeFileName(name) + UmlProject.FileExtension);

    [RelayCommand]
    private async Task OpenProject()
    {
        var path = await _dialogs.PickOpenFileAsync(Strings.Picker_OpenProject, Strings.Picker_ProjectFiles, UmlProject.FilePatterns);
        if (path is not null)
        {
            await OpenProjectFileAsync(path);
        }
    }

    public async Task OpenProjectFileAsync(string path)
    {
        if (!await TryCloseProjectAsync())
        {
            return;
        }

        try
        {
            LoadProject(ProjectSerializer.Load(path), path);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            await _dialogs.ShowErrorAsync(Strings.Picker_OpenProject, Loc.Format(Strings.Message_ProjectNotOpened, ex.Message));
        }
    }

    #region Recent projects

    public const int MaxRecentProjects = 10;

    private static readonly StringComparer PathComparer =
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    /// <summary>Full paths of the most recently used projects, most recent first.</summary>
    public IReadOnlyList<string> RecentProjects => _recentProjects;

    /// <summary>Entries of File → Recent Projects: the projects, a separator and "Clear Recent Projects".</summary>
    public ObservableCollection<RecentProjectEntry> RecentProjectsMenu { get; } = [];

    public bool HasRecentProjects => _recentProjects.Count > 0;

    /// <summary>The projects of the Recent Projects section of the left panel, most recent first.</summary>
    public IReadOnlyList<RecentProjectItem> RecentProjectItems => _recentProjects.Select(p => new RecentProjectItem(p, CopyRecentProjectCommand, DeleteRecentProjectCommand)).ToList();

    [RelayCommand]
    private async Task OpenRecentProject(string? path)
    {
        if (path is null)
        {
            return;
        }

        if (!File.Exists(path))
        {
            await OfferToForgetMissingProjectAsync(path);
            return;
        }

        await OpenProjectFileAsync(path);
    }

    /// <summary>Says a recent project's file is gone, and removes it from the recent projects when the user agrees.</summary>
    private async Task OfferToForgetMissingProjectAsync(string path)
    {
        var answer = await _dialogs.ConfirmAsync(Strings.Message_RecentProjectMissing_Title, Loc.Format(Strings.Message_RecentProjectMissing, path));
        if (answer == ConfirmResult.Yes)
        {
            UpdateRecentProjects(_recentProjects.Where(p => !PathComparer.Equals(p, path)));
        }
    }

    /// <summary>
    /// Copies a recent project to "<c>name (copy)</c>" in the same folder ("<c>name (copy 2)</c>" and so on when that is
    /// taken), its project name changed the same way, and puts the copy first among the recent projects, without opening
    /// it. The user's tabs and expanded folders go with it; the original is not touched.
    /// </summary>
    [RelayCommand]
    private async Task CopyRecentProject(string? path)
    {
        if (path is null)
        {
            return;
        }

        if (!File.Exists(path))
        {
            await OfferToForgetMissingProjectAsync(path);
            return;
        }

        try
        {
            var project = ProjectSerializer.Load(path);
            var folder = Path.GetDirectoryName(path)!;
            var fileName = Path.GetFileNameWithoutExtension(path);
            var (copyPath, number) = (string.Empty, 1);
            for (; ; number++)
            {
                copyPath = Path.Combine(folder, CopyName(fileName, number) + UmlProject.FileExtension);
                if (!File.Exists(copyPath))
                {
                    break;
                }
            }

            project.Name = CopyName(project.Name, number);
            project.Created = project.Modified = DateTimeOffset.Now;
            ProjectSerializer.Save(project, copyPath);
            if (_projectStates.Load(path) is { } state)
            {
                _projectStates.Save(copyPath, new ProjectUserState
                {
                    OpenDiagramIds = [.. state.OpenDiagramIds],
                    ActiveDiagramId = state.ActiveDiagramId,
                    ExpandedFolderIds = state.ExpandedFolderIds is null ? null : [.. state.ExpandedFolderIds],
                });
            }

            AddRecentProject(copyPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            await _dialogs.ShowErrorAsync(Strings.Recent_CopyProject, Loc.Format(Strings.Message_ProjectNotCopied, path, ex.Message));
        }

        static string CopyName(string name, int number) =>
            number == 1 ? Loc.Format(Strings.Recent_CopyProject_Name, name) : Loc.Format(Strings.Recent_CopyProject_NameNumbered, name, number);
    }

    /// <summary>
    /// Deletes a recent project's file for good, after the user confirms it, and removes it from the recent projects
    /// with the tabs and folders kept for it. The file is not moved to the Recycle Bin, so it cannot be restored. A file
    /// that is already gone is only removed from the list.
    /// </summary>
    [RelayCommand]
    private async Task DeleteRecentProject(string? path)
    {
        if (path is null || (ProjectPath is not null && PathComparer.Equals(path, ProjectPath)))
        {
            return;
        }

        if (File.Exists(path))
        {
            var answer = await _dialogs.ConfirmAsync(
                Strings.Message_DeleteProjectFile_Title,
                Loc.Format(Strings.Message_DeleteProjectFile, Path.GetFileNameWithoutExtension(path), path));
            if (answer != ConfirmResult.Yes)
            {
                return;
            }

            try
            {
                File.Delete(path);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                await _dialogs.ShowErrorAsync(Strings.Message_DeleteProjectFile_Title, Loc.Format(Strings.Message_ProjectFileNotDeleted, path, ex.Message));
                return;
            }
        }

        _projectStates.Delete(path);
        UpdateRecentProjects(_recentProjects.Where(p => !PathComparer.Equals(p, path)));
    }

    [RelayCommand]
    private void ClearRecentProjects() => UpdateRecentProjects([]);

    /// <summary>Moves <paramref name="path"/> to the top of the list, keeping at most <see cref="MaxRecentProjects"/> entries.</summary>
    private void AddRecentProject(string path) =>
        UpdateRecentProjects(_recentProjects.Where(p => !PathComparer.Equals(p, path)).Prepend(path));

    private void UpdateRecentProjects(IEnumerable<string> paths)
    {
        _recentProjects = Normalize(paths);
        _recentStore.Save(_recentProjects);
        BuildRecentProjectsMenu();
    }

    /// <summary>Full paths without duplicates, at most <see cref="MaxRecentProjects"/>; malformed entries are dropped.</summary>
    private static List<string> Normalize(IEnumerable<string> paths) =>
        paths.Select(TryGetFullPath).OfType<string>().Distinct(PathComparer).Take(MaxRecentProjects).ToList();

    private static string? TryGetFullPath(string path)
    {
        try
        {
            return string.IsNullOrWhiteSpace(path) ? null : Path.GetFullPath(path);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }
    }

    private void BuildRecentProjectsMenu()
    {
        RecentProjectsMenu.Clear();
        for (var i = 0; i < _recentProjects.Count; i++)
        {
            var path = _recentProjects[i];
            RecentProjectsMenu.Add(new RecentProjectEntry(RecentProjectEntry.FormatHeader(i, path), OpenRecentProjectCommand, path, path));
        }

        if (_recentProjects.Count > 0)
        {
            RecentProjectsMenu.Add(RecentProjectEntry.Separator);
            RecentProjectsMenu.Add(new RecentProjectEntry(Strings.Menu_File_ClearRecentProjects, ClearRecentProjectsCommand));
        }

        OnPropertyChanged(nameof(RecentProjects));
        OnPropertyChanged(nameof(RecentProjectItems));
        OnPropertyChanged(nameof(HasRecentProjects));
    }

    #endregion

    [RelayCommand]
    private async Task CloseProject() => await TryCloseProjectAsync();

    /// <summary>Closes the project after asking to save changes; false when the user cancelled.</summary>
    public async Task<bool> TryCloseProjectAsync()
    {
        if (!await ConfirmSaveChangesAsync())
        {
            return false;
        }

        SaveUserState();
        foreach (var diagram in OpenDiagrams.ToList())
        {
            diagram.Dispose();
        }

        OpenDiagrams.Clear();
        ActiveDiagram = null;
        ProjectPath = null;
        Project = null;
        RebuildTree();
        return true;
    }

    /// <summary>Asks whether to save unsaved diagrams; false when the user cancelled.</summary>
    public async Task<bool> ConfirmSaveChangesAsync()
    {
        var dirty = OpenDiagrams.Where(d => d.IsDirty).ToList();
        if (dirty.Count == 0)
        {
            return true;
        }

        var names = string.Join("\n", dirty.Select(d => "  • " + d.Name));
        switch (await _dialogs.ConfirmAsync(Strings.Message_UnsavedChanges_Title, Loc.Format(Strings.Message_UnsavedChanges_Many, names), allowCancel: true))
        {
            case ConfirmResult.Yes:
                return await SaveDiagramsAsync(dirty);
            case ConfirmResult.No:
                return true;
            default:
                return false;
        }
    }

    [RelayCommand(CanExecute = nameof(HasUnsavedChanges))]
    private async Task SaveProject()
    {
        if (Project is null)
        {
            return;
        }

        await SaveDiagramsAsync(OpenDiagrams.Where(d => d.IsDirty).ToList());
    }

    /// <summary>
    /// Saves the project, with the unsaved changes of the open diagrams, to a new file, which then becomes the
    /// project file. The previous file is left as it was last saved.
    /// </summary>
    [RelayCommand]
    private async Task SaveProjectAs()
    {
        if (Project is null || ProjectPath is null)
        {
            return;
        }

        var path = await _dialogs.PickSaveFileAsync(
            Strings.Picker_SaveProjectAs,
            Path.GetFileNameWithoutExtension(ProjectPath) + UmlProject.FileExtension,
            UmlProject.FileExtension.TrimStart('.'),
            Strings.Picker_ProjectFiles,
            [$"*{UmlProject.FileExtension}"]);
        if (path is null || !await SaveDiagramsAsync(OpenDiagrams.Where(d => d.IsDirty).ToList(), path))
        {
            return;
        }

        ProjectPath = Path.GetFullPath(path);
        AddRecentProject(ProjectPath);
    }

    [RelayCommand]
    private async Task EditProjectProperties()
    {
        if (Project is null)
        {
            return;
        }

        var dialog = new ProjectPropertiesViewModel(_dialogs, isNew: false, _clipboard)
        {
            Name = Project.Name,
            Version = Project.Version,
            Description = Project.Description,
            Author = Project.Author,
            Folder = ProjectPath is null ? null : Path.GetDirectoryName(ProjectPath),
        };
        if (!await _dialogs.ShowDialogAsync(dialog))
        {
            return;
        }

        Project.Name = dialog.Name.Trim();
        Project.Version = AttributeRowViewModel.NullIfBlank(dialog.Version);
        Project.Description = AttributeRowViewModel.NullIfBlank(dialog.Description);
        Project.Author = AttributeRowViewModel.NullIfBlank(dialog.Author);
        await SaveProjectFileAsync();
        ProjectRoot?.Refresh();
        OnPropertyChanged(nameof(WindowTitle));
    }

    private void LoadProject(UmlProject project, string path)
    {
        ProjectPath = Path.GetFullPath(path);
        Project = project;
        _userState = _projectStates.Load(ProjectPath) ?? ProjectUserState.ReadFromProjectFile(ProjectPath) ?? new ProjectUserState();
        _userState.Normalize(project);
        AddRecentProject(ProjectPath);
        RebuildTree(expandAll: true);
        SelectedNode = ProjectRoot;
        RestoreOpenTabs();
        RestoreExpandedFolders();
    }

    /// <summary>
    /// Expands the folders the user had expanded, and collapses the others; everything stays expanded when there is no
    /// saved state. The selected diagram, that of the active tab, is kept visible.
    /// </summary>
    private void RestoreExpandedFolders()
    {
        if (_userState.ExpandedFolderIds is not { } ids)
        {
            return;
        }

        var expanded = ids.ToHashSet();
        foreach (var node in AllNodes().Where(n => n is FolderNodeViewModel or ProjectRootNodeViewModel))
        {
            node.IsExpanded = expanded.Contains(node.Id);
        }

        Reveal(SelectedNode);
    }

    /// <summary>Ids of the expanded folders, and of the project root when it is expanded.</summary>
    private List<string> ExpandedFolderIds() =>
        AllNodes().Where(n => (n is FolderNodeViewModel or ProjectRootNodeViewModel) && n.IsExpanded).Select(n => n.Id).ToList();

    /// <summary>Reopens the user's tabs of the project, in their order, and activates the saved active tab.</summary>
    private void RestoreOpenTabs()
    {
        var items = AllNodes().OfType<DiagramItemViewModel>().ToDictionary(i => i.Id);
        foreach (var id in _userState.OpenDiagramIds)
        {
            if (items.TryGetValue(id, out var item))
            {
                Activate(CreateDiagramViewModel(item));
            }
        }

        ActiveDiagram = OpenDiagrams.FirstOrDefault(d => d.Id == _userState.ActiveDiagramId) ?? OpenDiagrams.LastOrDefault();
    }

    /// <summary>
    /// Keeps the open tabs and the expanded folders for the user when they differ from the saved ones; called when the
    /// project or the application closes. The project file is not touched.
    /// </summary>
    public void SaveUserState()
    {
        if (Project is null || ProjectPath is null ||
            (_userState.OpenDiagramIds.SequenceEqual(OpenDiagrams.Select(d => d.Id)) && _userState.ActiveDiagramId == ActiveDiagram?.Id
             && (_userState.ExpandedFolderIds ?? AllNodes().Where(n => n is FolderNodeViewModel or ProjectRootNodeViewModel).Select(n => n.Id))
                 .ToHashSet().SetEquals(ExpandedFolderIds())))
        {
            return;
        }

        KeepUserState(ProjectPath);
    }

    /// <summary>
    /// Keeps the open tabs, their order, the active tab and the expanded folders for the user, as the state of the
    /// project file at <paramref name="projectPath"/>. The store ignores failures: the state is a convenience and must
    /// not keep the user from saving or closing.
    /// </summary>
    private void KeepUserState(string projectPath)
    {
        _userState = new ProjectUserState
        {
            OpenDiagramIds = OpenDiagrams.Select(d => d.Id).ToList(),
            ActiveDiagramId = ActiveDiagram?.Id,
            ExpandedFolderIds = ExpandedFolderIds(),
        };
        _projectStates.Save(projectPath, _userState);
    }

    /// <summary>
    /// Recreates the explorer tree from the project, keeping expanded folders expanded and the selection.
    /// </summary>
    /// <param name="select">Id of the node to select; defaults to the currently selected node.</param>
    private void RebuildTree(string? select = null, bool expandAll = false)
    {
        var collapsed = AllNodes().Where(n => !n.IsExpanded).Select(n => n.Id).ToHashSet();
        select ??= SelectedNode?.Id;
        SelectedNode = null;
        ExplorerNodes.Clear();
        if (Project is not null)
        {
            var root = new ProjectRootNodeViewModel(Project)
            {
                IsExpanded = expandAll || !collapsed.Contains(ProjectRootNodeViewModel.RootId),
            };
            AddNodes(root.Children, Project.Folders, Project.TopLevelDiagrams(), parent: null);
            ExplorerNodes.Add(root);
        }

        SelectedNode = select is null ? null : AllNodes().FirstOrDefault(n => n.Id == select);
        Reveal(SelectedNode);
        OnPropertyChanged(nameof(IsProjectEmpty));

        void AddNodes(
            ICollection<ProjectNodeViewModel> target, IEnumerable<ProjectFolder> folders, IEnumerable<DiagramDocument> diagrams,
            FolderNodeViewModel? parent)
        {
            foreach (var folder in folders)
            {
                var node = new FolderNodeViewModel(folder)
                {
                    Parent = parent,
                    IsExpanded = expandAll || !collapsed.Contains(folder.Id),
                };
                AddNodes(
                    node.Children,
                    folder.Folders,
                    folder.DiagramIds.Select(Project!.FindDiagram).OfType<DiagramDocument>(),
                    node);
                target.Add(node);
            }

            foreach (var diagram in diagrams)
            {
                target.Add(new DiagramItemViewModel(diagram) { Parent = parent });
            }
        }
    }

    /// <summary>All nodes of the explorer tree, the project root first, depth first.</summary>
    public IEnumerable<ProjectNodeViewModel> AllNodes()
    {
        var stack = new Stack<ProjectNodeViewModel>(ExplorerNodes.Reverse());
        while (stack.TryPop(out var node))
        {
            yield return node;
            var children = node switch
            {
                FolderNodeViewModel folder => folder.Children,
                ProjectRootNodeViewModel root => root.Children,
                _ => null,
            };
            foreach (var child in children?.Reverse() ?? [])
            {
                stack.Push(child);
            }
        }
    }

    /// <summary>Expands the project root and the folders that contain <paramref name="node"/>, so it is visible.</summary>
    private void Reveal(ProjectNodeViewModel? node)
    {
        if (node is null or ProjectRootNodeViewModel)
        {
            return;
        }

        for (var folder = node.Parent; folder is not null; folder = folder.Parent)
        {
            folder.IsExpanded = true;
        }

        ProjectRoot?.IsExpanded = true;
    }

    /// <summary>
    /// Applies a change to the folder hierarchy or the diagram order and saves the project; the change is undone when
    /// saving fails.
    /// </summary>
    private async Task<bool> ChangeStructureAsync(Action change, string? select = null)
    {
        var folders = Project!.Folders.Select(f => f.Clone()).ToList();
        var diagrams = Project.Diagrams.ToList();
        change();
        if (!await SaveProjectFileAsync())
        {
            Project.Folders = folders;
            Project.Diagrams.Clear();
            Project.Diagrams.AddRange(diagrams);
            return false;
        }

        RebuildTree(select);
        return true;
    }

    /// <summary>The folder new items go into: the selected folder, or the folder of the selected diagram.</summary>
    private string? TargetFolderId => SelectedNode switch
    {
        FolderNodeViewModel folder => folder.Id,
        { Parent: { } parent } => parent.Id,
        _ => null,
    };

    /// <summary>
    /// Writes the project file, which contains the saved state of every diagram, to <paramref name="path"/>
    /// (the current project file by default).
    /// </summary>
    private async Task<bool> SaveProjectFileAsync(string? path = null)
    {
        try
        {
            Project!.Modified = DateTimeOffset.Now;
            ProjectSerializer.Save(Project, path ?? ProjectPath!);
            KeepUserState(path ?? ProjectPath!);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            await _dialogs.ShowErrorAsync(Strings.Dialog_SaveProject_Title, Loc.Format(Strings.Message_ProjectNotSaved, ex.Message));
            return false;
        }
    }

    /// <summary>
    /// Copies the edited state of <paramref name="diagrams"/> into the project and writes the project file.
    /// On failure the project keeps the previously saved diagrams and the editors stay dirty.
    /// </summary>
    private async Task<bool> SaveDiagramsAsync(IReadOnlyCollection<DiagramViewModel> diagrams, string? path = null)
    {
        var previous = diagrams.Select(d => Project!.FindDiagram(d.Id)).OfType<DiagramDocument>().ToList();
        foreach (var diagram in diagrams)
        {
            ReplaceSavedDiagram(DiagramSerializer.Clone(diagram.Document));
        }

        if (!await SaveProjectFileAsync(path))
        {
            previous.ForEach(ReplaceSavedDiagram);
            return false;
        }

        foreach (var diagram in diagrams)
        {
            diagram.Editor.MarkSaved();
        }

        return true;
    }

    /// <summary>Replaces the diagram with the same id in the project and the explorer.</summary>
    private void ReplaceSavedDiagram(DiagramDocument document)
    {
        var index = Project!.Diagrams.FindIndex(d => d.Id == document.Id);
        if (index < 0)
        {
            return;
        }

        Project.Diagrams[index] = document;
        AllNodes().OfType<DiagramItemViewModel>().FirstOrDefault(i => i.Id == document.Id)?.Update(document);
    }

    #endregion

    #region Diagrams

    [RelayCommand]
    private Task NewClassDiagram() => NewDiagramAsync(DiagramType.Class);

    [RelayCommand]
    private Task NewSequenceDiagram() => NewDiagramAsync(DiagramType.Sequence);

    /// <summary>Creates a diagram of the given type in the folder of the explorer selection.</summary>
    [RelayCommand]
    private Task NewDiagram(DiagramType type) => NewDiagramAsync(type);

    /// <summary>One entry per diagram type, for the explorer's new-diagram button and the Diagram menu.</summary>
    public IReadOnlyList<NewDiagramEntry> NewDiagramMenu => field ??= DiagramTypeInfo.All
        .Select(info => new NewDiagramEntry(Loc.Format(Strings.Menu_Diagram_NewDiagramEntry, info.DisplayName), info.Icon, NewDiagramCommand, info.Type))
        .ToList();

    private async Task NewDiagramAsync(DiagramType type)
    {
        if (Project is null)
        {
            return;
        }

        var prefix = DiagramTypeInfo.For(type).NamePrefix;
        var number = 1;
        while (Project.Diagrams.Any(d => d.Name == prefix + number))
        {
            number++;
        }

        var dialog = new NewDiagramViewModel(prefix + number, type);
        if (!await _dialogs.ShowDialogAsync(dialog))
        {
            return;
        }

        var document = DiagramDocument.Create(dialog.Name.Trim(), dialog.Type);
        document.Author = Project.Author;
        await AddDiagramAsync(document, Strings.Dialog_NewDiagram_Title);
    }

    /// <summary>Copies a stand-alone diagram file (as written by schema 1 projects) into the project.</summary>
    [RelayCommand]
    private async Task ImportDiagram()
    {
        if (Project is null)
        {
            return;
        }

        var path = await _dialogs.PickOpenFileAsync(Strings.Picker_ImportDiagram, Strings.Picker_DiagramFiles, DiagramPatterns);
        if (path is null)
        {
            return;
        }

        DiagramDocument document;
        try
        {
            document = DiagramSerializer.Load(path);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            await _dialogs.ShowErrorAsync(Strings.Picker_ImportDiagram, Loc.Format(Strings.Message_InvalidDiagram, ex.Message));
            return;
        }

        if (Project.FindDiagram(document.Id) is not null)
        {
            document.Id = ModelElement.NewId();
        }

        await AddDiagramAsync(document, Strings.Picker_ImportDiagram);
    }

    /// <summary>
    /// Adds a diagram to the folder of the current selection, saves the project and opens the diagram; false when saving
    /// failed (the error is shown).
    /// </summary>
    private async Task<bool> AddDiagramAsync(DiagramDocument document, string title, int? index = null)
    {
        var folderId = TargetFolderId;
        Project!.Diagrams.Add(document);
        if (folderId is not null || index is not null)
        {
            Project.MoveDiagram(document.Id, folderId, index);
        }

        try
        {
            Project.Modified = DateTimeOffset.Now;
            ProjectSerializer.Save(Project, ProjectPath!);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Project.RemoveDiagram(document.Id);
            await _dialogs.ShowErrorAsync(title, Loc.Format(Strings.Message_DiagramNotAdded, ex.Message));
            return false;
        }

        RebuildTree(select: document.Id);
        Activate(CreateDiagramViewModel(SelectedDiagramItem!));
        return true;
    }

    /// <summary>
    /// Copies the diagram selected in the explorer, as last saved, to a new diagram named "<c>name (copy)</c>" just below
    /// it in the same folder, and opens the copy.
    /// </summary>
    [RelayCommand]
    private async Task CopyDiagram()
    {
        if (Project is null || SelectedDiagramItem is not { } item)
        {
            return;
        }

        var copy = DiagramSerializer.Clone(item.Document);
        copy.Id = ModelElement.NewId();
        copy.Name = Loc.Format(Strings.Explorer_CopyDiagram_Name, item.Document.Name);
        var siblings = Project.FolderOfDiagram(item.Id)?.DiagramIds ?? Project.TopLevelDiagrams().Select(d => d.Id).ToList();
        await AddDiagramAsync(copy, Strings.Explorer_CopyDiagram, siblings.IndexOf(item.Id) + 1);
    }

    [RelayCommand]
    private void OpenDiagram(DiagramItemViewModel? item)
    {
        item ??= SelectedDiagramItem;
        if (item is null)
        {
            return;
        }

        ActiveDiagram = OpenDiagrams.FirstOrDefault(d => d.Id == item.Id) ?? Activate(CreateDiagramViewModel(item));
    }

    /// <summary>Creates a folder inside the selected folder (or the folder of the selected diagram).</summary>
    [RelayCommand]
    private async Task NewFolder()
    {
        if (Project is null)
        {
            return;
        }

        var parentId = TargetFolderId;
        var dialog = new TextInputViewModel(Strings.Dialog_NewFolder_Title, Strings.Dialog_FolderName, Strings.Dialog_NewFolder_DefaultName);
        if (!await _dialogs.ShowDialogAsync(dialog))
        {
            return;
        }

        ProjectFolder? folder = null;
        await ChangeStructureAsync(() => folder = Project.AddFolder(dialog.Text.Trim(), parentId), select: null);
        if (folder is not null && AllNodes().FirstOrDefault(n => n.Id == folder.Id) is { } node)
        {
            SelectedNode = node;
            Reveal(node);
        }
    }

    /// <summary>Renames the selected folder or diagram.</summary>
    [RelayCommand]
    private async Task RenameItem()
    {
        switch (SelectedNode)
        {
            case FolderNodeViewModel folder:
            {
                var dialog = new TextInputViewModel(Strings.Dialog_RenameFolder_Title, Strings.Dialog_FolderName, folder.Name);
                if (await _dialogs.ShowDialogAsync(dialog) && dialog.Text.Trim() != folder.Name)
                {
                    var name = dialog.Text.Trim();
                    await ChangeStructureAsync(() => Project!.FindFolder(folder.Id)!.Name = name);
                }

                break;
            }

            case DiagramItemViewModel item:
                await RenameDiagramAsync(item);
                break;

            case ProjectRootNodeViewModel root:
                await RenameProjectAsync(root);
                break;
        }
    }

    private async Task RenameProjectAsync(ProjectRootNodeViewModel root)
    {
        var dialog = new TextInputViewModel(Strings.Dialog_RenameProject_Title, Strings.Dialog_ProjectName, root.Name);
        if (!await _dialogs.ShowDialogAsync(dialog) || dialog.Text.Trim() == root.Name)
        {
            return;
        }

        var previous = root.Project.Name;
        root.Project.Name = dialog.Text.Trim();
        if (!await SaveProjectFileAsync())
        {
            root.Project.Name = previous;
            return;
        }

        root.Refresh();
        OnPropertyChanged(nameof(WindowTitle));
    }

    /// <summary>Edits the name, description and author of the diagram selected in the explorer, and saves them.</summary>
    [RelayCommand]
    private async Task EditDiagramProperties()
    {
        if (Project is null || SelectedDiagramItem is not { } item)
        {
            return;
        }

        var saved = item.Document;
        var dialog = new DiagramPropertiesViewModel(saved);
        if (!await _dialogs.ShowDialogAsync(dialog) || !dialog.Differs(saved))
        {
            return;
        }

        var updated = DiagramSerializer.Clone(saved);
        dialog.ApplyTo(updated);
        ReplaceSavedDiagram(updated);
        if (!await SaveProjectFileAsync())
        {
            ReplaceSavedDiagram(saved);
            return;
        }

        OpenDiagrams.FirstOrDefault(d => d.Id == item.Id)?.ApplyProperties(updated.Name, updated.Description, updated.Author, updated.TaggedValues);
    }

    private async Task RenameDiagramAsync(DiagramItemViewModel item)
    {
        var dialog = new TextInputViewModel(Strings.Dialog_RenameDiagram_Title, Strings.Dialog_DiagramName, item.Name);
        if (!await _dialogs.ShowDialogAsync(dialog) || dialog.Text.Trim() == item.Name)
        {
            return;
        }

        var name = dialog.Text.Trim();
        var saved = item.Document;
        var renamed = DiagramSerializer.Clone(saved);
        renamed.Name = name;
        ReplaceSavedDiagram(renamed);
        if (!await SaveProjectFileAsync())
        {
            ReplaceSavedDiagram(saved);
            return;
        }

        OpenDiagrams.FirstOrDefault(d => d.Id == item.Id)?.Rename(name);
    }

    /// <summary>Moves the selected folder or diagram to another folder or to the top level.</summary>
    [RelayCommand]
    private async Task MoveItem()
    {
        if (Project is null || SelectedNode is not ({ } node and (FolderNodeViewModel or DiagramItemViewModel)))
        {
            return;
        }

        var isFolder = node is FolderNodeViewModel;
        var current = node.Parent?.Id;
        var targets = new List<MoveTarget> { new(null, Strings.Dialog_MoveTo_TopLevel, 0) };
        targets.AddRange(FolderTargets(Project.Folders, 1));
        if (isFolder)
        {
            targets.RemoveAll(t => !Project.CanMoveFolder(node.Id, t.FolderId));
        }

        var dialog = new MoveToViewModel(node.Name, targets, targets.FirstOrDefault(t => t.FolderId == current));
        if (!await _dialogs.ShowDialogAsync(dialog) || dialog.Selected!.FolderId == current)
        {
            return;
        }

        await MoveNodeAsync(node, dialog.Selected.FolderId);

        static IEnumerable<MoveTarget> FolderTargets(IEnumerable<ProjectFolder> folders, int depth) =>
            folders.SelectMany(f => FolderTargets(f.Folders, depth + 1).Prepend(new MoveTarget(f.Id, f.Name, depth)));
    }

    /// <summary>
    /// True when <paramref name="node"/> can be moved into the folder <paramref name="folderId"/> (the top level
    /// when <c>null</c>): it is not already there, and a folder is not moved into itself or one of its subfolders.
    /// </summary>
    public bool CanMoveNode(ProjectNodeViewModel node, string? folderId) =>
        Project is not null && node.Parent?.Id != folderId && (node is FolderNodeViewModel
            ? Project.CanMoveFolder(node.Id, folderId)
            : Project.FindDiagram(node.Id) is not null && (folderId is null || Project.FindFolder(folderId) is not null));

    /// <summary>Moves a folder or diagram into a folder, or to the top level when <paramref name="folderId"/> is <c>null</c>, and saves.</summary>
    public async Task<bool> MoveNodeAsync(ProjectNodeViewModel node, string? folderId)
    {
        if (!CanMoveNode(node, folderId))
        {
            return false;
        }

        return await ChangeStructureAsync(
            () =>
            {
                if (node is FolderNodeViewModel)
                {
                    Project!.MoveFolder(node.Id, folderId);
                }
                else
                {
                    Project!.MoveDiagram(node.Id, folderId);
                }
            },
            select: node.Id);
    }

    /// <summary>The diagram nodes directly in <paramref name="folder"/> (the top level when <c>null</c>), in display order.</summary>
    public IEnumerable<DiagramItemViewModel> DiagramsIn(FolderNodeViewModel? folder) =>
        (folder?.Children ?? ProjectTree).OfType<DiagramItemViewModel>();

    /// <summary>
    /// True when placing <paramref name="item"/> at <paramref name="index"/> among the other diagrams of the folder
    /// <paramref name="folderId"/> (the top level when <c>null</c>) changes where it is.
    /// </summary>
    public bool CanMoveDiagram(DiagramItemViewModel item, string? folderId, int index) =>
        Project?.FindDiagram(item.Id) is not null &&
        (folderId is null || Project.FindFolder(folderId) is not null) &&
        (item.Parent?.Id != folderId || DiagramsIn(item.Parent).ToList().IndexOf(item) != index);

    /// <summary>
    /// Moves a diagram to <paramref name="index"/> among the other diagrams of the folder <paramref name="folderId"/>
    /// (the top level when <c>null</c>), e.g. to reorder it within its folder, and saves.
    /// </summary>
    public async Task<bool> MoveDiagramAsync(DiagramItemViewModel item, string? folderId, int index)
    {
        if (!CanMoveDiagram(item, folderId, index))
        {
            return false;
        }

        return await ChangeStructureAsync(() => Project!.MoveDiagram(item.Id, folderId, index), select: item.Id);
    }

    /// <summary>Deletes the selected diagram, or removes the selected folder (its contents move up one level).</summary>
    [RelayCommand]
    private async Task DeleteItem()
    {
        switch (SelectedNode)
        {
            case FolderNodeViewModel folder:
            {
                var destination = folder.Parent is { } parent
                    ? Loc.Format(Strings.Message_DeleteFolder_ToFolder, parent.Name)
                    : Strings.Message_DeleteFolder_ToTopLevel;
                var answer = await _dialogs.ConfirmAsync(
                    Strings.Message_DeleteFolder_Title,
                    Loc.Format(Strings.Message_DeleteFolder, folder.Name, destination));
                if (answer == ConfirmResult.Yes)
                {
                    await ChangeStructureAsync(() => Project!.RemoveFolder(folder.Id), select: folder.Parent?.Id);
                }

                break;
            }

            case DiagramItemViewModel item:
                await DeleteDiagramAsync(item);
                break;
        }
    }

    private async Task DeleteDiagramAsync(DiagramItemViewModel item)
    {
        var answer = await _dialogs.ConfirmAsync(
            Strings.Message_DeleteDiagram_Title,
            Loc.Format(Strings.Message_DeleteDiagram, item.Name));
        if (answer != ConfirmResult.Yes)
        {
            return;
        }

        var index = Project!.Diagrams.IndexOf(item.Document);
        var folders = Project.Folders.Select(f => f.Clone()).ToList();
        Project.RemoveDiagram(item.Id);
        if (!await SaveProjectFileAsync())
        {
            Project.Diagrams.Insert(index, item.Document);
            Project.Folders = folders;
            return;
        }

        if (OpenDiagrams.FirstOrDefault(d => d.Id == item.Id) is { } open)
        {
            OpenDiagrams.Remove(open);
            open.Dispose();
        }

        RebuildTree(select: item.Parent?.Id);
    }

    public async Task CloseDiagramAsync(DiagramViewModel diagram)
    {
        if (diagram.IsDirty)
        {
            var answer = await _dialogs.ConfirmAsync(
                Strings.Message_UnsavedChanges_Title, Loc.Format(Strings.Message_UnsavedChanges_One, diagram.Name), allowCancel: true);
            if (answer == ConfirmResult.Cancel || (answer == ConfirmResult.Yes && !await SaveDiagramsAsync([diagram])))
            {
                return;
            }
        }

        // Closing the active tab activates its right neighbour, or the left one when it was the last tab.
        if (ActiveDiagram == diagram)
        {
            var index = OpenDiagrams.IndexOf(diagram);
            ActiveDiagram = OpenDiagrams.ElementAtOrDefault(index + 1) ?? OpenDiagrams.ElementAtOrDefault(index - 1);
        }

        OpenDiagrams.Remove(diagram);
        diagram.Dispose();
    }

    /// <summary>Closes every open diagram tab after asking to save unsaved changes; the project stays open.</summary>
    [RelayCommand(CanExecute = nameof(HasOpenDiagrams))]
    private async Task CloseAllTabs()
    {
        if (!await ConfirmSaveChangesAsync())
        {
            return;
        }

        var diagrams = OpenDiagrams.ToList();
        ActiveDiagram = null;
        OpenDiagrams.Clear();

        // With no diagram open, the explorer should not keep one selected.
        if (SelectedNode is DiagramItemViewModel)
        {
            SelectedNode = null;
        }

        foreach (var diagram in diagrams)
        {
            diagram.Dispose();
        }
    }

    /// <summary>
    /// Exports the diagram selected in the explorer, whether or not it is open. An open diagram is exported as shown,
    /// including unsaved changes.
    /// </summary>
    [RelayCommand]
    private async Task ExportPng()
    {
        if (SelectedDiagramItem is not { } item)
        {
            return;
        }

        var document = OpenDiagrams.FirstOrDefault(d => d.Id == item.Id)?.Document ?? item.Document;
        var options = new ExportPngViewModel(
            _dialogs,
            document.Name,
            _exportFolder ?? (ProjectPath is null ? string.Empty : Path.GetDirectoryName(ProjectPath) ?? string.Empty),
            SafeFileName(document.Name) + ".png",
            _exportTransparent);

        // Ask again, with the same entries, when the user does not want to replace an existing file.
        string path;
        while (true)
        {
            if (!await _dialogs.ShowDialogAsync(options))
            {
                return;
            }

            path = options.FullPath!;
            if (!File.Exists(path) || await _dialogs.ConfirmAsync(
                    Strings.Dialog_Export_Title,
                    Loc.Format(Strings.Message_ReplaceFile, Path.GetFileName(path), Path.GetDirectoryName(path))) == ConfirmResult.Yes)
            {
                break;
            }
        }

        // Offer the same folder and background next time.
        _exportFolder = Path.GetDirectoryName(path);
        _exportTransparent = options.TransparentBackground;
        try
        {
            // Rendering completes missing layout entries; keep that off the project and the editor.
            DiagramExporter.ExportPng(
                DiagramSerializer.Clone(document), path, transparent: options.TransparentBackground,
                defaults: _settings.Load().StyleFor(document.DiagramType));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            await _dialogs.ShowErrorAsync(Strings.Dialog_Export_Title, ex.Message);
        }
    }

    /// <summary>
    /// Creates a class diagram from an OpenAPI 3.x or Swagger 2.0 description in the folder of the selection, and lists
    /// what could not be kept.
    /// </summary>
    [RelayCommand]
    private async Task ImportOpenApi()
    {
        if (Project is null ||
            await _dialogs.PickOpenFileAsync(Strings.Picker_ImportOpenApi, Strings.Picker_OpenApiFiles, OpenApiPatterns) is not { } path)
        {
            return;
        }

        OpenApiImportResult result;
        try
        {
            result = OpenApiImporter.ImportFile(path);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            await _dialogs.ShowErrorAsync(Strings.Picker_ImportOpenApi, Loc.Format(Strings.Message_InvalidOpenApi, ex.Message));
            return;
        }

        if (await AddDiagramAsync(result.Document, Strings.Picker_ImportOpenApi) && result.Warnings.Count > 0)
        {
            await _dialogs.ShowMessageAsync(
                Strings.Picker_ImportOpenApi,
                Loc.Format(Strings.Message_OpenApiImportNotes, result.Document.Name, Notes(result.Warnings)));
        }
    }

    /// <summary>
    /// Writes the class diagram selected in the explorer as an OpenAPI 3.0 description; an open diagram is exported as
    /// shown, including unsaved changes.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanGenerateCode))]
    private async Task ExportOpenApi()
    {
        if (SelectedDiagramItem is not { } item)
        {
            return;
        }

        var document = OpenDiagrams.FirstOrDefault(d => d.Id == item.Id)?.Document ?? item.Document;
        var options = new ExportOpenApiViewModel(
            _dialogs,
            document.Name,
            _exportFolder ?? (ProjectPath is null ? string.Empty : Path.GetDirectoryName(ProjectPath) ?? string.Empty),
            SafeFileName(document.Name) + ".yaml",
            OpenApiExporter.DefaultTitle(document),
            OpenApiExporter.DefaultVersion(document));

        // Ask again, with the same entries, when the user does not want to replace an existing file.
        string path;
        while (true)
        {
            if (!await _dialogs.ShowDialogAsync(options))
            {
                return;
            }

            path = options.FullPath!;
            if (!File.Exists(path) || await _dialogs.ConfirmAsync(
                    Strings.Dialog_ExportOpenApi_Title,
                    Loc.Format(Strings.Message_ReplaceFile, Path.GetFileName(path), Path.GetDirectoryName(path))) == ConfirmResult.Yes)
            {
                break;
            }
        }

        _exportFolder = Path.GetDirectoryName(path);
        OpenApiExportResult result;
        try
        {
            result = OpenApiExporter.Export(document, options.Options);
            await File.WriteAllTextAsync(path, result.Text);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            await _dialogs.ShowErrorAsync(Strings.Dialog_ExportOpenApi_Title, ex.Message);
            return;
        }

        await _dialogs.ShowMessageAsync(
            Strings.Dialog_ExportOpenApi_Title,
            result.Warnings.Count == 0
                ? Loc.Format(Strings.Message_OpenApiExported, path)
                : Loc.Format(Strings.Message_OpenApiExportedWithNotes, path, Notes(result.Warnings)));
    }

    /// <summary>
    /// Adds the diagrams of a PlantUML file (one per @startuml) to the folder of the explorer selection, laid out
    /// automatically, then lists what could not be kept.
    /// </summary>
    [RelayCommand]
    private async Task ImportPlantUml()
    {
        if (Project is null ||
            await _dialogs.PickOpenFileAsync(Strings.Picker_ImportPlantUml, Strings.Picker_PlantUmlFiles, PlantUmlImporter.FilePatterns) is not { } path)
        {
            return;
        }

        PlantUmlImportResult result;
        try
        {
            result = PlantUmlImporter.ImportFile(path);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            await _dialogs.ShowErrorAsync(Strings.Picker_ImportPlantUml, Loc.Format(Strings.Message_InvalidPlantUml, ex.Message));
            return;
        }

        var added = new List<string>();
        foreach (var document in result.Documents)
        {
            if (!await AddDiagramAsync(document, Strings.Picker_ImportPlantUml))
            {
                return;
            }

            added.Add($"'{document.Name}'");
        }

        if (result.Warnings.Count > 0)
        {
            await _dialogs.ShowMessageAsync(
                Strings.Picker_ImportPlantUml,
                Loc.Format(Strings.Message_PlantUmlImportNotes, string.Join(", ", added), Notes(result.Warnings)));
        }
    }

    #region AI

    /// <summary>The AI service of Preferences → AI, or <c>null</c> while none is set up.</summary>
    private IAiProvider? CurrentAiProvider()
    {
        var settings = _settings.Load().Ai;
        return settings.IsEnabled ? _aiProviders(settings, AiKeys.Get(_secrets, settings)) : null;
    }

    /// <summary>Whether the AI chat panel is shown on the right (View → AI Chat); hidden at start-up, until asked for.</summary>
    [ObservableProperty]
    public partial bool IsAiChatVisible { get; set; }

    partial void OnIsAiChatVisibleChanged(bool value)
    {
        ActiveDiagram?.Chat?.Refresh();
    }

    /// <summary>Shows or hides the AI chat panel (the <see cref="Shortcuts.ToggleAiChat"/> shortcut).</summary>
    [RelayCommand]
    private void ToggleAiChat() => IsAiChatVisible = !IsAiChatVisible;

    /// <summary>
    /// True while a long task runs (an AI request, an import, arranging a diagram): the window shows what it is doing and
    /// how far it is, with Cancel.
    /// </summary>
    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    [ObservableProperty]
    public partial string? BusyText { get; set; }

    /// <summary>How far the long task is, from 0 to 1; <c>null</c> while that cannot be told, which shows a moving bar.</summary>
    [ObservableProperty]
    public partial double? BusyProgress { get; set; }

    /// <summary>Stops the long task that runs.</summary>
    [RelayCommand]
    private void CancelBusy() => _busy?.Cancel();

    /// <summary>How long a task that is usually quick (arranging, looking through a folder) may take before the busy overlay shows.</summary>
    internal static TimeSpan ShowBusyAfter { get; set; } = TimeSpan.FromMilliseconds(300);

    /// <summary>
    /// Runs a long task with the busy overlay: <paramref name="text"/>, a progress bar, and Cancel, which cancels the
    /// task's token and stops waiting for it with an <see cref="OperationCanceledException"/>. The task tells how far it is
    /// through its <see cref="IProgress{T}"/>. With <paramref name="showAfter"/>, the overlay only shows when the task
    /// takes longer, so that a quick one does not flash it.
    /// </summary>
    private async Task<T> RunBusyAsync<T>(
        string text, Func<IProgress<BusyStep>, CancellationToken, Task<T>> work, TimeSpan showAfter = default)
    {
        using var busy = new CancellationTokenSource();
        _busy = busy;
        BusyText = text;
        BusyProgress = null;

        // Reports come back on the UI thread; those that arrive after the task ended are dropped.
        var progress = new Progress<BusyStep>(step =>
        {
            if (_busy == busy)
            {
                BusyText = step.Text;
                BusyProgress = step.Progress;
            }
        });
        try
        {
            var task = work(progress, busy.Token);
            if (showAfter > TimeSpan.Zero)
            {
                await Task.WhenAny(task, Task.Delay(showAfter));
            }

            IsBusy = !task.IsCompleted;
            return await task.WaitAsync(busy.Token);
        }
        finally
        {
            IsBusy = false;
            BusyText = null;
            BusyProgress = null;
            _busy = null;
        }
    }

    /// <summary>
    /// Diagram → Create with AI: a diagram from a description, made by the AI service of Preferences → AI and added to the
    /// folder of the explorer's selection. Offers to set up a service when there is none.
    /// </summary>
    [RelayCommand]
    private async Task CreateDiagramWithAi()
    {
        if (Project is null)
        {
            return;
        }

        var settings = _settings.Load().Ai;
        if (!settings.IsEnabled || _aiProviders(settings, AiKeys.Get(_secrets, settings)) is not { } provider)
        {
            if (await _dialogs.ConfirmAsync(Strings.Dialog_AiDiagram_Title, Strings.Message_AiNotSetUp) == ConfirmResult.Yes)
            {
                await Preferences();
            }

            return;
        }

        var dialog = new AiDiagramViewModel(provider.DisplayName);
        if (!await _dialogs.ShowDialogAsync(dialog))
        {
            return;
        }

        AiDiagramResult result;
        try
        {
            result = await RunBusyAsync(
                Loc.Format(Strings.Message_AiWorking, provider.DisplayName),
                (_, token) => new AiDiagramGenerator(provider).GenerateAsync(dialog.Description, dialog.SelectedType.Type, token));
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (AiException ex)
        {
            await _dialogs.ShowErrorAsync(Strings.Dialog_AiDiagram_Title, ex.Message);
            return;
        }

        var document = result.Document;
        document.Author = Project.Author;
        document.Description = Loc.Format(Strings.Message_AiDiagramDescription, provider.DisplayName, dialog.Description.Trim());
        if (await AddDiagramAsync(document, Strings.Dialog_AiDiagram_Title) && result.Warnings.Count > 0)
        {
            await _dialogs.ShowMessageAsync(
                Strings.Dialog_AiDiagram_Title,
                Loc.Format(Strings.Message_PlantUmlImportNotes, $"'{document.Name}'", Notes(result.Warnings)));
        }
    }

    #endregion

    /// <summary>
    /// Writes the diagram selected in the explorer, of any type, as PlantUML; an open diagram is exported as shown,
    /// including unsaved changes.
    /// </summary>
    [RelayCommand(CanExecute = nameof(IsDiagramSelected))]
    private async Task ExportPlantUml()
    {
        if (SelectedDiagramItem is not { } item)
        {
            return;
        }

        var document = OpenDiagrams.FirstOrDefault(d => d.Id == item.Id)?.Document ?? item.Document;
        if (await _dialogs.PickSaveFileAsync(Strings.Picker_ExportPlantUml, SafeFileName(document.Name) + ".puml", ".puml",
                Strings.Picker_PlantUmlFiles, ["*.puml"]) is not { } path)
        {
            return;
        }

        var result = PlantUmlExporter.Export(document);
        try
        {
            await File.WriteAllTextAsync(path, result.Text);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            await _dialogs.ShowErrorAsync(Strings.Picker_ExportPlantUml, ex.Message);
            return;
        }

        await _dialogs.ShowMessageAsync(
            Strings.Picker_ExportPlantUml,
            result.Warnings.Count == 0
                ? Loc.Format(Strings.Message_PlantUmlExported, path)
                : Loc.Format(Strings.Message_PlantUmlExportedWithNotes, path, Notes(result.Warnings)));
    }

    /// <summary>A diagram is selected in the explorer.</summary>
    private bool IsDiagramSelected => SelectedDiagramItem is not null;

    /// <summary>Notes as a list, one per line; long lists end with how many more there are.</summary>
    private static string Notes(IReadOnlyList<string> notes)
    {
        const int Shown = 12;
        var lines = notes.Take(Shown).Select(n => "• " + n);
        return string.Join('\n', notes.Count > Shown ? lines.Append($"… (+{notes.Count - Shown})") : lines);
    }

    /// <summary>Code can be generated from the class diagram selected in the explorer.</summary>
    private bool CanGenerateCode => SelectedDiagramItem?.Document.DiagramType == DiagramType.Class;

    /// <summary>Writes a generated file; tests replace it to see what the window shows while files are written.</summary>
    internal Func<string, string, CancellationToken, Task> WriteFileAsync { get; set; } = File.WriteAllTextAsync;

    /// <summary>
    /// Generates source code for the class diagram selected in the explorer, whether or not it is open; an open diagram
    /// is generated as shown, including unsaved changes. Existing files are only replaced after confirming.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanGenerateCode))]
    private async Task GenerateCode()
    {
        if (SelectedDiagramItem is not { } item)
        {
            return;
        }

        var document = OpenDiagrams.FirstOrDefault(d => d.Id == item.Id)?.Document ?? item.Document;
        if (document.Model is not ClassDiagramModel diagram)
        {
            return;
        }

        if (_codeGenerators.Generators.Count == 0)
        {
            var message = Loc.Format(Strings.Message_NoCodeGenerators, string.Join(Environment.NewLine, InstalledPlugins.Folders));
            await _dialogs.ShowErrorAsync(Strings.Dialog_GenerateCode_Title,
                string.Join(Environment.NewLine + Environment.NewLine, _codeGenerators.Problems.Prepend(message)));
            return;
        }

        var options = new GenerateCodeViewModel(
            _dialogs,
            diagram,
            document.Name,
            ProjectPath is null ? string.Empty : Path.GetDirectoryName(ProjectPath) ?? string.Empty,
            Project?.Name ?? string.Empty,
            _codeGenerators,
            _codeLanguage,
            _codeFolder,
            _codeNamespace);

        // Ask again, with the same choices, when the user does not want to replace existing files.
        while (true)
        {
            if (!await _dialogs.ShowDialogAsync(options))
            {
                return;
            }

            if (options.ExistingFileCount == 0 || await _dialogs.ConfirmAsync(
                    Strings.Dialog_GenerateCode_Title,
                    Loc.Format(Strings.Message_ReplaceGeneratedFiles, options.ExistingFileCount, options.FullOutputFolder)) == ConfirmResult.Yes)
            {
                break;
            }
        }

        // Offer the same choices next time; a suggested folder is suggested again for the language then chosen.
        _codeLanguage = options.Language.Id;
        _codeFolder = string.Equals(options.FullOutputFolder, options.DefaultFolder(options.Language), StringComparison.OrdinalIgnoreCase)
            ? null
            : options.FullOutputFolder;
        _codeNamespace = options.Namespace.Trim();

        // A large diagram makes hundreds of files: they are written on another thread, showing how far it is.
        var files = options.Result.Files.Select(f => (Path: options.PathOf(f), f.Content)).ToList();
        try
        {
            await RunBusyAsync(
                Loc.Format(Strings.Message_WritingFiles, 0, files.Count),
                (progress, token) => Task.Run(async () =>
                {
                    for (var i = 0; i < files.Count; i++)
                    {
                        token.ThrowIfCancellationRequested();
                        Directory.CreateDirectory(Path.GetDirectoryName(files[i].Path)!);
                        await WriteFileAsync(files[i].Path, files[i].Content, token);
                        if ((i + 1) * 100 / files.Count != i * 100 / files.Count)
                        {
                            progress.Report(new BusyStep(Loc.Format(Strings.Message_WritingFiles, i + 1, files.Count), (double)(i + 1) / files.Count));
                        }
                    }

                    return true;
                }, token),
                ShowBusyAfter);
        }
        catch (OperationCanceledException)
        {
            await _dialogs.ShowMessageAsync(Strings.Dialog_GenerateCode_Title, Loc.Format(Strings.Message_CodeGenerationCancelled, options.FullOutputFolder));
            return;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            await _dialogs.ShowErrorAsync(Strings.Dialog_GenerateCode_Title, ex.Message);
            return;
        }

        await _dialogs.ShowMessageAsync(
            Strings.Dialog_GenerateCode_Title,
            Loc.Format(Strings.Message_CodeGenerated, options.Result.Files.Count, options.FullOutputFolder));
    }

    partial void OnActiveDiagramChanged(DiagramViewModel? oldValue, DiagramViewModel? newValue)
    {
        if (oldValue is not null)
        {
            oldValue.PropertyChanged -= OnActiveDiagramPropertyChanged;
        }

        if (newValue is not null)
        {
            newValue.PropertyChanged += OnActiveDiagramPropertyChanged;

            // The clipboard may have changed while another diagram or application was active.
            _ = newValue.RefreshCanPasteAsync();

            // Keep the explorer on the diagram of the active tab.
            if (AllNodes().FirstOrDefault(n => n.Id == newValue.Id) is { } node && SelectedNode != node)
            {
                Reveal(node);
                SelectedNode = node;
            }
        }

        OnEditStateChanged();
    }

    /// <summary>
    /// Selecting a diagram in the explorer brings its tab to the front when it is open. Selecting one that is not open
    /// (it is not opened) leaves no tab active, so the tabs never show a different diagram than the explorer.
    /// </summary>
    partial void OnSelectedNodeChanged(ProjectNodeViewModel? value)
    {
        GenerateCodeCommand.NotifyCanExecuteChanged();
        ExportOpenApiCommand.NotifyCanExecuteChanged();
        ExportPlantUmlCommand.NotifyCanExecuteChanged();
        if (value is DiagramItemViewModel item)
        {
            ActiveDiagram = OpenDiagrams.FirstOrDefault(d => d.Id == item.Id);
        }
    }

    private void OnActiveDiagramPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(DiagramViewModel.CanEditFont) or nameof(DiagramViewModel.CanFormatShape) or nameof(DiagramViewModel.CanFormatLine)
            or nameof(DiagramViewModel.HasSelection)
            or nameof(DiagramViewModel.CanUndo) or nameof(DiagramViewModel.CanRedo) or nameof(DiagramViewModel.CanPaste))
        {
            OnEditStateChanged();
        }
    }

    /// <summary>The active diagram, its selection, its undo history or the clipboard changed.</summary>
    private void OnEditStateChanged()
    {
        OnPropertyChanged(nameof(CanEditFont));
        EditFontCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(CanFormatShape));
        FormatShapeCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(CanFormatLine));
        FormatLineCommand.NotifyCanExecuteChanged();
        UndoCommand.NotifyCanExecuteChanged();
        RedoCommand.NotifyCanExecuteChanged();
        CutCommand.NotifyCanExecuteChanged();
        CopyCommand.NotifyCanExecuteChanged();
        PasteCommand.NotifyCanExecuteChanged();
        DeleteCommand.NotifyCanExecuteChanged();
        EditCommand.NotifyCanExecuteChanged();
        BringToFrontCommand.NotifyCanExecuteChanged();
        BringForwardCommand.NotifyCanExecuteChanged();
        SendBackwardCommand.NotifyCanExecuteChanged();
        SendToBackCommand.NotifyCanExecuteChanged();
        ArrangeCommand.NotifyCanExecuteChanged();
    }

    /// <summary>True when an open diagram has unsaved changes.</summary>
    public bool HasUnsavedChanges => OpenDiagrams.Any(d => d.IsDirty);

    private void OnOpenDiagramPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(DiagramViewModel.IsDirty))
        {
            OnSaveStateChanged();
        }
    }

    /// <summary>Enables Save Project while there is something to save.</summary>
    private void OnSaveStateChanged()
    {
        OnPropertyChanged(nameof(HasUnsavedChanges));
        SaveProjectCommand.NotifyCanExecuteChanged();
    }

    /// <summary>Moves the tab of <paramref name="diagram"/> to <paramref name="index"/> among the open tabs; it stays the active tab.</summary>
    public void MoveTab(DiagramViewModel diagram, int index)
    {
        var from = OpenDiagrams.IndexOf(diagram);
        index = Math.Clamp(index, 0, OpenDiagrams.Count - 1);
        if (from < 0 || from == index)
        {
            return;
        }

        OpenDiagrams.Move(from, index);
        ActiveDiagram = diagram;
    }

    private DiagramViewModel Activate(DiagramViewModel diagram)
    {
        OpenDiagrams.Add(diagram);
        ActiveDiagram = diagram;
        return diagram;
    }

    /// <summary>Opens an editor on a copy of the saved diagram, so unsaved edits never reach the project.</summary>
    private DiagramViewModel CreateDiagramViewModel(DiagramItemViewModel item) =>
        WithChat(new(DiagramSerializer.Clone(item.Document), _dialogs, _clipboard, CloseDiagramAsync)
        {
            Defaults = _settings.Load().StyleFor(item.Document.DiagramType),
            RunBusy = (text, work) => RunBusyAsync(text, async (_, token) => { await work(token); return true; }, ShowBusyAfter),
        });

    private DiagramViewModel WithChat(DiagramViewModel diagram)
    {
        diagram.Chat = new AiChatViewModel(diagram, new AiChatServices(
            CurrentAiProvider,
            () => _settings.Load().Ai.ChangeMode,
            Preferences,
            () => Project?.Name ?? string.Empty,
            IsAiAllowed,
            AllowAi));
        return diagram;
    }

    /// <summary>The projects allowed in this session that have no file yet; saved projects are remembered in the settings.</summary>
    private bool _aiAllowedUnsaved;

    /// <summary>Whether the user allowed the AI chat to send the open project's diagrams.</summary>
    private bool IsAiAllowed() => ProjectPath is null
        ? _aiAllowedUnsaved
        : _settings.Load().AiAllowedProjects.Contains(ProjectPath, StringComparer.OrdinalIgnoreCase);

    private void AllowAi()
    {
        if (ProjectPath is null)
        {
            _aiAllowedUnsaved = true;
        }
        else if (!IsAiAllowed())
        {
            SaveSettings(s => s with { AiAllowedProjects = [.. s.AiAllowedProjects, ProjectPath] });
        }
    }

    #endregion

    #region Edit commands forwarded to the active diagram

    // Each command is enabled when the active diagram's command is; see OnActiveDiagramPropertyChanged.

    [RelayCommand(CanExecute = nameof(CanUndo))]
    private void Undo() => ActiveDiagram?.UndoCommand.Execute(null);

    [RelayCommand(CanExecute = nameof(CanRedo))]
    private void Redo() => ActiveDiagram?.RedoCommand.Execute(null);

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void Cut() => ActiveDiagram?.CutCommand.Execute(null);

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void Copy() => ActiveDiagram?.CopyCommand.Execute(null);

    [RelayCommand(CanExecute = nameof(CanPaste))]
    private void Paste() => ActiveDiagram?.PasteCommand.Execute(null);

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void Delete() => ActiveDiagram?.DeleteCommand.Execute(null);

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void Edit() => ActiveDiagram?.EditCommand.Execute(null);

    private bool CanUndo => ActiveDiagram?.CanUndo == true;

    private bool CanRedo => ActiveDiagram?.CanRedo == true;

    private bool HasSelection => ActiveDiagram?.HasSelection == true;

    private bool CanPaste => ActiveDiagram?.CanPaste == true;

    /// <summary>True when the active diagram has a selected element whose font can be edited.</summary>
    public bool CanEditFont => ActiveDiagram?.CanEditFont == true;

    [RelayCommand(CanExecute = nameof(CanEditFont))]
    private void EditFont() => ActiveDiagram?.EditFontCommand.Execute(null);

    [RelayCommand]
    private void EditDiagramFont() => ActiveDiagram?.EditDiagramFontCommand.Execute(null);

    /// <summary>True when the active diagram has a selected element drawn as a shape (not a line).</summary>
    public bool CanFormatShape => ActiveDiagram?.CanFormatShape == true;

    [RelayCommand(CanExecute = nameof(CanFormatShape))]
    private void FormatShape() => ActiveDiagram?.FormatShapeCommand.Execute(null);

    /// <summary>True when the active diagram has a selected connector (relationship, message or edge).</summary>
    public bool CanFormatLine => ActiveDiagram?.CanFormatLine == true;

    [RelayCommand(CanExecute = nameof(CanFormatLine))]
    private void FormatLine() => ActiveDiagram?.FormatLineCommand.Execute(null);

    [RelayCommand(CanExecute = nameof(CanBringToFront))]
    private void BringToFront() => ActiveDiagram?.BringToFrontCommand.Execute(null);

    [RelayCommand(CanExecute = nameof(CanBringForward))]
    private void BringForward() => ActiveDiagram?.BringForwardCommand.Execute(null);

    [RelayCommand(CanExecute = nameof(CanSendBackward))]
    private void SendBackward() => ActiveDiagram?.SendBackwardCommand.Execute(null);

    [RelayCommand(CanExecute = nameof(CanSendToBack))]
    private void SendToBack() => ActiveDiagram?.SendToBackCommand.Execute(null);

    private bool CanBringToFront => ActiveDiagram?.BringToFrontCommand.CanExecute(null) == true;

    private bool CanBringForward => ActiveDiagram?.BringForwardCommand.CanExecute(null) == true;

    private bool CanSendBackward => ActiveDiagram?.SendBackwardCommand.CanExecute(null) == true;

    private bool CanSendToBack => ActiveDiagram?.SendToBackCommand.CanExecute(null) == true;

    [RelayCommand(CanExecute = nameof(CanArrange))]
    private Task Arrange() => ActiveDiagram?.ArrangeCommand.ExecuteAsync(null) ?? Task.CompletedTask;

    private bool CanArrange => ActiveDiagram?.ArrangeCommand.CanExecute(null) == true;

    #endregion

    [RelayCommand]
    private void Exit() => ExitRequested?.Invoke(this, EventArgs.Empty);

    #region Help

    /// <summary>The folder of the help pages installed with the application.</summary>
    public static string HelpFolder { get; } = Path.Combine(AppContext.BaseDirectory, "Help");

    [RelayCommand]
    private Task UserManual() => OpenHelpPageAsync("user-manual", Strings.Help_UserManual);

    [RelayCommand]
    private Task LearnUml() => OpenHelpPageAsync("learn-uml", Strings.Help_LearnUml);

    [RelayCommand]
    private Task OpenApiHowTo() => OpenHelpPageAsync("openapi-how-to", Strings.Help_OpenApiHowTo);

    [RelayCommand]
    private Task PlantUmlHowTo() => OpenHelpPageAsync("plantuml-how-to", Strings.Help_PlantUmlHowTo);

    [RelayCommand]
    private Task GeneratingCodeHowTo() => OpenHelpPageAsync("generating-code-how-to", Strings.Help_GeneratingCodeHowTo);

    /// <summary>
    /// The file of a help page in a language: <c>&lt;page&gt;-&lt;language&gt;.html</c> in <see cref="HelpFolder"/>, or
    /// the English one, <c>&lt;page&gt;-en.html</c>, when the page has not been translated into that language.
    /// </summary>
    public static string HelpPagePath(string page, string language)
    {
        var translated = Path.Combine(HelpFolder, $"{page}-{language}.html");
        return File.Exists(translated) ? translated : Path.Combine(HelpFolder, $"{page}-en.html");
    }

    /// <summary>
    /// Opens a help page in the display language (see <see cref="HelpPagePath"/>) in the browser; shows an error when
    /// it is missing or cannot be opened.
    /// </summary>
    private async Task OpenHelpPageAsync(string page, string title)
    {
        var path = HelpPagePath(page, Loc.DisplayLanguage);
        if (!File.Exists(path) || !await _dialogs.OpenFileAsync(path))
        {
            await _dialogs.ShowErrorAsync(title, Loc.Format(Strings.Message_HelpPageNotOpened, path));
        }
    }

    [RelayCommand]
    private Task About() => _dialogs.ShowAboutAsync(new AboutViewModel(_clipboard, _codeGenerators, _codeImporters));

    #endregion

    /// <summary>Characters no file name may have on some platform: Windows forbids all of them, macOS and Linux the slash.</summary>
    private static readonly char[] NonPortableFileNameChars = ['<', '>', ':', '"', '/', '\\', '|', '?', '*'];

    /// <summary>Names Windows reserves for devices, with or without an extension.</summary>
    private static readonly HashSet<string> ReservedFileNames = new(
        ["CON", "PRN", "AUX", "NUL", .. Enumerable.Range(1, 9).SelectMany(i => new[] { $"COM{i}", $"LPT{i}" })],
        StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// A file name made from a project or diagram name that works on Windows, macOS and Linux alike, whichever one
    /// creates it, so that projects can be moved between them: characters a platform forbids and control characters
    /// become <c>_</c>, trailing dots and spaces (which Windows drops) are removed, and device names get a <c>_</c>.
    /// </summary>
    internal static string SafeFileName(string name)
    {
        var safe = new string(name.Select(c => c < ' ' || NonPortableFileNameChars.Contains(c) ? '_' : c).ToArray()).Trim().TrimEnd('.', ' ');
        if (safe.Length == 0)
        {
            return Strings.App_Untitled;
        }

        var stem = safe.Split('.')[0];
        return ReservedFileNames.Contains(stem) ? "_" + safe : safe;
    }
}
