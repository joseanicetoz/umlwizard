using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using UmlWizard.App.Services;
using UmlWizard.App.ViewModels;
using UmlWizard.App.ViewModels.Dialogs;
using UmlWizard.App.Views;
using UmlWizard.Core.Model;
using UmlWizard.Core.Serialization;

namespace UmlWizard.App.Tests;

public sealed class ProjectExplorerTests
{
    [AvaloniaFact]
    public void OpeningProject_BuildsTree_FoldersFirst()
    {
        using var f = new ExplorerFixture();

        Assert.Equal(["Model", "B"], f.Main.ProjectTree.Select(n => n.Name));
        var model = Assert.IsType<FolderNodeViewModel>(f.Main.ProjectTree[0]);
        Assert.True(model.IsExpanded);
        Assert.Equal(["Nested", "A"], model.Children.Select(n => n.Name));
        Assert.Same(model, model.Children[1].Parent);
        Assert.Same(f.Main.ProjectRoot, f.Main.SelectedNode);
        Assert.False(f.Main.IsProjectEmpty);
    }

    [AvaloniaFact]
    public void ProjectRoot_IsNamedAfterProject_AndHoldsTopLevelNodes()
    {
        using var f = new ExplorerFixture();

        var root = Assert.Single(f.Main.ExplorerNodes);
        Assert.Same(f.Main.ProjectRoot, root);
        Assert.Equal("Shop", root.Name);
        Assert.True(root.IsExpanded);
        Assert.Same(f.Main.ProjectRoot!.Children, f.Main.ProjectTree);
        Assert.All(f.Main.ProjectTree, n => Assert.Null(n.Parent));
    }

    [AvaloniaFact]
    public async Task RenamingProjectRoot_RenamesProject_AndSaves()
    {
        using var f = new ExplorerFixture();
        f.Main.SelectedNode = f.Main.ProjectRoot;
        f.Services.FillDialog = d => ((TextInputViewModel)d).Text = "Store";

        await f.Main.RenameItemCommand.ExecuteAsync(null);

        Assert.Equal("Store", f.Main.ProjectRoot?.Name);
        Assert.Equal("Store", f.Saved.Name);
        Assert.Contains("Store", f.Main.WindowTitle);
    }

    [AvaloniaFact]
    public async Task NewFolder_WithProjectRootSelected_IsCreatedAtTopLevel()
    {
        using var f = new ExplorerFixture();
        f.Main.SelectedNode = f.Main.ProjectRoot;
        f.Services.FillDialog = d => ((TextInputViewModel)d).Text = "Payments";

        await f.Main.NewFolderCommand.ExecuteAsync(null);

        Assert.Null(f.Main.SelectedNode?.Parent);
        Assert.Equal(["Model", "Payments"], f.Saved.Folders.Select(x => x.Name));
    }

    [AvaloniaFact]
    public async Task NewFolder_IsCreatedInsideSelectedFolder_AndSaved()
    {
        using var f = new ExplorerFixture();
        f.Main.SelectedNode = f.Node("A");
        f.Services.FillDialog = d => ((TextInputViewModel)d).Text = "Payments";

        await f.Main.NewFolderCommand.ExecuteAsync(null);

        Assert.Equal("Payments", f.Main.SelectedNode?.Name);
        Assert.Equal("Model", f.Main.SelectedNode?.Parent?.Name);
        Assert.Equal(["Nested", "Payments"], f.Saved.Folders[0].Folders.Select(x => x.Name));
    }

    [AvaloniaFact]
    public async Task NewDiagram_GoesIntoFolderOfSelection_AndOpens()
    {
        using var f = new ExplorerFixture();
        f.Main.SelectedNode = f.Node("Nested");
        f.Services.FillDialog = d => ((NewDiagramViewModel)d).Name = "Orders";

        await f.Main.NewClassDiagramCommand.ExecuteAsync(null);

        var node = Assert.IsType<DiagramItemViewModel>(f.Main.SelectedNode);
        Assert.Equal("Orders", node.Name);
        Assert.Equal("Nested", node.Parent?.Name);
        Assert.Equal("Orders", f.Main.ActiveDiagram?.Name);
        var saved = f.Saved;
        Assert.Equal("Orders", saved.FindDiagram(Assert.Single(saved.FindFolder(node.Parent!.Id)!.DiagramIds))!.Name);
    }

