using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using UmlWizard.App.Resources;
using UmlWizard.App.Services;
using UmlWizard.App.ViewModels;
using UmlWizard.App.ViewModels.Dialogs;
using UmlWizard.App.Views;
using UmlWizard.App.Views.Dialogs;
using UmlWizard.CodeGeneration;
using UmlWizard.CodeGeneration.Plugins;
using UmlWizard.Core.Model;

namespace UmlWizard.App.Tests;

public sealed class GenerateCodeTests
{
    /// <summary>A language of the plug-ins copied to the test's plugins folder, as in the application.</summary>
    private static ICodeGenerator Language(string id) => InstalledPlugins.CodeGenerators.Find(id)!;

    /// <summary>Gives the class diagram "A" of the fixture an Order class and an OrderStatus enumeration, and selects it.</summary>
    private static ClassDiagramModel SelectShopDiagram(ExplorerFixture f)
    {
        var item = (DiagramItemViewModel)f.Node("A");
        var model = (ClassDiagramModel)item.Document.Model;
        model.Classes.Add(new UmlClass { Name = "Order", Operations = { new UmlOperation { Name = "total", ReturnType = "decimal" } } });
        model.Classes.Add(new UmlClass { Name = "OrderStatus", Kind = ClassifierKind.Enumeration, Attributes = { new UmlAttribute { Name = "New" } } });
        f.Main.SelectedNode = item;
        return model;
    }

    [AvaloniaFact]
    public void GenerateCode_IsOnlyAvailable_ForASelectedClassDiagram()
    {
        using var f = new ExplorerFixture();
        Assert.False(f.Main.GenerateCodeCommand.CanExecute(null));

        f.Main.SelectedNode = f.Node("B"); // a sequence diagram
        Assert.False(f.Main.GenerateCodeCommand.CanExecute(null));

        f.Main.SelectedNode = f.Node("Model"); // a folder
        Assert.False(f.Main.GenerateCodeCommand.CanExecute(null));

        f.Main.SelectedNode = f.Node("A");
        Assert.True(f.Main.GenerateCodeCommand.CanExecute(null));
    }

    [AvaloniaFact]
    public async Task TheDialog_SuggestsAFolderAndNamespace_ThatFollowTheLanguage_AndWritesTheFiles()
    {
        using var f = new ExplorerFixture();
        SelectShopDiagram(f);
        f.Services.FillDialog = d =>
        {
            var options = Assert.IsType<GenerateCodeViewModel>(d);
            Assert.Equal("CSharp", options.Language.Id);
            Assert.Equal(Path.Combine(f.Folder, "Generated", "CSharp"), options.OutputFolder);
            Assert.Equal("Shop", options.Namespace);
            Assert.Equal(Strings.Dialog_GenerateCode_Namespace, options.NamespaceLabel);
            Assert.Equal(["Order.cs", "OrderStatus.cs"], options.Files.Select(i => i.Path));

            options.Language = Language("Java");
            Assert.Equal(Path.Combine(f.Folder, "Generated", "Java"), options.OutputFolder);
            Assert.Equal("shop", options.Namespace);
            Assert.Equal(Strings.Dialog_GenerateCode_Package, options.NamespaceLabel);
            Assert.Equal(["shop/Order.java", "shop/OrderStatus.java"], options.Files.Select(i => i.Path));
            Assert.Equal(Loc.Format(Strings.Dialog_GenerateCode_FileCount, 2), options.FileSummary);
            Assert.True(options.TryAccept());
        };

        await f.Main.GenerateCodeCommand.ExecuteAsync(null);

        var order = Path.Combine(f.Folder, "Generated", "Java", "shop", "Order.java");
        Assert.Contains("public BigDecimal total() {", File.ReadAllText(order));
        Assert.True(File.Exists(Path.Combine(f.Folder, "Generated", "Java", "shop", "OrderStatus.java")));
        Assert.Equal(Loc.Format(Strings.Message_CodeGenerated, 2, Path.Combine(f.Folder, "Generated", "Java")), Assert.Single(f.Services.Messages));
        Assert.Empty(f.Services.ConfirmMessages);
        Assert.Empty(f.Main.OpenDiagrams);
    }

