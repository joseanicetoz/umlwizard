using System.Collections.ObjectModel;
using System.Text.RegularExpressions;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using UmlWizard.App.Resources;
using UmlWizard.App.Services;
using UmlWizard.CodeGeneration;
using UmlWizard.CodeGeneration.Plugins;
using UmlWizard.Core.Model;

namespace UmlWizard.App.ViewModels.Dialogs;

/// <summary>
/// The language, namespace and output folder for generating code from a class diagram. The files and the generator's
/// notes are shown as they would be written, and follow every change.
/// </summary>
public sealed partial class GenerateCodeViewModel : DialogViewModel
{
    private readonly IDialogService _dialogs;
    private readonly ClassDiagramModel _diagram;
    private readonly string _diagramName;
    private readonly string _projectFolder;
    private readonly string _projectName;
    private readonly CodeGeneratorCatalog _generators;

    /// <param name="projectFolder">The default output folders are in its <c>Generated</c> folder.</param>
    /// <param name="projectName">Suggests the namespace.</param>
    /// <param name="outputFolder">The folder used last time, or <c>null</c> for the language's default folder.</param>
    /// <param name="generators">The languages offered; must not be empty. Their loading problems are shown as notes.</param>
    /// <param name="languageId">The language used last time; the first language when it is not offered.</param>
    /// <param name="ns">The namespace used last time, or <c>null</c> for one made from the project name.</param>
    public GenerateCodeViewModel(
        IDialogService dialogs, ClassDiagramModel diagram, string diagramName, string projectFolder, string projectName,
        CodeGeneratorCatalog generators, string? languageId, string? outputFolder, string? ns)
    {
        _dialogs = dialogs;
        _diagram = diagram;
        _diagramName = diagramName;
        _projectFolder = projectFolder;
        _projectName = projectName;
        _generators = generators;
        _language = generators.Find(languageId) ?? generators.Generators[0];
        OutputFolder = outputFolder ?? DefaultFolder(_language);
        Namespace = ns ?? DefaultNamespace(_language);
    }

    public override string Title => Strings.Dialog_GenerateCode_Title;

    public string Prompt => Loc.Format(Strings.Dialog_GenerateCode_Prompt, _diagramName);

    /// <summary>The languages of the installed code generator plug-ins.</summary>
    public IReadOnlyList<ICodeGenerator> Languages => _generators.Generators;

    private ICodeGenerator _language;

    /// <summary>
    /// The target language. A folder or namespace that is still the previous language's suggestion follows the change.
    /// </summary>
    public ICodeGenerator Language
    {
        get => _language;
        set
        {
            var previous = _language;
            if (!SetProperty(ref _language, value))
            {
                return;
            }

            if (SamePath(OutputFolder, DefaultFolder(previous)))
            {
                OutputFolder = DefaultFolder(value);
            }

            if (Namespace == DefaultNamespace(previous))
            {
                Namespace = DefaultNamespace(value);
            }

            OnPropertyChanged(nameof(NamespaceLabel));
            Refresh();
        }
    }

    /// <summary>What the language calls a namespace.</summary>
    public string NamespaceLabel => Language.NamespaceKind switch
    {
        NamespaceKind.Package => Strings.Dialog_GenerateCode_Package,
        NamespaceKind.Module => Strings.Dialog_GenerateCode_Module,
        _ => Strings.Dialog_GenerateCode_Namespace,
    };

    [ObservableProperty]
    public partial string Namespace { get; set; }

    [ObservableProperty]
    public partial string OutputFolder { get; set; }

    public ObservableCollection<GeneratedFileItem> Files { get; } = [];

    public ObservableCollection<string> Warnings { get; } = [];

    public bool HasWarnings => Warnings.Count > 0;

    /// <summary>How many files will be written, and how many of them replace existing ones.</summary>
    public string FileSummary
    {
        get
        {
            var existing = Files.Count(f => f.Exists);
            return existing == 0
                ? Loc.Format(Strings.Dialog_GenerateCode_FileCount, Files.Count)
                : Loc.Format(Strings.Dialog_GenerateCode_ExistingCount, Files.Count, existing);
        }
    }

    /// <summary>The files as last generated for the current choices.</summary>
    public CodeGenerationResult Result { get; private set; } = new([], []);