    [AvaloniaFact]
    public async Task MoveItem_MovesDiagramToChosenFolder()
    {
        using var f = new ExplorerFixture();
        f.Main.SelectedNode = f.Node("B");
        f.Services.FillDialog = d =>
        {
            var move = (MoveToViewModel)d;
            Assert.Equal("(Top level)", move.Selected?.Label);
            move.Selected = move.Targets.Single(t => t.Label == "Nested");
        };

        await f.Main.MoveItemCommand.ExecuteAsync(null);

        Assert.Equal("Nested", f.Node("B").Parent?.Name);
        Assert.Same(f.Node("B"), f.Main.SelectedNode);
        Assert.Empty(f.Saved.TopLevelDiagrams());
    }

    [AvaloniaFact]
    public async Task SaveProject_WritesChangesToProjectFile()
    {
        using var f = new ExplorerFixture();
        f.Main.OpenDiagramCommand.Execute((DiagramItemViewModel)f.Node("A"));
        var diagram = f.Main.ActiveDiagram!;
        diagram.Editor.AddClass(ClassifierKind.Class, 100, 100);
        Assert.True(diagram.IsDirty);

        await f.Main.SaveProjectCommand.ExecuteAsync(null);

        Assert.False(diagram.IsDirty);
        var saved = (ClassDiagramModel)f.Saved.FindDiagram(diagram.Id)!.Model;
        Assert.Single(saved.Classes);
    }

    [AvaloniaFact]
    public async Task SaveProjectAs_WritesNewFile_WithUnsavedChanges_AndSwitchesToIt()
    {
        using var f = new ExplorerFixture();
        f.Main.OpenDiagramCommand.Execute((DiagramItemViewModel)f.Node("A"));
        var diagram = f.Main.ActiveDiagram!;
        diagram.Editor.AddClass(ClassifierKind.Class, 100, 100);
        var copy = Path.Combine(f.Folder, "Copy.umlwiz");
        f.Services.SaveFilePath = copy;

        await f.Main.SaveProjectAsCommand.ExecuteAsync(null);

        Assert.Equal(copy, f.Main.ProjectPath);
        Assert.Equal(copy, f.Main.RecentProjects[0]);
        Assert.False(diagram.IsDirty);
        Assert.Single(((ClassDiagramModel)ProjectSerializer.Load(copy).FindDiagram(diagram.Id)!.Model).Classes);

        // The original file keeps its last saved state.
        Assert.Empty(((ClassDiagramModel)f.Saved.FindDiagram(diagram.Id)!.Model).Classes);
    }

    [AvaloniaFact]
    public async Task SaveProjectAs_Cancelled_ChangesNothing()
    {
        using var f = new ExplorerFixture();
        f.Main.OpenDiagramCommand.Execute((DiagramItemViewModel)f.Node("A"));
        f.Main.ActiveDiagram!.Editor.AddClass(ClassifierKind.Class, 100, 100);

        await f.Main.SaveProjectAsCommand.ExecuteAsync(null);

        Assert.Equal(f.Path, f.Main.ProjectPath);
        Assert.True(f.Main.ActiveDiagram.IsDirty);
    }

    [AvaloniaFact]
    public async Task MoveNode_DropsDiagramIntoFolder_AndBackToTopLevel()
    {
        using var f = new ExplorerFixture();

        Assert.True(await f.Main.MoveNodeAsync(f.Node("B"), f.Node("Nested").Id));

        Assert.Equal("Nested", f.Node("B").Parent?.Name);
        Assert.Same(f.Node("B"), f.Main.SelectedNode);
        Assert.Empty(f.Saved.TopLevelDiagrams());

        Assert.True(await f.Main.MoveNodeAsync(f.Node("B"), null));

        Assert.Null(f.Node("B").Parent);
        Assert.Equal(["B"], f.Saved.TopLevelDiagrams().Select(d => d.Name));
    }