    [AvaloniaFact]
    public async Task ChangedChoices_AreOfferedAgain_AndAChangedFolderIsKeptWhenTheLanguageChanges()
    {
        using var f = new ExplorerFixture();
        SelectShopDiagram(f);
        var output = Path.Combine(f.Folder, "src");
        f.Services.FillDialog = d =>
        {
            var options = (GenerateCodeViewModel)d;
            options.Language = Language("Ruby");
            options.OutputFolder = output;
            options.Namespace = "Shop::Domain";
            Assert.Equal(Strings.Dialog_GenerateCode_Module, options.NamespaceLabel);

            // The folder is the user's own now: another language does not change it.
            options.Language = Language("Cpp");
            Assert.Equal(output, options.OutputFolder);
            Assert.Equal("Shop::Domain", options.Namespace);
            options.Language = Language("Ruby");
        };

        await f.Main.GenerateCodeCommand.ExecuteAsync(null);
        Assert.True(File.Exists(Path.Combine(output, "shop", "domain", "order.rb")));

        f.Services.AcceptDialogs = false;
        f.Services.FillDialog = d =>
        {
            var options = (GenerateCodeViewModel)d;
            Assert.Equal(("Ruby", output, "Shop::Domain"), (options.Language.Id, options.OutputFolder, options.Namespace));
        };
        await f.Main.GenerateCodeCommand.ExecuteAsync(null);
        Assert.Equal(2, f.Services.ShownDialogs.Count);
    }

    [AvaloniaFact]
    public async Task ExistingFiles_AreMarked_AndOnlyReplacedAfterConfirming_OtherwiseTheDialogReturns()
    {
        using var f = new ExplorerFixture();
        SelectShopDiagram(f);
        var folder = Directory.CreateDirectory(Path.Combine(f.Folder, "Generated", "CSharp")).FullName;
        var existing = Path.Combine(folder, "Order.cs");
        File.WriteAllText(existing, "// mine");
        var shown = 0;
        f.Services.FillDialog = d =>
        {
            var options = (GenerateCodeViewModel)d;
            Assert.Equal([true, false], options.Files.Select(i => i.Exists));
            Assert.Equal(Loc.Format(Strings.Dialog_GenerateCode_ExistingCount, 2, 1), options.FileSummary);
            if (++shown == 2)
            {
                f.Services.ConfirmAnswer = ConfirmResult.Yes;
            }
        };

        // "No" to replacing: the dialog comes back, and nothing is written.
        f.Services.ConfirmAnswer = ConfirmResult.No;
        await f.Main.GenerateCodeCommand.ExecuteAsync(null);

        Assert.Equal(2, shown);
        Assert.Equal(2, f.Services.ConfirmMessages.Count);
        Assert.Equal(Loc.Format(Strings.Message_ReplaceGeneratedFiles, 1, folder), f.Services.ConfirmMessages[0]);
        Assert.Contains("public class Order", File.ReadAllText(existing));
    }

    [AvaloniaFact]
    public void Validation_NeedsAFullFolderPath_AValidNamespace_AndSomethingToGenerate()
    {
        using var f = new ExplorerFixture();
        var model = SelectShopDiagram(f);
        var options = new GenerateCodeViewModel(new FakeServices(), model, "A", f.Folder, "Shop", InstalledPlugins.CodeGenerators, "CSharp", null, null);
        Assert.True(options.TryAccept());

        options.OutputFolder = " ";
        Assert.False(options.TryAccept());
        Assert.Equal(Strings.Dialog_GenerateCode_NeedsFolder, options.ErrorMessage);

        options.OutputFolder = "relative\\folder";
        Assert.False(options.TryAccept());
        Assert.Equal(Strings.Dialog_GenerateCode_InvalidFolder, options.ErrorMessage);

        options.OutputFolder = f.Folder;
        options.Namespace = "Shop Domain";
        Assert.False(options.TryAccept());
        Assert.Equal(Strings.Dialog_GenerateCode_InvalidNamespace, options.ErrorMessage);

        options.Namespace = "";
        Assert.True(options.TryAccept());

        var empty = new GenerateCodeViewModel(new FakeServices(), new ClassDiagramModel(), "E", f.Folder, "Shop", InstalledPlugins.CodeGenerators, "Java", null, null);
        Assert.False(empty.TryAccept());
        Assert.Equal(Strings.Dialog_GenerateCode_NoClasses, empty.ErrorMessage);
    }

