using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using UmlWizard.App.Services;
using UmlWizard.App.ViewModels;
using UmlWizard.App.Views;
using UmlWizard.Core.Model;

namespace UmlWizard.App.Tests;

public sealed class WindowMenuTests
{
    private static (MainWindowViewModel Main, FakeServices Services, DiagramHarness[] Tabs) OpenTabs(int count)
    {
        var tabs = Enumerable.Range(0, count).Select(_ => new DiagramHarness(DiagramType.Class)).ToArray();
        var services = tabs[0].Services;
        var main = new MainWindowViewModel(services, services);
        foreach (var tab in tabs)
        {
            main.OpenDiagrams.Add(tab.ViewModel);
        }

        main.ActiveDiagram = tabs[^1].ViewModel;
        return (main, services, tabs);
    }

    [AvaloniaFact]
    public void CloseAllTabs_ClosesEveryTab_WithoutAskingWhenNothingChanged()
    {
        var (main, services, _) = OpenTabs(3);
        Assert.True(main.CloseAllTabsCommand.CanExecute(null));

        main.CloseAllTabsCommand.Execute(null);

        Assert.Empty(main.OpenDiagrams);
        Assert.Null(main.ActiveDiagram);
        Assert.False(main.HasOpenDiagrams);
        Assert.False(main.CloseAllTabsCommand.CanExecute(null));
        Assert.Empty(services.ConfirmMessages);
    }

    [AvaloniaFact]
    public void CloseAllTabs_WithUnsavedChanges_CancelKeepsTabsOpen()
    {
        var (main, services, tabs) = OpenTabs(2);
        tabs[0].ViewModel.Editor.AddClass(ClassifierKind.Class, 0, 0);
        services.ConfirmAnswer = ConfirmResult.Cancel;

        main.CloseAllTabsCommand.Execute(null);

        Assert.Equal(2, main.OpenDiagrams.Count);
        Assert.Same(tabs[1].ViewModel, main.ActiveDiagram);
        Assert.Contains(tabs[0].ViewModel.Name, Assert.Single(services.ConfirmMessages));
    }

    [AvaloniaFact]
    public void CloseAllTabs_WithUnsavedChanges_DiscardClosesTabs()
    {
        var (main, services, tabs) = OpenTabs(2);
        tabs[1].ViewModel.Editor.AddClass(ClassifierKind.Class, 0, 0);
        services.ConfirmAnswer = ConfirmResult.No;

        main.CloseAllTabsCommand.Execute(null);

        Assert.Empty(main.OpenDiagrams);
        Assert.Null(main.ActiveDiagram);
    }

    [AvaloniaFact]
    public void DiagramMenu_HasCloseAllDiagrams_EnabledOnlyWithOpenDiagrams_AndThereIsNoWindowMenu()
    {
        using var f = new ExplorerFixture();
        var window = new MainWindow { DataContext = f.Main };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var topMenus = window.GetLogicalDescendants().OfType<Menu>().Single().Items.OfType<MenuItem>().ToList();
        Assert.DoesNotContain(topMenus, m => (string?)m.Header == "_Window");
        var menu = topMenus.Single(m => (string?)m.Header == "_Diagram");
        var closeAll = menu.Items.OfType<MenuItem>().Single(m => (string?)m.Header == "Close _All Diagrams");
        Assert.Same(f.Main.CloseAllTabsCommand, closeAll.Command);

        menu.Open();
        Dispatcher.UIThread.RunJobs();
        Assert.False(closeAll.IsEffectivelyEnabled);

        f.Main.OpenDiagramCommand.Execute((DiagramItemViewModel)f.Node("A"));
        Dispatcher.UIThread.RunJobs();
        Assert.True(closeAll.IsEffectivelyEnabled);

        closeAll.Command!.Execute(null);
        Dispatcher.UIThread.RunJobs();
        Assert.Empty(f.Main.OpenDiagrams);
        window.Close();
    }

    [AvaloniaFact]
    public void CloseAllTabs_ClearsTheDiagramSelectedInTheExplorer()
    {
        using var f = new ExplorerFixture();
        f.Main.OpenDiagramCommand.Execute((DiagramItemViewModel)f.Node("A"));
        f.Main.OpenDiagramCommand.Execute((DiagramItemViewModel)f.Node("B"));
        Assert.IsType<DiagramItemViewModel>(f.Main.SelectedNode);

        f.Main.CloseAllTabsCommand.Execute(null);

        Assert.Null(f.Main.SelectedNode);
        Assert.Null(f.Main.SelectedDiagramItem);
    }
}