    [AvaloniaFact]
    public async Task MoveDiagram_InsertsAtPosition_AndReordersWithinFolder()
    {
        using var f = new ExplorerFixture();
        var model = (FolderNodeViewModel)f.Node("Model");
        var a = (DiagramItemViewModel)f.Node("A");

        Assert.False(f.Main.CanMoveDiagram(a, model.Id, 0));
        Assert.True(await f.Main.MoveDiagramAsync((DiagramItemViewModel)f.Node("B"), model.Id, 0));

        Assert.Equal(["B", "A"], f.Main.DiagramsIn((FolderNodeViewModel)f.Node("Model")).Select(d => d.Name));
        Assert.Equal("B", f.Main.SelectedNode?.Name);
        var saved = f.Saved;
        Assert.Equal(["B", "A"], saved.Folders[0].DiagramIds.Select(id => saved.FindDiagram(id)!.Name));

        Assert.True(await f.Main.MoveDiagramAsync((DiagramItemViewModel)f.Node("A"), f.Node("Model").Id, 0));

        Assert.Equal(["A", "B"], f.Main.DiagramsIn((FolderNodeViewModel)f.Node("Model")).Select(d => d.Name));
        saved = f.Saved;
        Assert.Equal(["A", "B"], saved.Folders[0].DiagramIds.Select(id => saved.FindDiagram(id)!.Name));
    }

    [AvaloniaFact]
    public async Task MoveDiagram_ReordersTopLevel()
    {
        using var f = new ExplorerFixture();

        Assert.True(await f.Main.MoveDiagramAsync((DiagramItemViewModel)f.Node("A"), null, 0));

        Assert.Equal(["Model", "A", "B"], f.Main.ProjectTree.Select(n => n.Name));
        Assert.Equal(["A", "B"], f.Saved.TopLevelDiagrams().Select(d => d.Name));

        var b = (DiagramItemViewModel)f.Node("B");
        Assert.False(f.Main.CanMoveDiagram(b, null, 1));
        Assert.True(await f.Main.MoveDiagramAsync(b, null, 0));
        Assert.Equal(["B", "A"], f.Saved.TopLevelDiagrams().Select(d => d.Name));
    }

    [AvaloniaFact]
    public async Task MoveNode_RejectsInvalidDrops()
    {
        using var f = new ExplorerFixture();
        var model = f.Node("Model");
        var nested = f.Node("Nested");

        Assert.False(f.Main.CanMoveNode(f.Node("A"), model.Id));
        Assert.False(f.Main.CanMoveNode(model, model.Id));
        Assert.False(f.Main.CanMoveNode(model, nested.Id));
        Assert.True(f.Main.CanMoveNode(nested, null));
        Assert.False(await f.Main.MoveNodeAsync(model, nested.Id));
        Assert.Equal(["Model"], f.Saved.Folders.Select(x => x.Name));
    }

    [AvaloniaFact]
    public async Task MoveItem_ForFolder_OffersOnlyValidDestinations()
    {
        using var f = new ExplorerFixture();
        f.Main.SelectedNode = f.Node("Model");
        f.Services.AcceptDialogs = false;

        await f.Main.MoveItemCommand.ExecuteAsync(null);

        var move = Assert.IsType<MoveToViewModel>(Assert.Single(f.Services.ShownDialogs));
        Assert.Equal(["(Top level)"], move.Targets.Select(t => t.Label));
    }

    [AvaloniaFact]
    public async Task DeleteFolder_MovesContentsUp_AndKeepsDiagrams()
    {
        using var f = new ExplorerFixture();
        f.Main.SelectedNode = f.Node("Model");
        f.Services.ConfirmAnswer = ConfirmResult.Yes;

        await f.Main.DeleteItemCommand.ExecuteAsync(null);

        Assert.Equal(["Nested", "A", "B"], f.Main.ProjectTree.Select(n => n.Name));
        var saved = f.Saved;
        Assert.Equal(["Nested"], saved.Folders.Select(x => x.Name));
        Assert.Equal(2, saved.Diagrams.Count);
        Assert.Contains("moved to the top level", Assert.Single(f.Services.ConfirmMessages));
    }

    [AvaloniaFact]
    public async Task DeleteDiagram_RemovesItFromFolderAndProject()
    {
        using var f = new ExplorerFixture();
        f.Main.SelectedNode = f.Node("A");
        f.Main.OpenDiagramCommand.Execute(null);
        f.Services.ConfirmAnswer = ConfirmResult.Yes;

        await f.Main.DeleteItemCommand.ExecuteAsync(null);

        Assert.DoesNotContain(f.Main.AllNodes(), n => n.Name == "A");
        Assert.Empty(f.Main.OpenDiagrams);
        Assert.Same(f.Node("Model"), f.Main.SelectedNode);
        Assert.Empty(f.Saved.Folders[0].DiagramIds);
    }