    /// <summary>The language and namespace <see cref="Result"/> was generated for; the folder does not change the code.</summary>
    private (ICodeGenerator Language, string Namespace)? _generatedFor;

    /// <summary>The output folder as a full path, or <c>null</c> while it is empty or not a valid full path.</summary>
    public string? FullOutputFolder
    {
        get
        {
            var folder = (OutputFolder ?? string.Empty).Trim();
            if (folder.Length == 0 || !Path.IsPathFullyQualified(folder))
            {
                return null;
            }

            try
            {
                return Path.GetFullPath(folder);
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
            {
                return null;
            }
        }
    }

    public int ExistingFileCount => Files.Count(f => f.Exists);

    /// <summary>Where a generated file goes.</summary>
    public string PathOf(GeneratedFile file) => Path.Combine(FullOutputFolder!, file.Path.Replace('/', Path.DirectorySeparatorChar));

    [GeneratedRegex(@"^\s*([A-Za-z_][A-Za-z0-9_]*)((\.|::)[A-Za-z_][A-Za-z0-9_]*)*\s*$")]
    private static partial Regex NamespacePattern();

    partial void OnNamespaceChanged(string value) => Refresh();

    partial void OnOutputFolderChanged(string value) => Refresh();

    /// <summary>
    /// Lists the files for the current choices, which of them exist, and the generator's notes. The code is only generated
    /// again when the language or namespace changed: for a large diagram that takes a moment.
    /// </summary>
    private void Refresh()
    {
        // Called by the property setters in the constructor before every field is set.
        if (_diagram is null || Namespace is null || OutputFolder is null)
        {
            return;
        }

        var choices = (Language, Namespace.Trim());
        if (_generatedFor != choices)
        {
            try
            {
                Result = CodeGenerator.Generate(_diagram, Language, new CodeGenerationOptions(choices.Item2, _diagramName));
            }
            catch (Exception ex)
            {
                // A plug-in is not trusted to be free of bugs: report it instead of taking the application down.
                Result = new([], [Loc.Format(Strings.Message_CodeGeneratorFailed, Language.DisplayName, ex.Message)]);
            }

            _generatedFor = choices;
        }

        var folder = FullOutputFolder;
        Files.Clear();
        foreach (var file in Result.Files)
        {
            Files.Add(new GeneratedFileItem(file.Path, folder is not null && File.Exists(PathOf(file))));
        }

        Warnings.Clear();
        foreach (var warning in _generators.Problems.Concat(Result.Warnings))
        {
            Warnings.Add(warning);
        }

        OnPropertyChanged(nameof(HasWarnings));
        OnPropertyChanged(nameof(FileSummary));
    }

    [RelayCommand]
    private async Task BrowseFolder()
    {
        if (await _dialogs.PickFolderAsync(Strings.Picker_OutputFolder) is { } folder)
        {
            OutputFolder = folder;
        }
    }

    protected override string? Validate()
    {
        Refresh();
        if (string.IsNullOrWhiteSpace(OutputFolder))
        {
            return Strings.Dialog_GenerateCode_NeedsFolder;
        }

        if (FullOutputFolder is null)
        {
            return Strings.Dialog_GenerateCode_InvalidFolder;
        }

        if (!string.IsNullOrWhiteSpace(Namespace) && !NamespacePattern().IsMatch(Namespace))
        {
            return Strings.Dialog_GenerateCode_InvalidNamespace;
        }

        return Result.Files.Count == 0 ? Strings.Dialog_GenerateCode_NoClasses : null;
    }

    /// <summary>The suggested folder: <c>Generated/&lt;language Id&gt;</c> next to the project file.</summary>
    public string DefaultFolder(ICodeGenerator language) =>
        _projectFolder.Length == 0 ? string.Empty : Path.Combine(_projectFolder, "Generated", language.Id);

    /// <summary>The suggested namespace: the project name as an identifier, as the language suggests it (lower case for Java).</summary>
    public string DefaultNamespace(ICodeGenerator language)
    {
        var name = new string(_projectName.Where(c => char.IsLetterOrDigit(c) || c == '_').ToArray());
        if (name.Length > 0 && char.IsDigit(name[0]))
        {
            name = "_" + name;
        }

        return language.DefaultNamespace(name);
    }

    private static bool SamePath(string? a, string b) =>
        string.Equals((a ?? string.Empty).Trim().TrimEnd('\\', '/'), b.TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase);
}
