using System.Text.Json.Nodes;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using UmlWizard.App.Services;
using UmlWizard.App.ViewModels;
using UmlWizard.App.Views;
using UmlWizard.Core.Model;

namespace UmlWizard.App.Tests;

public sealed class OpenTabsTests
{
    private static void OpenAandB(ExplorerFixture f)
    {
        f.Main.OpenDiagramCommand.Execute((DiagramItemViewModel)f.Node("A"));
        f.Main.OpenDiagramCommand.Execute((DiagramItemViewModel)f.Node("B"));
    }

    [AvaloniaFact]
    public async Task ClosingTheProject_SavesTabs_AndReopeningRestoresThem()
    {
        using var f = new ExplorerFixture();
        OpenAandB(f);
        var a = f.Main.OpenDiagrams[0];
        f.Main.MoveTab(a, 1);

        Assert.True(await f.Main.TryCloseProjectAsync());

        var saved = f.Saved;
        var tabs = f.SavedTabs!;
        Assert.Equal(["B", "A"], tabs.OpenDiagramIds.Select(id => saved.FindDiagram(id)!.Name));
        Assert.Equal("A", saved.FindDiagram(tabs.ActiveDiagramId!)!.Name);

        await f.Main.OpenProjectFileAsync(f.Path);

        Assert.Equal(["B", "A"], f.Main.OpenDiagrams.Select(d => d.Name));
        Assert.Equal("A", f.Main.ActiveDiagram?.Name);
    }

    [AvaloniaFact]
    public async Task TheTabs_AreKeptForTheUser_NotInTheProjectFile()
    {
        using var f = new ExplorerFixture();
        var before = File.ReadAllText(f.Path);
        OpenAandB(f);

        Assert.True(await f.Main.TryCloseProjectAsync());

        Assert.Equal(before, File.ReadAllText(f.Path));
        Assert.Equal(["Shop.umlwiz"], Directory.GetFiles(f.Folder).Select(Path.GetFileName));
        Assert.Equal(2, f.SavedTabs!.OpenDiagramIds.Count);
    }

    [AvaloniaFact]
    public async Task SavingTheProject_AlsoSavesTheTabs_ButNotInTheProjectFile()
    {
        using var f = new ExplorerFixture();
        OpenAandB(f);
        f.Main.ActiveDiagram!.Editor.AddLifeline(LifelineKind.Object, 0);

        await f.Main.SaveProjectCommand.ExecuteAsync(null);

        Assert.Equal(2, f.SavedTabs!.OpenDiagramIds.Count);
        Assert.Equal(f.Main.ActiveDiagram.Id, f.SavedTabs.ActiveDiagramId);
        var project = JsonNode.Parse(File.ReadAllText(f.Path))!.AsObject();
        Assert.False(project.ContainsKey("openDiagramIds"));
        Assert.False(project.ContainsKey("activeDiagramId"));
    }

    [AvaloniaFact]
    public async Task SavingTheProjectAs_TakesTheTabsAlong()
    {
        using var f = new ExplorerFixture();
        OpenAandB(f);
        f.Services.SaveFilePath = Path.Combine(f.Folder, "Copy.umlwiz");

        await f.Main.SaveProjectAsCommand.ExecuteAsync(null);

        Assert.Equal(2, f.ProjectStates.Load(f.Services.SaveFilePath)!.OpenDiagramIds.Count);
    }

    [AvaloniaFact]
    public async Task UnchangedTabs_DoNotWriteAnything()
    {
        using var f = new ExplorerFixture();
        var before = File.ReadAllText(f.Path);

        Assert.True(await f.Main.TryCloseProjectAsync());

        Assert.Equal(before, File.ReadAllText(f.Path));
        Assert.Null(f.SavedTabs);
    }

    [AvaloniaFact]
    public async Task UnsavedChanges_DiscardedOnClose_AreNotWritten_ButTabsAre()
    {
        using var f = new ExplorerFixture();
        OpenAandB(f);
        f.Main.ActiveDiagram!.Editor.AddLifeline(LifelineKind.Object, 0);
        f.Services.ConfirmAnswer = ConfirmResult.No;

        Assert.True(await f.Main.TryCloseProjectAsync());

        Assert.Equal(2, f.SavedTabs!.OpenDiagramIds.Count);
        Assert.Empty(((SequenceDiagramModel)f.Saved.Diagrams.Single(d => d.Name == "B").Model).Lifelines);
    }

    [AvaloniaFact]
    public void ClosingTheWindow_SavesTheTabs()
    {
        using var f = new ExplorerFixture();
        var window = new MainWindow { DataContext = f.Main };
        window.Show();
        OpenAandB(f);
        Dispatcher.UIThread.RunJobs();

        window.Close();
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(2, f.SavedTabs!.OpenDiagramIds.Count);
    }

    [AvaloniaFact]
    public async Task TheTabs_OfAProjectFromAnEarlierVersion_AreReadFromTheProjectFile()
    {
        // Earlier versions kept the tabs in the project file; the user's own state takes over from the next save or close.
        using var f = new ExplorerFixture();
        Assert.True(await f.Main.TryCloseProjectAsync());
        var project = JsonNode.Parse(File.ReadAllText(f.Path))!.AsObject();
        var b = f.Saved.Diagrams.Single(d => d.Name == "B").Id;
        project["openDiagramIds"] = new JsonArray("gone", b);
        project["activeDiagramId"] = b;
        File.WriteAllText(f.Path, project.ToJsonString());

        await f.Main.OpenProjectFileAsync(f.Path);

        Assert.Equal(["B"], f.Main.OpenDiagrams.Select(d => d.Name));
        Assert.Equal("B", f.Main.ActiveDiagram?.Name);
    }