    [AvaloniaFact]
    public void TheDialog_ListsTheFilesAndNotes_AndKeepsItsSize_WhenTheLanguageChanges()
    {
        using var f = new ExplorerFixture();
        var model = SelectShopDiagram(f);
        model.Classes.Add(new UmlClass { Name = "Cart", Attributes = { new UmlAttribute { Name = "items", Type = "List<Item>" } } });
        model.Classes[1].Operations.Add(new UmlOperation { Name = "next" }); // enumerations have no operations in C# and C++
        var options = new GenerateCodeViewModel(new FakeServices(), model, "A", f.Folder, "Shop", InstalledPlugins.CodeGenerators, "Java", null, null);
        var window = new DialogWindow { DataContext = options };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var view = window.GetLogicalDescendants().OfType<GenerateCodeView>().Single();
        var files = view.FindControl<ItemsControl>("FileList")!;
        var warnings = view.FindControl<ItemsControl>("WarningList")!;
        Assert.Equal(3, files.ItemCount);
        Assert.Equal(1, warnings.ItemCount); // Item is not in the diagram
        var size = window.Bounds.Size;

        options.Language = Language("Cpp");
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(4, files.ItemCount); // Order.h, Order.cpp, OrderStatus.h, Cart.h
        Assert.Equal(2, warnings.ItemCount);
        Assert.Equal(size, window.Bounds.Size);
        window.Close();
    }

    [AvaloniaFact]
    public void TheLanguageBox_ListsTheInstalledPlugins_ByDisplayName()
    {
        using var f = new ExplorerFixture();
        var model = SelectShopDiagram(f);
        var options = new GenerateCodeViewModel(new FakeServices(), model, "A", f.Folder, "Shop", InstalledPlugins.CodeGenerators, "Java", null, null);
        var window = new DialogWindow { DataContext = options };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var box = window.GetLogicalDescendants().OfType<GenerateCodeView>().Single().FindControl<ComboBox>("LanguageBox")!;
        Assert.Equal(["C#", "C++", "Java", "Kotlin", "Ruby", "TypeScript"], box.Items.Cast<ICodeGenerator>().Select(g => g.DisplayName));
        Assert.Equal("Java", ((ICodeGenerator)box.SelectedItem!).Id);
        window.Close();
    }

    [AvaloniaFact]
    public async Task WithoutPlugins_GenerateCode_SaysWhereToPutThem()
    {
        using var f = new ExplorerFixture(new CodeGeneratorCatalog([]));
        SelectShopDiagram(f);

        await f.Main.GenerateCodeCommand.ExecuteAsync(null);

        Assert.Empty(f.Services.ShownDialogs);
        Assert.Equal(Loc.Format(Strings.Message_NoCodeGenerators, string.Join(Environment.NewLine, InstalledPlugins.Folders)), Assert.Single(f.Services.Errors));
    }

    [AvaloniaFact]
    public void AFailingPlugin_AndPluginProblems_AreShownAsNotes_NotThrown()
    {
        using var f = new ExplorerFixture();
        var model = SelectShopDiagram(f);
        var catalog = new CodeGeneratorCatalog([new FailingGenerator()], ["The plug-in Old.dll could not be loaded."]);
        var options = new GenerateCodeViewModel(new FakeServices(), model, "A", f.Folder, "Shop", catalog, null, null, null);

        Assert.Empty(options.Files);
        Assert.Equal(
            ["The plug-in Old.dll could not be loaded.", Loc.Format(Strings.Message_CodeGeneratorFailed, "Broken", "boom")],
            options.Warnings);
        Assert.False(options.TryAccept());
    }

    [AvaloniaFact]
    public void TheDialog_GeneratesTheCodeAgain_OnlyWhenTheLanguageOrNamespaceChanges()
    {
        using var f = new ExplorerFixture();
        var model = (ClassDiagramModel)f.Saved.Diagrams.Single(d => d.Name == "A").Model;
        model.Classes.Add(new UmlClass { Name = "Order" });
        var counting = new CountingGenerator();
        var options = new GenerateCodeViewModel(new FakeServices(), model, "A", f.Folder, "Shop", new CodeGeneratorCatalog([counting]), null, null, null);
        Assert.Equal(1, counting.Calls);

        options.OutputFolder = Path.Combine(f.Folder, "Elsewhere");
        Assert.True(options.TryAccept());
        Assert.Equal(1, counting.Calls);

        options.Namespace = "Shop.Domain";
        Assert.Equal(2, counting.Calls);
    }

