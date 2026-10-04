using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using UmlWizard.App.ViewModels;
using UmlWizard.App.Views;

namespace UmlWizard.App.Tests;

public sealed class ExplorerClickTests
{
    private static (MainWindow Window, TreeView Tree) Show(ExplorerFixture f)
    {
        var window = new MainWindow { DataContext = f.Main };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return (window, window.FindControl<TreeView>("DiagramTree")!);
    }

    /// <summary>Clicks the middle of the explorer row showing <paramref name="node"/>.</summary>
    private static void Click(MainWindow window, TreeView tree, ProjectNodeViewModel node)
    {
        // Opening a diagram expands its folder, which moves the rows below it.
        window.UpdateLayout();
        var header = tree.GetVisualDescendants().OfType<TextBlock>().First(t => t.DataContext == node && t.Text == node.Name);
        var point = header.TranslatePoint(new Point(header.Bounds.Width / 2, header.Bounds.Height / 2), window)!.Value;
        window.MouseDown(point, MouseButton.Left);
        window.MouseUp(point, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
    }

    [AvaloniaFact]
    public void OneClick_OnADiagram_OpensIt_AndOneClickOnAnOpenDiagram_ActivatesItsTab()
    {
        using var f = new ExplorerFixture();
        var (window, tree) = Show(f);
        f.Main.OpenDiagramCommand.Execute((DiagramItemViewModel)f.Node("A"));
        Dispatcher.UIThread.RunJobs();

        Click(window, tree, f.Node("B"));

        Assert.Equal(["A", "B"], f.Main.OpenDiagrams.Select(d => d.Name));
        Assert.Equal("B", f.Main.ActiveDiagram?.Name);

        Click(window, tree, f.Node("A"));

        Assert.Equal(2, f.Main.OpenDiagrams.Count);
        Assert.Equal("A", f.Main.ActiveDiagram?.Name);
        window.Close();
    }

    [AvaloniaFact]
    public void ClickingAFolder_OpensNothing()
    {
        using var f = new ExplorerFixture();
        var (window, tree) = Show(f);

        Click(window, tree, f.Node("Model"));

        Assert.Empty(f.Main.OpenDiagrams);
        window.Close();
    }

    [AvaloniaFact]
    public void Enter_OpensTheDiagramSelectedWithTheKeyboard()
    {
        using var f = new ExplorerFixture();
        var (window, tree) = Show(f);
        f.Main.SelectedNode = f.Node("B");
        Dispatcher.UIThread.RunJobs();
        Assert.Empty(f.Main.OpenDiagrams);

        // Focus is on the diagram's row, as after clicking it or moving to it with the arrow keys.
        Assert.True(tree.GetVisualDescendants().OfType<TreeViewItem>().Single(i => i.DataContext == f.Node("B")).Focus());
        window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal("B", f.Main.ActiveDiagram?.Name);
        window.Close();
    }
}