    [AvaloniaFact]
    public async Task TheExpandedFolders_AreSavedForTheUser_AndRestored()
    {
        using var f = new ExplorerFixture();
        Assert.True(f.Node("Model").IsExpanded);
        f.Node("Model").IsExpanded = false;
        var nested = f.Node("Nested").Id;

        Assert.True(await f.Main.TryCloseProjectAsync());

        Assert.Equal([ProjectRootNodeViewModel.RootId, nested], f.SavedTabs!.ExpandedFolderIds!.Order());
        await f.Main.OpenProjectFileAsync(f.Path);

        Assert.False(f.Node("Model").IsExpanded);
        Assert.True(f.Node("Nested").IsExpanded);
        Assert.True(f.Main.ProjectRoot!.IsExpanded);
    }

    [AvaloniaFact]
    public async Task ACollapsedProject_StaysCollapsed()
    {
        using var f = new ExplorerFixture();
        f.Main.ProjectRoot!.IsExpanded = false;

        Assert.True(await f.Main.TryCloseProjectAsync());
        await f.Main.OpenProjectFileAsync(f.Path);

        Assert.False(f.Main.ProjectRoot!.IsExpanded);
    }

    [AvaloniaFact]
    public async Task TheFolderOfTheActiveTab_IsExpandedAgain_SoItsDiagramStaysVisible()
    {
        using var f = new ExplorerFixture();
        f.Main.OpenDiagramCommand.Execute((DiagramItemViewModel)f.Node("A"));
        f.Node("Model").IsExpanded = false;

        Assert.True(await f.Main.TryCloseProjectAsync());
        await f.Main.OpenProjectFileAsync(f.Path);

        Assert.Equal("A", f.Main.ActiveDiagram?.Name);
        Assert.True(f.Node("Model").IsExpanded);
    }

    [Fact]
    public void MissingFolders_AreDropped()
    {
        var project = new UmlProject();
        var model = project.AddFolder("Model");
        var state = new ProjectUserState { ExpandedFolderIds = ["gone", model.Id, model.Id, ProjectRootNodeViewModel.RootId] };

        state.Normalize(project);

        Assert.Equal([model.Id, ProjectRootNodeViewModel.RootId], state.ExpandedFolderIds);
    }

    [Fact]
    public void MissingDiagrams_AreDropped()
    {
        var project = new UmlProject();
        var a = DiagramDocument.Create("A", DiagramType.Class);
        project.Diagrams.Add(a);
        var state = new ProjectUserState { OpenDiagramIds = ["gone", a.Id, a.Id], ActiveDiagramId = "gone" };

        state.Normalize(project);

        Assert.Equal([a.Id], state.OpenDiagramIds);
        Assert.Null(state.ActiveDiagramId);
    }
}

public sealed class ProjectStateFileStoreTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), $"umlwizard-states-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }
    }

    [Fact]
    public void TheState_OfEachProject_IsKeptInAFileOfItsOwn()
    {
        var store = new ProjectStateFileStore(_folder);
        var shop = Path.Combine(_folder, "work", "Shop.umlwiz");
        var bank = Path.Combine(_folder, "work", "Bank.umlwiz");

        store.Save(shop, new ProjectUserState { OpenDiagramIds = ["a", "b"], ActiveDiagramId = "b" });
        store.Save(bank, new ProjectUserState { OpenDiagramIds = ["c"] });

        var reread = new ProjectStateFileStore(_folder);
        Assert.Equal(["a", "b"], reread.Load(shop)!.OpenDiagramIds);
        Assert.Equal("b", reread.Load(shop)!.ActiveDiagramId);
        Assert.Equal(shop, reread.Load(shop)!.ProjectPath);
        Assert.Equal(["c"], reread.Load(bank)!.OpenDiagramIds);
        Assert.Null(reread.Load(Path.Combine(_folder, "work", "Other.umlwiz")));
        Assert.Equal(2, Directory.GetFiles(_folder, "*.json").Length);
        if (OperatingSystem.IsWindows())
        {
            Assert.Equal(["a", "b"], reread.Load(shop.ToUpperInvariant())!.OpenDiagramIds);
        }
    }

    [Fact]
    public void AnUnreadableFile_OrOneOfAnotherProject_GivesNoState()
    {
        var store = new ProjectStateFileStore(_folder);
        var shop = Path.Combine(_folder, "Shop.umlwiz");
        Directory.CreateDirectory(_folder);

        File.WriteAllText(store.FileFor(shop), "not json");
        Assert.Null(store.Load(shop));

        File.WriteAllText(store.FileFor(shop), """{ "projectPath": "C:\\elsewhere\\Bank.umlwiz", "openDiagramIds": ["a"] }""");
        Assert.Null(store.Load(shop));
    }

    [Fact]
    public void AFolderThatCannotBeWritten_IsIgnored()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_folder)!);
        File.WriteAllText(_folder, "a file where the folder should be");
        try
        {
            new ProjectStateFileStore(_folder).Save(Path.Combine(Path.GetTempPath(), "Shop.umlwiz"), new ProjectUserState());
        }
        finally
        {
            File.Delete(_folder);
        }
    }
}