    /// <summary>Holds the second file open until the test lets it go, to see what the window shows meanwhile.</summary>
    private static (TaskCompletionSource Release, Func<string, string, CancellationToken, Task> Write) HoldSecondFile()
    {
        var release = new TaskCompletionSource();
        var written = 0;
        return (release, async (path, content, token) =>
        {
            if (Interlocked.Increment(ref written) == 2)
            {
                await release.Task.WaitAsync(token);
            }

            await File.WriteAllTextAsync(path, content, token);
        });
    }

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
    public async Task WritingTheFiles_ShowsHowFarItIs()
    {
        using var f = new ExplorerFixture();
        SelectShopDiagram(f);
        f.Services.FillDialog = d => Assert.True(((GenerateCodeViewModel)d).TryAccept());
        var (release, write) = HoldSecondFile();
        f.Main.WriteFileAsync = write;

        var generating = f.Main.GenerateCodeCommand.ExecuteAsync(null);
        RunJobsUntil(() => f.Main.IsBusy && f.Main.BusyText == "Writing 1 of 2 files…");
        Assert.Equal(0.5, f.Main.BusyProgress);

        release.SetResult();
        await generating;
        Assert.False(f.Main.IsBusy);
        Assert.Equal(Loc.Format(Strings.Message_CodeGenerated, 2, Path.Combine(f.Folder, "Generated", "CSharp")), Assert.Single(f.Services.Messages));
    }

    [AvaloniaFact]
    public async Task CancellingWritingTheFiles_SaysSomeMayHaveBeenWritten()
    {
        using var f = new ExplorerFixture();
        SelectShopDiagram(f);
        f.Services.FillDialog = d => Assert.True(((GenerateCodeViewModel)d).TryAccept());
        var (_, write) = HoldSecondFile();
        f.Main.WriteFileAsync = write;

        var generating = f.Main.GenerateCodeCommand.ExecuteAsync(null);
        RunJobsUntil(() => f.Main.IsBusy);
        f.Main.CancelBusyCommand.Execute(null);
        await generating;

        Assert.False(f.Main.IsBusy);
        var folder = Path.Combine(f.Folder, "Generated", "CSharp");
        Assert.Equal(Loc.Format(Strings.Message_CodeGenerationCancelled, folder), Assert.Single(f.Services.Messages));
        Assert.True(File.Exists(Path.Combine(folder, "Order.cs")));
        Assert.False(File.Exists(Path.Combine(folder, "OrderStatus.cs")));
    }

    /// <summary>Counts how often the code is generated.</summary>
    private sealed class CountingGenerator : ICodeGenerator
    {
        public int Calls { get; private set; }

        public string Id => "Count";

        public string DisplayName => "Count";

        public NamespaceKind NamespaceKind => NamespaceKind.Namespace;

        public bool MultipleInheritance => false;

        public string DefaultNamespace(string projectIdentifier) => projectIdentifier;

        public IEnumerable<GeneratedFile> Generate(CodeModel model, CodeGenerationOptions options, IList<string> warnings)
        {
            Calls++;
            return model.Types.Select(t => new GeneratedFile(t.Name + ".txt", t.Name));
        }
    }

    private sealed class FailingGenerator : ICodeGenerator
    {
        public string Id => "Broken";

        public string DisplayName => "Broken";

        public NamespaceKind NamespaceKind => NamespaceKind.Namespace;

        public bool MultipleInheritance => false;

        public string DefaultNamespace(string projectIdentifier) => projectIdentifier;

        public IEnumerable<GeneratedFile> Generate(CodeModel model, CodeGenerationOptions options, IList<string> warnings) =>
            throw new InvalidOperationException("boom");
    }

    [AvaloniaFact]
    public void TheDiagramMenu_OffersGenerateCode_AfterExport()
    {
        var services = new FakeServices();
        var window = new MainWindow { DataContext = new MainWindowViewModel(services, services) };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var diagram = window.FindControl<Menu>("MainMenu")!.Items.OfType<MenuItem>().Single(m => (string?)m.Header == "_Diagram");
        var headers = diagram.Items.OfType<MenuItem>().Select(m => (string?)m.Header).ToList();
        Assert.Equal(headers.IndexOf("_Export") + 1, headers.IndexOf("_Generate Code…"));
        window.Close();
    }
}
