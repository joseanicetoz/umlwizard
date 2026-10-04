using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using UmlWizard.App.ViewModels;
using UmlWizard.App.Views;

namespace UmlWizard.App.Tests;

public sealed class TabSelectionSyncTests
{
    private static (DiagramViewModel A, DiagramViewModel B) OpenAandB(ExplorerFixture f)
    {
        f.Main.OpenDiagramCommand.Execute((DiagramItemViewModel)f.Node("A"));
        f.Main.OpenDiagramCommand.Execute((DiagramItemViewModel)f.Node("B"));
        return (f.Main.OpenDiagrams[0], f.Main.OpenDiagrams[1]);
    }

    [AvaloniaFact]
    public void ActivatingATab_SelectsItsDiagram_AndExpandsItsFolders()
    {
        using var f = new ExplorerFixture();
        var (a, _) = OpenAandB(f);
        var model = (FolderNodeViewModel)f.Node("Model");
        model.IsExpanded = false;

        f.Main.ActiveDiagram = a;

        Assert.Same(f.Node("A"), f.Main.SelectedNode);
        Assert.True(model.IsExpanded);
    }

    [AvaloniaFact]
    public void SelectingAnOpenDiagram_ActivatesItsTab()
    {
        using var f = new ExplorerFixture();
        var (a, b) = OpenAandB(f);
        Assert.Same(b, f.Main.ActiveDiagram);

        f.Main.SelectedNode = f.Node("A");

        Assert.Same(a, f.Main.ActiveDiagram);
        Assert.Equal(2, f.Main.OpenDiagrams.Count);
    }

    [AvaloniaFact]
    public void SelectingAClosedDiagram_DeselectsTheTabs_WithoutOpeningOrClosingAny()
    {
        using var f = new ExplorerFixture();
        f.Main.OpenDiagramCommand.Execute((DiagramItemViewModel)f.Node("B"));

        f.Main.SelectedNode = f.Node("A");

        Assert.Null(f.Main.ActiveDiagram);
        Assert.False(f.Main.HasActiveDiagram);
        Assert.Same(f.Node("A"), f.Main.SelectedNode);
        Assert.Equal(["B"], f.Main.OpenDiagrams.Select(d => d.Name));
        Assert.StartsWith("This diagram is not open", f.Main.PlaceholderText);
        Assert.False(f.Main.UndoCommand.CanExecute(null));

        // Selecting the open diagram again brings its tab back.
        f.Main.SelectedNode = f.Node("B");
        Assert.Equal("B", f.Main.ActiveDiagram?.Name);
    }

    [AvaloniaFact]
    public void SelectingAFolder_KeepsTheActiveTab()
    {
        using var f = new ExplorerFixture();
        f.Main.OpenDiagramCommand.Execute((DiagramItemViewModel)f.Node("B"));
        var b = f.Main.ActiveDiagram;

        f.Main.SelectedNode = f.Node("Model");

        Assert.Same(b, f.Main.ActiveDiagram);
    }

    [AvaloniaFact]
    public void InTheWindow_SelectingAClosedDiagram_LeavesNoTabSelected_AndDisablesTheDiagramToolbar()
    {
        using var f = new ExplorerFixture();
        var window = new MainWindow { DataContext = f.Main };
        window.Show();
        f.Main.OpenDiagramCommand.Execute((DiagramItemViewModel)f.Node("B"));
        Dispatcher.UIThread.RunJobs();
        var tabs = window.FindControl<TabControl>("DocumentTabs")!;
        var tree = window.FindControl<TreeView>("DiagramTree")!;
        var toolbar = window.GetLogicalDescendants().OfType<DiagramToolbar>().Single();
        Assert.Single(window.GetLogicalDescendants().OfType<DrawingToolbar>());
        Assert.True(toolbar.IsEffectivelyEnabled);

        tree.SelectedItem = f.Node("A");
        Dispatcher.UIThread.RunJobs();

        // The tab strip must not pick a tab on its own, which would move the explorer back to B.
        Assert.Null(tabs.SelectedItem);
        Assert.Null(f.Main.ActiveDiagram);
        Assert.Same(f.Node("A"), tree.SelectedItem);
        Assert.True(tabs.IsVisible);
        Assert.False(toolbar.IsEffectivelyEnabled);
        Assert.Empty(window.GetLogicalDescendants().OfType<DrawingToolbar>());
        Assert.True(window.FindControl<Button>("NewProjectButton")!.IsEffectivelyEnabled);

        // Clicking the tab again selects it and its diagram.
        tabs.SelectedIndex = 0;
        Dispatcher.UIThread.RunJobs();
        Assert.Same(f.Node("B"), tree.SelectedItem);
        Assert.True(toolbar.IsEffectivelyEnabled);
        window.Close();
    }

    [AvaloniaFact]
    public void ClosingTheActiveTab_SelectsTheDiagramOfTheNextActiveTab()
    {
        using var f = new ExplorerFixture();
        var (_, b) = OpenAandB(f);

        b.CloseCommand.Execute(null);

        Assert.Equal("A", f.Main.ActiveDiagram?.Name);
        Assert.Same(f.Node("A"), f.Main.SelectedNode);
    }

    [AvaloniaFact]
    public void InTheWindow_ClickingATab_SelectsTheTreeItem_AndSelectingTheTreeItem_ActivatesTheTab()
    {
        using var f = new ExplorerFixture();
        var window = new MainWindow { DataContext = f.Main };
        window.Show();
        OpenAandB(f);
        Dispatcher.UIThread.RunJobs();
        var tabs = window.FindControl<TabControl>("DocumentTabs")!;
        var tree = window.FindControl<TreeView>("DiagramTree")!;

        tabs.SelectedIndex = 0;
        Dispatcher.UIThread.RunJobs();
        Assert.Same(f.Node("A"), tree.SelectedItem);

        tree.SelectedItem = f.Node("B");
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("B", ((DiagramViewModel)tabs.SelectedItem!).Name);
        window.Close();
    }
}
