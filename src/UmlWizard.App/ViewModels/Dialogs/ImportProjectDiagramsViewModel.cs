using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Material.Icons;
using UmlWizard.App.Resources;
using UmlWizard.App.Services;
using UmlWizard.Core.Model;
using UmlWizard.Core.Serialization;

namespace UmlWizard.App.ViewModels.Dialogs;

public sealed partial class ImportProjectDiagramsViewModel : DialogViewModel
{
    private readonly IDialogService _dialogs;

    public override string Title => Strings.Dialog_ImportProjectDiagrams_Title;

    [ObservableProperty]
    public partial string SourcePath { get; set; } = string.Empty;

    public ObservableCollection<ImportProjectDiagramEntryViewModel> Diagrams { get; } = [];

    public IEnumerable<DiagramDocument> SelectedDiagrams =>
        Diagrams.Where(d => d.IsSelected).Select(d => d.Document);

    public ImportProjectDiagramsViewModel(IDialogService dialogs, string sourcePath, IEnumerable<DiagramDocument> diagrams)
    {
        _dialogs = dialogs;
        LoadDiagrams(sourcePath, diagrams);
    }

    public void LoadDiagrams(string sourcePath, IEnumerable<DiagramDocument> diagrams)
    {
        SourcePath = sourcePath;
        Diagrams.Clear();
        foreach (var diagram in diagrams)
        {
            Diagrams.Add(new ImportProjectDiagramEntryViewModel(diagram));
        }
    }

    [RelayCommand]
    private async Task BrowseSource()
    {
        var path = await _dialogs.PickOpenFileAsync(Strings.Picker_ImportProject, Strings.Picker_ProjectFiles, UmlProject.FilePatterns);
        if (path is null)
        {
            return;
        }

        try
        {
            var project = ProjectSerializer.Load(path);
            LoadDiagrams(path, project.Diagrams);
            ErrorMessage = null;
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            ErrorMessage = Loc.Format(Strings.Message_ProjectNotOpened, ex.Message);
        }
    }

    [RelayCommand]
    private void SelectAll()
    {
        foreach (var diagram in Diagrams)
        {
            diagram.IsSelected = true;
        }
    }

    [RelayCommand]
    private void DeselectAll()
    {
        foreach (var diagram in Diagrams)
        {
            diagram.IsSelected = false;
        }
    }

    protected override string? Validate()
    {
        if (string.IsNullOrWhiteSpace(SourcePath) || Diagrams.Count == 0)
        {
            return Strings.Dialog_ImportProjectDiagrams_NoDiagrams;
        }

        if (!Diagrams.Any(d => d.IsSelected))
        {
            return Strings.Dialog_ImportProjectDiagrams_NoneSelected;
        }

        return null;
    }
}

public sealed partial class ImportProjectDiagramEntryViewModel : ObservableObject
{
    public DiagramDocument Document { get; }

    public string Name => Document.Name;

    public DiagramType DiagramType => Document.DiagramType;

    public string TypeName => DiagramTypeInfo.For(Document.DiagramType).DisplayName;

    public MaterialIconKind Icon => DiagramTypeInfo.For(Document.DiagramType).Icon;

    [ObservableProperty]
    public partial bool IsSelected { get; set; } = true;

    public ImportProjectDiagramEntryViewModel(DiagramDocument document)
    {
        Document = document;
    }
}
