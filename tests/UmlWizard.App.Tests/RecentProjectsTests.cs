using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using UmlWizard.App.Services;
using UmlWizard.App.ViewModels;
using UmlWizard.App.Views;
using UmlWizard.Core.Model;
using UmlWizard.Core.Serialization;

namespace UmlWizard.App.Tests;

public sealed class RecentProjectsTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), $"umlwizard-recent-{Guid.NewGuid():N}");

    public RecentProjectsTests() => Directory.CreateDirectory(_folder);

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    private string CreateProject(string name)
    {
        var path = Path.Combine(_folder, name + UmlProject.FileExtension);
        ProjectSerializer.Save(new UmlProject { Name = name }, path);
        return path;
    }

    [AvaloniaFact]
    public async Task OpeningProjects_KeepsMostRecentFirst_WithoutDuplicates()
    {
        var services = new FakeServices();
        var store = new InMemoryRecentProjectsStore();
        var main = new MainWindowViewModel(services, services, store);
        var a = CreateProject("A");
        var b = CreateProject("B");

        await main.OpenProjectFileAsync(a);
        await main.OpenProjectFileAsync(b);
        var upper = a.ToUpperInvariant();
        await main.OpenProjectFileAsync(upper);

        // Windows: the same project, so not a duplicate. macOS: the same file (the file system ignores case), listed as
        // written. Linux: another file, which does not exist, so it is not opened and not listed.
        string[] expected = OperatingSystem.IsWindows() ? [upper, b] : File.Exists(upper) ? [upper, b, a] : [b, a];
        Assert.Equal(expected, main.RecentProjects);
        Assert.Equal(main.RecentProjects, store.Load());
        Assert.True(main.HasRecentProjects);
    }

    [AvaloniaFact]
    public async Task List_IsLimitedToTen()
    {
        var services = new FakeServices();
        var main = new MainWindowViewModel(services, services);
        var paths = Enumerable.Range(1, 12).Select(i => CreateProject($"P{i}")).ToList();

        foreach (var path in paths)
        {
            await main.OpenProjectFileAsync(path);
        }

        Assert.Equal(MainWindowViewModel.MaxRecentProjects, main.RecentProjects.Count);
        Assert.Equal(paths[11], main.RecentProjects[0]);
        Assert.Equal(paths[2], main.RecentProjects[9]);
    }

    [AvaloniaFact]
    public void StoredList_IsRestored_AndCleanedUp()
    {
        var services = new FakeServices();
        var stored = Enumerable.Range(1, 12).Select(i => Path.Combine(_folder, $"S{i}.umlwiz")).Append("").ToList();

        var main = new MainWindowViewModel(services, services, new InMemoryRecentProjectsStore(stored));

        Assert.Equal(stored.Take(10), main.RecentProjects);
        Assert.Equal(12, main.RecentProjectsMenu.Count);
        Assert.Equal("-", main.RecentProjectsMenu[10].Header);
        Assert.Equal("_Clear Recent Projects", main.RecentProjectsMenu[11].Header);
    }

    [AvaloniaFact]
    public async Task OpenRecentProject_OpensIt()
    {
        var services = new FakeServices();
        var path = CreateProject("Shop");
        var main = new MainWindowViewModel(services, services, new InMemoryRecentProjectsStore([path]));

        await main.OpenRecentProjectCommand.ExecuteAsync(path);

        Assert.Equal("Shop", main.Project?.Name);
    }

    [AvaloniaFact]
    public async Task OpenRecentProject_WhenMissing_OffersToRemoveIt()
    {
        var services = new FakeServices();
        var missing = Path.Combine(_folder, "Gone.umlwiz");
        var kept = CreateProject("Kept");
        var main = new MainWindowViewModel(services, services, new InMemoryRecentProjectsStore([missing, kept]));

        services.ConfirmAnswer = ConfirmResult.No;
        await main.OpenRecentProjectCommand.ExecuteAsync(missing);
        Assert.Equal([missing, kept], main.RecentProjects);

        services.ConfirmAnswer = ConfirmResult.Yes;
        await main.OpenRecentProjectCommand.ExecuteAsync(missing);
        Assert.Equal([kept], main.RecentProjects);
        Assert.Null(main.Project);
        Assert.Contains(missing, services.ConfirmMessages[0]);
    }

    [AvaloniaFact]
    public void ClearRecentProjects_EmptiesListAndStore()
    {
        var services = new FakeServices();
        var store = new InMemoryRecentProjectsStore([Path.Combine(_folder, "A.umlwiz")]);
        var main = new MainWindowViewModel(services, services, store);

        main.ClearRecentProjectsCommand.Execute(null);

        Assert.Empty(main.RecentProjects);
        Assert.Empty(main.RecentProjectsMenu);
        Assert.Empty(store.Load());
        Assert.False(main.HasRecentProjects);
    }

    [Fact]
    public void FileStore_RoundTrips_AndIgnoresCorruptFiles()
    {
        var file = Path.Combine(_folder, "sub", "recent.json");
        var store = new RecentProjectsFileStore(file);

        Assert.Empty(store.Load());
        store.Save([@"C:\a.umlwiz", @"D:\b.umlwiz"]);
        Assert.Equal([@"C:\a.umlwiz", @"D:\b.umlwiz"], new RecentProjectsFileStore(file).Load());

        File.WriteAllText(file, "{ not json");
        Assert.Empty(store.Load());
    }

    [Fact]
    public void FormatHeader_NumbersEntries_AndEscapesUnderscores()
    {
        var path = Path.Combine(_folder, "my_shop.umlwiz");

        Assert.StartsWith("_1  my__shop    ", RecentProjectEntry.FormatHeader(0, path));
        Assert.StartsWith("_9  ", RecentProjectEntry.FormatHeader(8, path));
        Assert.StartsWith("1_0  ", RecentProjectEntry.FormatHeader(9, path));
        Assert.Equal(@"C:\abcd…very\deep\folder", RecentProjectEntry.Shorten(@"C:\abcdefghij\very\deep\folder", 24));
        Assert.Equal(24, RecentProjectEntry.Shorten(new string('x', 100), 24).Length);
    }

    [AvaloniaFact]
    public void FileMenu_ShowsRecentProjectsSubmenu()
    {
        var services = new FakeServices();
        var a = Path.Combine(_folder, "Alpha.umlwiz");
        var b = Path.Combine(_folder, "Beta.umlwiz");
        var main = new MainWindowViewModel(services, services, new InMemoryRecentProjectsStore([a, b]));
        var window = new MainWindow { DataContext = main };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var file = window.GetLogicalDescendants().OfType<MenuItem>().Single(m => (string?)m.Header == "_File");
        var recent = file.Items.OfType<MenuItem>().Single(m => (string?)m.Header == "Recent _Projects");
        file.Open();
        Dispatcher.UIThread.RunJobs();
        recent.Open();
        Dispatcher.UIThread.RunJobs();

        Assert.True(recent.IsEffectivelyEnabled);
        var items = recent.GetLogicalChildren().OfType<MenuItem>().ToList();
        Assert.Equal(4, items.Count);
        Assert.StartsWith("_1  Alpha", (string)items[0].Header!);
        Assert.Same(main.OpenRecentProjectCommand, items[0].Command);
        Assert.Equal(a, items[0].CommandParameter);
        Assert.Same(main.ClearRecentProjectsCommand, items[3].Command);

        main.ClearRecentProjectsCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();
        Assert.False(recent.IsEnabled);
        window.Close();
    }

    [AvaloniaFact]
    public void ChoosingRecentProject_OpensIt_AndClosesTheMenu()
    {
        var services = new FakeServices();
        var a = CreateProject("Alpha");
        var b = CreateProject("Beta");
        var main = new MainWindowViewModel(services, services, new InMemoryRecentProjectsStore([a, b]));
        var window = new MainWindow { DataContext = main };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var menu = window.GetLogicalDescendants().OfType<Menu>().Single();
        var file = menu.Items.OfType<MenuItem>().Single(m => (string?)m.Header == "_File");
        var recent = file.Items.OfType<MenuItem>().Single(m => (string?)m.Header == "Recent _Projects");
        file.Open();
        Dispatcher.UIThread.RunJobs();
        recent.Open();
        Dispatcher.UIThread.RunJobs();

        var beta = recent.GetLogicalChildren().OfType<MenuItem>().ToList()[1];
        beta.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(MenuItem.ClickEvent));
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(Path.GetFullPath(b), main.ProjectPath);
        Assert.False(file.IsSubMenuOpen);
        Assert.False(menu.IsOpen);
        window.Close();
    }

    [AvaloniaFact]
    public void RecentProjectsSection_ListsProjects_AndClickOpensOne()
    {
        var services = new FakeServices();
        var shop = CreateProject("Shop");
        var bank = CreateProject("Bank");
        var main = new MainWindowViewModel(services, services, new InMemoryRecentProjectsStore([shop, bank]));
        var window = new MainWindow { DataContext = main };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var section = window.GetLogicalDescendants().OfType<Border>().Single(b => b.Name == "RecentProjectsSection");
        Assert.True(section.IsEffectivelyVisible);
        var texts = section.GetLogicalDescendants().OfType<TextBlock>().Select(t => t.Text).ToList();
        Assert.Contains("Recent Projects", texts);
        Assert.Contains("Shop", texts);
        Assert.Contains("Bank", texts);

        var bankButton = section.GetLogicalDescendants().OfType<Button>().Single(b => Equals(b.CommandParameter, bank));
        bankButton.Command!.Execute(bankButton.CommandParameter);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal("Bank", main.Project?.Name);
        Assert.False(section.IsEffectivelyVisible);

        main.CloseProjectCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();
        Assert.Null(main.Project);
        Assert.True(section.IsEffectivelyVisible);
        window.Close();
    }

    [AvaloniaFact]
    public void WithoutAProject_TheExplorerShowsOnlyRecentProjects_FillingIt()
    {
        var services = new FakeServices();
        var main = new MainWindowViewModel(services, services, new InMemoryRecentProjectsStore([CreateProject("Shop")]));
        var window = new MainWindow { DataContext = main, Width = 1200, Height = 800 };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var explorer = window.GetLogicalDescendants().OfType<Border>().Single(b => b.Name == "ProjectExplorer");
        var section = window.GetLogicalDescendants().OfType<Border>().Single(b => b.Name == "RecentProjectsSection");
        var sectionBounds = new Rect(section.TranslatePoint(default, explorer)!.Value, section.Bounds.Size);

        // No "Project Explorer" title: the recent projects start at the top of the panel and fill it (less its margin).
        Assert.False(window.GetLogicalDescendants().OfType<TextBlock>().Single(t => t.Name == "ExplorerTitle").IsEffectivelyVisible);
        Assert.InRange(sectionBounds.Top, 0, 12);
        Assert.InRange(explorer.Bounds.Height - sectionBounds.Bottom, 0, 10);
        Assert.InRange(sectionBounds.Width, explorer.Bounds.Width - 20, explorer.Bounds.Width);

        // No buttons in the explorer but the recent projects: creating, opening and importing are on the start screen.
        var buttons = explorer.GetLogicalDescendants().OfType<Button>().Where(b => b.IsEffectivelyVisible).ToList();
        Assert.Single(buttons);
        Assert.Same(main.OpenRecentProjectCommand, buttons[0].Command);
        Assert.False(window.GetLogicalDescendants().OfType<TextBlock>().Single(t => t.Name == "NoRecentProjects").IsEffectivelyVisible);
        window.Close();
    }

    [AvaloniaFact]
    public void WithoutRecentProjects_TheExplorerSaysSo()
    {
        var services = new FakeServices();
        var window = new MainWindow { DataContext = new MainWindowViewModel(services, services, new InMemoryRecentProjectsStore()) };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        Assert.True(window.GetLogicalDescendants().OfType<Border>().Single(b => b.Name == "RecentProjectsSection").IsEffectivelyVisible);
        var none = window.GetLogicalDescendants().OfType<TextBlock>().Single(t => t.Name == "NoRecentProjects");
        Assert.True(none.IsEffectivelyVisible);
        Assert.StartsWith("No recent projects", none.Text);
        window.Close();
    }

    [AvaloniaFact]
    public async Task DeleteRecentProject_AfterConfirming_DeletesTheFileForGood_AndForgetsIt()
    {
        var path = CreateProject("Shop");
        var other = CreateProject("Bank");
        var services = new FakeServices { ConfirmAnswer = ConfirmResult.Yes };
        var states = new InMemoryProjectStateStore();
        states.Save(path, new ProjectUserState { OpenDiagramIds = ["a"] });
        var main = new MainWindowViewModel(services, services, new InMemoryRecentProjectsStore([path, other]), projectStates: states);

        await main.DeleteRecentProjectCommand.ExecuteAsync(path);

        Assert.Equal(
            $"Delete the project 'Shop' permanently?\n{path}\n\n"
            + "The file is deleted for good, not moved to the Recycle Bin or Trash. This cannot be undone, and the file cannot be restored.",
            Assert.Single(services.ConfirmMessages).ReplaceLineEndings("\n"));
        Assert.False(File.Exists(path));
        Assert.True(File.Exists(other));
        Assert.Equal([other], main.RecentProjects);
        Assert.Null(states.Load(path));
    }

    [AvaloniaFact]
    public async Task DeleteRecentProject_WhenNotConfirmed_KeepsEverything()
    {
        var path = CreateProject("Shop");
        var services = new FakeServices { ConfirmAnswer = ConfirmResult.No };
        var main = new MainWindowViewModel(services, services, new InMemoryRecentProjectsStore([path]));

        await main.DeleteRecentProjectCommand.ExecuteAsync(path);

        Assert.True(File.Exists(path));
        Assert.Equal([path], main.RecentProjects);
    }

    [AvaloniaFact]
    public async Task DeleteRecentProject_OfAFileAlreadyGone_OnlyRemovesItFromTheList()
    {
        var path = Path.Combine(_folder, "Gone" + UmlProject.FileExtension);
        var services = new FakeServices();
        var main = new MainWindowViewModel(services, services, new InMemoryRecentProjectsStore([path]));

        await main.DeleteRecentProjectCommand.ExecuteAsync(path);

        Assert.Empty(services.ConfirmMessages);
        Assert.Empty(main.RecentProjects);
    }

    [AvaloniaFact]
    public void ARecentProjectOnTheStartScreen_HasDeleteProjectFile_OnItsRightClickMenu()
    {
        var path = CreateProject("Shop");
        var services = new FakeServices();
        var main = new MainWindowViewModel(services, services, new InMemoryRecentProjectsStore([path]));
        var window = new MainWindow { DataContext = main };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        var button = window.GetLogicalDescendants().OfType<Button>().Single(b => b.CommandParameter as string == path && b.ContextMenu is not null);

        button.ContextMenu!.Open(button);
        Dispatcher.UIThread.RunJobs();

        var item = button.ContextMenu.Items.OfType<MenuItem>().Single(i => i.Name == "DeleteRecentProjectMenuItem");
        Assert.Equal("Delete Project File…", item.Header);
        Assert.Same(main.DeleteRecentProjectCommand, item.Command);
        Assert.Equal(path, item.CommandParameter);
        button.ContextMenu.Close();
        window.Close();
    }

    [AvaloniaFact]
    public async Task CopyRecentProject_CopiesItNextToTheOriginal_AsNameCopy_WithoutOpeningIt()
    {
        var path = CreateProject("Shop");
        var original = ProjectSerializer.Load(path);
        original.Version = "1.0";
        original.Diagrams.Add(DiagramDocument.Create("Domain", DiagramType.Class));
        ProjectSerializer.Save(original, path);
        var before = File.ReadAllText(path);
        var services = new FakeServices();
        var states = new InMemoryProjectStateStore();
        states.Save(path, new ProjectUserState { OpenDiagramIds = [original.Diagrams[0].Id], ExpandedFolderIds = ["$project"] });
        var main = new MainWindowViewModel(services, services, new InMemoryRecentProjectsStore([path]), projectStates: states);

        await main.CopyRecentProjectCommand.ExecuteAsync(path);

        var copyPath = Path.Combine(_folder, "Shop (copy)" + UmlProject.FileExtension);
        var copy = ProjectSerializer.Load(copyPath);
        Assert.Equal("Shop (copy)", copy.Name);
        Assert.Equal("1.0", copy.Version);
        Assert.Equal(original.Diagrams.Select(d => (d.Id, d.Name)), copy.Diagrams.Select(d => (d.Id, d.Name)));
        Assert.Equal(before, File.ReadAllText(path));
        Assert.Equal([copyPath, path], main.RecentProjects);
        Assert.Null(main.Project);
        Assert.Equal([original.Diagrams[0].Id], states.Load(copyPath)!.OpenDiagramIds);
        Assert.Equal(["$project"], states.Load(copyPath)!.ExpandedFolderIds);
        Assert.Empty(services.Errors);
    }

    [AvaloniaFact]
    public async Task CopyingAgain_NumbersTheCopies()
    {
        var path = CreateProject("Shop");
        var services = new FakeServices();
        var main = new MainWindowViewModel(services, services, new InMemoryRecentProjectsStore([path]));

        await main.CopyRecentProjectCommand.ExecuteAsync(path);
        await main.CopyRecentProjectCommand.ExecuteAsync(path);

        var second = Path.Combine(_folder, "Shop (copy 2)" + UmlProject.FileExtension);
        Assert.Equal("Shop (copy 2)", ProjectSerializer.Load(second).Name);
        Assert.Equal(second, main.RecentProjects[0]);
    }

    [AvaloniaFact]
    public async Task CopyRecentProject_OfAFileThatIsGone_OffersToRemoveItFromTheList()
    {
        var path = Path.Combine(_folder, "Gone" + UmlProject.FileExtension);
        var services = new FakeServices { ConfirmAnswer = ConfirmResult.Yes };
        var main = new MainWindowViewModel(services, services, new InMemoryRecentProjectsStore([path]));

        await main.CopyRecentProjectCommand.ExecuteAsync(path);

        Assert.Single(services.ConfirmMessages);
        Assert.Empty(main.RecentProjects);
        Assert.Empty(Directory.GetFiles(_folder));
    }

    [AvaloniaFact]
    public void ARecentProjectOnTheStartScreen_HasCopyProject_OnItsRightClickMenu()
    {
        var path = CreateProject("Shop");
        var services = new FakeServices();
        var main = new MainWindowViewModel(services, services, new InMemoryRecentProjectsStore([path]));
        var window = new MainWindow { DataContext = main };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        var button = window.GetLogicalDescendants().OfType<Button>().Single(b => b.CommandParameter as string == path && b.ContextMenu is not null);

        button.ContextMenu!.Open(button);
        Dispatcher.UIThread.RunJobs();

        var items = button.ContextMenu.Items.OfType<MenuItem>().ToList();
        Assert.Equal(["Copy Project", "Delete Project File…"], items.Select(i => (string?)i.Header));
        Assert.Same(main.CopyRecentProjectCommand, items[0].Command);
        Assert.Equal(path, items[0].CommandParameter);
        button.ContextMenu.Close();
        window.Close();
    }
}