    [AvaloniaFact]
    public async Task RenameFolder_IsSaved_AndKeepsCollapsedFoldersCollapsed()
    {
        using var f = new ExplorerFixture();
        var model = f.Node("Model");
        model.IsExpanded = false;
        f.Main.SelectedNode = model;
        f.Services.FillDialog = d => ((TextInputViewModel)d).Text = "Domain";

        await f.Main.RenameItemCommand.ExecuteAsync(null);

        Assert.False(f.Node("Domain").IsExpanded);
        Assert.Equal("Domain", f.Saved.Folders[0].Name);
    }

    [AvaloniaFact]
    public void ProjectExplorer_IsTitled_OnlyWithAProject()
    {
        // Without a project the panel only lists the recent projects, under their own heading.
        var services = new FakeServices();
        var window = new MainWindow { DataContext = new MainWindowViewModel(services, services) };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        var texts = VisibleTexts(window.FindControl<Border>("ProjectExplorer")!);
        Assert.DoesNotContain("Project Explorer", texts);
        Assert.Equal("Recent Projects", texts.First());
        window.Close();

        using var f = new ExplorerFixture();
        window = new MainWindow { DataContext = f.Main };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        Assert.Contains("Project Explorer", VisibleTexts(window.FindControl<Border>("ProjectExplorer")!));
        window.Close();
    }

    [AvaloniaFact]
    public void ViewMenu_TogglesProjectExplorer_AndItsColumnKeepsItsWidth()
    {
        using var f = new ExplorerFixture();
        var window = new MainWindow { DataContext = f.Main };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        var panel = window.FindControl<Border>("ProjectExplorer")!;
        var column = window.FindControl<Grid>("Workspace")!.ColumnDefinitions[0];
        column.Width = new GridLength(320); // as if resized with the splitter
        var item = MenuItems(window).Single(m => (string?)m.Header == "_Project Explorer");
        Assert.True(item.IsChecked);

        item.IsChecked = false;
        Dispatcher.UIThread.RunJobs();

        Assert.False(f.Main.IsProjectExplorerVisible);
        Assert.False(panel.IsVisible);
        Assert.Equal(0, column.Width.Value);
        Assert.False(window.GetVisualDescendants().OfType<GridSplitter>().Single(s => Grid.GetColumn(s) == 0).IsVisible);

        f.Main.IsProjectExplorerVisible = true;
        Dispatcher.UIThread.RunJobs();

        Assert.True(item.IsChecked);
        Assert.True(panel.IsVisible);
        Assert.Equal(320, column.Width.Value);
        window.Close();
    }

    [AvaloniaFact]
    public void CtrlAltL_TogglesProjectExplorer()
    {
        using var f = new ExplorerFixture();
        var window = new MainWindow { DataContext = f.Main };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        var item = MenuItems(window).Single(m => (string?)m.Header == "_Project Explorer");
        Assert.Equal(Shortcuts.ToggleProjectExplorer, item.InputGesture);

        var modifiers = RawInputModifiers.Alt |
            (OperatingSystem.IsMacOS() ? RawInputModifiers.Meta : RawInputModifiers.Control);
        window.KeyPressQwerty(PhysicalKey.L, modifiers);
        window.KeyReleaseQwerty(PhysicalKey.L, modifiers);
        Dispatcher.UIThread.RunJobs();
        Assert.False(f.Main.IsProjectExplorerVisible);
        Assert.False(window.FindControl<Border>("ProjectExplorer")!.IsVisible);

        window.KeyPressQwerty(PhysicalKey.L, modifiers);
        window.KeyReleaseQwerty(PhysicalKey.L, modifiers);
        Dispatcher.UIThread.RunJobs();
        Assert.True(f.Main.IsProjectExplorerVisible);
        window.Close();
    }

