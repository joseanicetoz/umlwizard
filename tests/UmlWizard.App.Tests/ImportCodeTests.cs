using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using UmlWizard.App.ViewModels;
using UmlWizard.App.ViewModels.Dialogs;
using UmlWizard.App.Views;
using UmlWizard.Core.Model;
using UmlWizard.Core.Serialization;
using UmlWizard.App.Services;

namespace UmlWizard.App.Tests;

public sealed class ImportCodeTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), $"umlwizard-importcode-{Guid.NewGuid():N}");

    public ImportCodeTests()
    {
        Directory.CreateDirectory(Path.Combine(_folder, "Code", "Shop"));
        Directory.CreateDirectory(Path.Combine(_folder, "Projects"));
        File.WriteAllText(Path.Combine(_folder, "Code", "Shop", "Order.cs"),
            "namespace Shop; public class Order { public List<OrderLine> Lines { get; } = []; public event EventHandler? Changed; }");
        File.WriteAllText(Path.Combine(_folder, "Code", "Shop", "OrderLine.cs"), "namespace Shop; public class OrderLine { public int Quantity { get; set; } }");
        File.WriteAllText(Path.Combine(_folder, "Code", "Shop", "App.xaml"), "<Application />");
    }

    /// <summary>A folder of C# code: two classes and a file that is not C#.</summary>
    private string Shop => Path.Combine(_folder, "Code", "Shop");

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    /// <summary>Imports C# while parsing waits for the test, to see what the window shows meanwhile.</summary>
    private sealed class WaitingImporter(Task wait) : CodeImport.ICodeImporter
    {
        private readonly CodeImport.ICodeImporter _csharp = InstalledPlugins.CodeImporters.Importers.Single(i => i.Id == "CSharp");

        public string Id => _csharp.Id;

        public string DisplayName => _csharp.DisplayName;

        public IReadOnlyList<string> FileExtensions => _csharp.FileExtensions;

        public CodeImport.CodeImportParseResult Parse(IReadOnlyList<CodeImport.SourceFile> files)
        {
            wait.Wait();
            return _csharp.Parse(files);
        }
    }

    private MainWindowViewModel ImportingMain(FakeServices services, Task parsing)
    {
        services.FolderPath = Shop;
        services.AcceptDialogs = true;
        services.FillDialog = d => ((ImportCodeViewModel)d).Location = Path.Combine(_folder, "Projects");
        return new MainWindowViewModel(services, services, codeImporters: new CodeImport.Plugins.CodeImporterCatalog([new WaitingImporter(parsing)]));
    }

    /// <summary>Runs the UI's jobs until <paramref name="done"/> holds, as the import works on another thread.</summary>
    private static void RunJobsUntil(Func<bool> done)
    {
        var until = DateTime.UtcNow.AddSeconds(10);
        while (!done() && DateTime.UtcNow < until)
        {
            Dispatcher.UIThread.RunJobs();
            Thread.Sleep(5);
        }

        Assert.True(done());
    }

    [AvaloniaFact]
    public async Task ImportingAFolder_ShowsWhatItIsDoing_UntilTheProjectIsCreated()
    {
        var services = new FakeServices();
        var parse = new TaskCompletionSource();
        var main = ImportingMain(services, parse.Task);
        var window = new MainWindow { DataContext = main };
        window.Show();

        var import = main.ImportFolderCommand.ExecuteAsync(null);
        RunJobsUntil(() => main.BusyText == "Reading the declarations in 2 C# files…");

        Assert.True(main.IsBusy);
        Assert.True(window.FindControl<Border>("BusyOverlay")!.IsVisible);
        var bar = window.FindControl<ProgressBar>("BusyProgressBar")!;
        Assert.True(bar.IsIndeterminate);

        parse.SetResult();
        await import;
        Assert.False(main.IsBusy);
        Assert.Null(main.BusyText);
        Assert.Equal("Shop", main.ActiveDiagram?.Name);
        window.Close();
    }

    [AvaloniaFact]
    public async Task CancellingAnImport_CreatesNoProject()
    {
        var services = new FakeServices();
        var parse = new TaskCompletionSource();
        var main = ImportingMain(services, parse.Task);

        var import = main.ImportFolderCommand.ExecuteAsync(null);
        RunJobsUntil(() => main.BusyText == "Reading the declarations in 2 C# files…");
        main.CancelBusyCommand.Execute(null);
        await import;
        parse.SetResult();

        Assert.False(main.IsBusy);
        Assert.Null(main.Project);
        Assert.False(Directory.Exists(Path.Combine(_folder, "Projects", "Shop")));
        Assert.Empty(services.Messages);
    }

    [AvaloniaFact]
    public void TheBusyBar_ShowsTheShareDone_WhenItIsKnown()
    {
        var services = new FakeServices();
        var main = new MainWindowViewModel(services, services) { IsBusy = true, BusyText = "Reading 4 of 10 source files…", BusyProgress = 0.4 };
        var window = new MainWindow { DataContext = main };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        var bar = window.FindControl<ProgressBar>("BusyProgressBar")!;

        Assert.False(bar.IsIndeterminate);
        Assert.True(bar.ShowProgressText);
        Assert.Equal(0.4, bar.Value);

        main.BusyProgress = null;
        Dispatcher.UIThread.RunJobs();
        Assert.True(bar.IsIndeterminate);
        Assert.False(bar.ShowProgressText);
        window.Close();
    }

    [AvaloniaFact]
    public void ImportFolderCard_IsOnTheStartScreen()
    {
        var services = new FakeServices();
        var main = new MainWindowViewModel(services, services);
        var window = new MainWindow { DataContext = main };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var card = window.GetLogicalDescendants().OfType<Button>().Single(b => b.Name == "ImportFolderCard");
        Assert.Contains(card.GetLogicalDescendants().OfType<TextBlock>(), t => t.Text == "Import Source Folder");
        Assert.True(card.IsEffectivelyVisible);
        Assert.True(card.IsEffectivelyEnabled);
        Assert.Same(main.ImportFolderCommand, card.Command);
        window.Close();
    }

    [AvaloniaFact]
    public async Task ImportFolder_OfCSharpCode_CreatesAProjectWithAClassDiagram_AndListsWhatWasLeftOut()
    {
        var services = new FakeServices { FolderPath = Shop, AcceptDialogs = true };
        services.FillDialog = d =>
        {
            var import = Assert.IsType<ImportCodeViewModel>(d);
            Assert.Equal("Shop", import.Name);
            Assert.Equal(["C# (2 files)"], import.Languages.Select(l => l.Label));
            Assert.Equal("2 C# files", import.FilesLine);
            import.Location = Path.Combine(_folder, "Projects");
        };
        var main = new MainWindowViewModel(services, services);

        await main.ImportFolderCommand.ExecuteAsync(null);

        var path = Path.Combine(_folder, "Projects", "Shop", "Shop.umlwiz");
        Assert.True(File.Exists(path));
        Assert.Equal(path, main.ProjectPath);
        Assert.Equal("Imported from Shop.", main.Project!.Description);

        // One diagram, opened, with the association the collection property makes.
        var diagram = Assert.Single(ProjectSerializer.Load(path).Diagrams);
        Assert.Equal("Shop", diagram.Name);
        var model = (ClassDiagramModel)diagram.Model;
        Assert.Equal(["Order", "OrderLine"], model.Classes.Select(c => c.Name));
        Assert.Equal("*", Assert.Single(model.Relationships).TargetMultiplicity);
        Assert.Equal("Shop", main.ActiveDiagram?.Name);

        var report = Assert.Single(services.Messages);
        Assert.StartsWith("Imported 2 classes, 0 interfaces and 0 enumerations from the folder Shop (2 files).", report);
        Assert.Contains("• Only C# source files (.cs) are read", report);
        Assert.Contains("• 1 other file was skipped: 1 .xaml.", report);
        Assert.Contains("• 1 event was left out.", report);
    }

    [AvaloniaFact]
    public async Task ImportFolder_WithoutTypes_CreatesNoProject()
    {
        var empty = Path.Combine(_folder, "Code", "Empty");
        Directory.CreateDirectory(empty);
        File.WriteAllText(Path.Combine(empty, "Nothing.cs"), "// Nothing here yet.");
        var services = new FakeServices { FolderPath = empty, AcceptDialogs = true };
        services.FillDialog = d => ((ImportCodeViewModel)d).Location = Path.Combine(_folder, "Projects");
        var main = new MainWindowViewModel(services, services);

        await main.ImportFolderCommand.ExecuteAsync(null);

        Assert.Equal(
            "No classes, interfaces, enumerations, structs or records were found in Empty, so no project was created.",
            Assert.Single(services.Errors));
        Assert.False(Directory.Exists(Path.Combine(_folder, "Projects", "Empty")));
        Assert.Null(main.Project);
    }

    [AvaloniaFact]
    public async Task ImportFolder_DoesNotOverwriteAnExistingProject()
    {
        Directory.CreateDirectory(Path.Combine(_folder, "Projects", "Shop"));
        File.WriteAllText(Path.Combine(_folder, "Projects", "Shop", "Shop.umlwiz"), "{}");
        var services = new FakeServices { FolderPath = Shop, AcceptDialogs = true };
        services.FillDialog = d => ((ImportCodeViewModel)d).Location = Path.Combine(_folder, "Projects");
        var main = new MainWindowViewModel(services, services);

        await main.ImportFolderCommand.ExecuteAsync(null);

        Assert.Contains("already exists", Assert.Single(services.Errors));
        Assert.Equal("{}", File.ReadAllText(Path.Combine(_folder, "Projects", "Shop", "Shop.umlwiz")));
        Assert.Null(main.Project);
    }

    [AvaloniaFact]
    public void Dialog_SaysUpFrontWhatIsNotImported()
    {
        var csharp = InstalledPlugins.CodeImporters.Find("CSharp")!;
        var dialog = ImportCodeViewModel.ForFolder(new FakeServices(), Shop, [new ImportLanguage(csharp, 2)]);
        var window = new DialogWindow { DataContext = dialog };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var notice = window.GetLogicalDescendants().OfType<TextBlock>().Single(t => t.Name == "ImportNotice");
        Assert.True(notice.IsEffectivelyVisible);
        Assert.Contains("Only C# source files (.cs) are read: other files are skipped.", notice.Text);
        Assert.Contains("a summary lists everything that was left out", notice.Text);
        Assert.Equal("2 C# files", window.GetLogicalDescendants().OfType<TextBlock>().Single(t => t.Name == "FilesLine").Text);
        Assert.True(window.GetLogicalDescendants().OfType<ComboBox>().Single(c => c.Name == "LanguageBox").IsEffectivelyVisible);
        window.Close();
    }

    [AvaloniaFact]
    public async Task ImportFolder_OffersTheLanguagesItHasFilesOf_AndImportsTheChosenOne()
    {
        var code = Path.Combine(_folder, "Code", "shop-java");
        Directory.CreateDirectory(Path.Combine(code, "src", "shop"));
        File.WriteAllText(Path.Combine(code, "src", "shop", "Order.java"), "package shop; public class Order extends Entity { private java.util.List<Line> lines; }");
        File.WriteAllText(Path.Combine(code, "src", "shop", "Entity.java"), "package shop; public abstract class Entity { }");
        File.WriteAllText(Path.Combine(code, "src", "shop", "Line.java"), "package shop; class Line { int quantity; }");
        File.WriteAllText(Path.Combine(code, "tools.rb"), "class Tool; end");
        var services = new FakeServices { FolderPath = code, AcceptDialogs = true };
        services.FillDialog = d =>
        {
            var import = Assert.IsType<ImportCodeViewModel>(d);
            Assert.Equal("Import Source Folder", import.Title);
            Assert.Equal(["Java (3 files)", "Ruby (1 file)"], import.Languages.Select(l => l.Label));
            Assert.Equal("Java", import.SelectedLanguage.Importer.DisplayName);
            Assert.Equal("3 Java files", import.FilesLine);
            Assert.Contains("Only Java source files (.java) are read", import.Notice);
            Assert.Equal("shop-java", import.Name);
            import.Location = Path.Combine(_folder, "Projects");
        };
        var main = new MainWindowViewModel(services, services);

        await main.ImportFolderCommand.ExecuteAsync(null);

        var diagram = Assert.Single(ProjectSerializer.Load(Path.Combine(_folder, "Projects", "shop-java", "shop-java.umlwiz")).Diagrams);
        var model = (ClassDiagramModel)diagram.Model;
        Assert.Equal(["Entity", "Line", "Order"], model.Classes.Select(c => c.Name).Order());
        Assert.Equal(2, model.Relationships.Count);
        Assert.StartsWith("Imported 3 classes, 0 interfaces and 0 enumerations from the folder shop-java (3 files).", Assert.Single(services.Messages));
        Assert.Contains("1 other file was skipped: 1 .rb.", services.Messages[0]);
    }

    [AvaloniaFact]
    public async Task ImportFolder_WithoutSourceFiles_IsRefused()
    {
        var empty = Path.Combine(_folder, "Code", "docs");
        Directory.CreateDirectory(empty);
        File.WriteAllText(Path.Combine(empty, "README.md"), "# Docs");
        var services = new FakeServices { FolderPath = empty, AcceptDialogs = true };
        var main = new MainWindowViewModel(services, services);

        await main.ImportFolderCommand.ExecuteAsync(null);

        Assert.Equal($"{empty} has no source files of a language that can be imported (C#, C++, Java, Kotlin, Ruby, TypeScript).", Assert.Single(services.Errors));
        Assert.Empty(services.ShownDialogs);
    }

    [AvaloniaFact]
    public async Task ImportFolder_WithoutCodeImportPlugins_SaysWhatIsMissing()
    {
        var services = new FakeServices { FolderPath = Shop, AcceptDialogs = true };
        var main = new MainWindowViewModel(services, services, codeImporters: new CodeImport.Plugins.CodeImporterCatalog([]));

        await main.ImportFolderCommand.ExecuteAsync(null);

        Assert.StartsWith("Shop cannot be imported: no code import plug-in for any language is in ", Assert.Single(services.Errors));
        Assert.Empty(services.ShownDialogs);
    }
}