    [AvaloniaFact]
    public void ProjectExplorer_Visibility_IsSaved_AndRestoredAtStartUp()
    {
        var services = new FakeServices();
        var store = new InMemorySettingsStore();
        var main = new MainWindowViewModel(services, services, settings: store);
        Assert.True(main.IsProjectExplorerVisible);

        main.IsProjectExplorerVisible = false;
        Assert.False(store.Load().ShowProjectExplorer);

        var window = new MainWindow { DataContext = new MainWindowViewModel(services, services, settings: store) };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        Assert.False(window.FindControl<Border>("ProjectExplorer")!.IsVisible);
        Assert.Equal(0, window.FindControl<Grid>("Workspace")!.ColumnDefinitions[0].Width.Value);
        window.Close();
    }

    private static List<string?> VisibleTexts(Visual root) =>
        root.GetVisualDescendants().OfType<TextBlock>().Where(t => t.IsEffectivelyVisible).Select(t => t.Text).ToList();

    private static IEnumerable<MenuItem> MenuItems(MainWindow window) =>
        window.FindControl<Menu>("MainMenu")!.Items.OfType<MenuItem>().SelectMany(m => m.Items.OfType<MenuItem>());

    [AvaloniaFact]
    public void ExplorerPanel_ShowsTree_UnderProjectName_WithoutAuthor()
    {
        using var f = new ExplorerFixture();
        var window = new MainWindow { DataContext = f.Main };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var tree = window.GetVisualDescendants().OfType<TreeView>().Single(t => t.Name == "DiagramTree");
        var texts = ((Visual)tree.Parent!).GetVisualDescendants().OfType<TextBlock>().Where(t => t.IsEffectivelyVisible)
            .Select(t => t.Text).ToList();

        var root = Assert.Single(tree.GetVisualDescendants().OfType<TreeViewItem>(), i => i.Parent == tree);
        Assert.IsType<ProjectRootNodeViewModel>(root.DataContext);
        Assert.Contains("Shop", texts);
        Assert.Contains("Model", texts);
        Assert.Contains("Nested", texts);
        Assert.DoesNotContain(texts, t => t?.Contains("Ada") == true);
        window.Close();
    }

    [AvaloniaFact]
    public async Task CopyDiagram_AddsACopyNamedCopy_BelowTheOriginal_InTheSameFolder_AndOpensIt()
    {
        using var f = new ExplorerFixture();
        var a = f.Main.Project!.Diagrams.Single(d => d.Name == "A");
        a.Description = "The domain";
        ((ClassDiagramModel)a.Model).Classes.Add(new UmlClass { Name = "Order" });
        f.Main.SelectedNode = f.Node("A");

        await f.Main.CopyDiagramCommand.ExecuteAsync(null);

        var saved = f.Saved;
        var copy = saved.Diagrams.Single(d => d.Name == "A (copy)");
        Assert.NotEqual(a.Id, copy.Id);
        Assert.Equal(DiagramType.Class, copy.DiagramType);
        Assert.Equal("The domain", copy.Description);
        Assert.Equal(["Order"], ((ClassDiagramModel)copy.Model).Classes.Select(c => c.Name));
        Assert.Equal([a.Id, copy.Id], saved.Folders.Single(x => x.Name == "Model").DiagramIds);
        Assert.Equal(copy.Id, f.Main.SelectedNode?.Id);
        Assert.Equal("A (copy)", f.Main.ActiveDiagram?.Name);
    }

    [AvaloniaFact]
    public async Task CopyDiagram_AtTheTopLevel_PutsTheCopyBelowTheOriginal()
    {
        using var f = new ExplorerFixture();
        f.Main.SelectedNode = f.Node("B");

        await f.Main.CopyDiagramCommand.ExecuteAsync(null);

        Assert.Equal(["B", "B (copy)"], f.Saved.TopLevelDiagrams().Select(d => d.Name));
    }

    [AvaloniaFact]
    public async Task CopyDiagram_CopiesTheDiagramAsSaved_NotItsUnsavedChanges()
    {
        using var f = new ExplorerFixture();
        f.Main.OpenDiagramCommand.Execute((DiagramItemViewModel)f.Node("A"));
        f.Main.ActiveDiagram!.Editor.AddClass(ClassifierKind.Class, 100, 100);
        f.Main.SelectedNode = f.Node("A");

        await f.Main.CopyDiagramCommand.ExecuteAsync(null);

        Assert.Empty(((ClassDiagramModel)f.Saved.Diagrams.Single(d => d.Name == "A (copy)").Model).Classes);
        Assert.True(f.Main.OpenDiagrams.Single(d => d.Name == "A").IsDirty);
    }
}
