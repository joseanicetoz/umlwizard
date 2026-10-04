using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Avalonia.VisualTree;
using UmlWizard.App.ViewModels;
using UmlWizard.App.ViewModels.Dialogs;
using UmlWizard.App.Views;
using UmlWizard.Core.Model;

namespace UmlWizard.App.Tests;

public sealed class GraphDiagramUiTests
{
    public static TheoryData<DiagramType> GraphTypes =>
        [DiagramType.StateMachine, DiagramType.Activity, DiagramType.UseCase, DiagramType.Component, DiagramType.Deployment];

    private static (double X, double Y) Center(DiagramHarness h, string id)
    {
        var layout = h.ViewModel.Document.Layout.Find(id)!;
        return (layout.X + layout.Width / 2, layout.Y + layout.Height / 2);
    }

    private static void Use(DiagramHarness h, GraphNodeKind kind) =>
        h.ViewModel.ActiveTool = h.ViewModel.Tools.Single(t => t.NodeKind == kind);

    private static void Use(DiagramHarness h, GraphEdgeKind kind) =>
        h.ViewModel.ActiveTool = h.ViewModel.Tools.Single(t => t.EdgeKind == kind);

    [Theory]
    [MemberData(nameof(GraphTypes))]
    public void Toolbox_HasEveryNodeAndEdgeOfTheDiagram_PlusNotesAndTextBoxes(DiagramType type)
    {
        var tools = ToolItem.For(type);

        Assert.Equal(GraphRules.NodeKinds(type), tools.Where(t => t.NodeKind is not null).Select(t => t.NodeKind!.Value));
        Assert.Equal(GraphRules.EdgeKinds(type), tools.Where(t => t.EdgeKind is not null).Select(t => t.EdgeKind!.Value));
        Assert.All(tools.Where(t => t.NodeKind is not null), t => Assert.True(t.IsNodeTool));
        Assert.All(tools.Where(t => t.EdgeKind is not null), t => Assert.True(t.IsConnectorTool));
        Assert.Contains(tools, t => t.Kind == ToolKind.Note);
        Assert.Contains(tools, t => t.Kind == ToolKind.NoteAttachment);
        Assert.Contains(tools, t => t.Kind == ToolKind.TextBox);
    }

    [AvaloniaFact]
    public void ClickingWithNodeTools_AndDraggingWithEdgeTools_DrawsAStateMachine()
    {
        var h = new DiagramHarness(DiagramType.StateMachine);
        Use(h, GraphNodeKind.InitialState);
        h.Click(60, 100);
        Use(h, GraphNodeKind.State);
        h.Click(250, 100);
        Use(h, GraphNodeKind.State);
        h.Click(500, 100);
        var model = (GraphDiagramModel)h.ViewModel.Document.Model;
        var (initial, idle, busy) = (model.Nodes[0], model.Nodes[1], model.Nodes[2]);

        Use(h, GraphEdgeKind.Transition);
        h.Drag(Center(h, initial.Id).X, Center(h, initial.Id).Y, Center(h, idle.Id).X, Center(h, idle.Id).Y);
        Use(h, GraphEdgeKind.Transition);
        h.Drag(Center(h, idle.Id).X, Center(h, idle.Id).Y, Center(h, busy.Id).X, Center(h, busy.Id).Y);

        // A transition dropped back onto its own state loops.
        Use(h, GraphEdgeKind.Transition);
        h.Drag(Center(h, busy.Id).X - 20, Center(h, busy.Id).Y, Center(h, busy.Id).X + 20, Center(h, busy.Id).Y);

        // Nothing leads into the initial state.
        Use(h, GraphEdgeKind.Transition);
        h.Drag(Center(h, busy.Id).X, Center(h, busy.Id).Y, Center(h, initial.Id).X, Center(h, initial.Id).Y);

        Assert.Equal([GraphNodeKind.InitialState, GraphNodeKind.State, GraphNodeKind.State], model.Nodes.Select(n => n.Kind));
        Assert.Equal([(initial.Id, idle.Id), (idle.Id, busy.Id), (busy.Id, busy.Id)], model.Edges.Select(e => (e.SourceId, e.TargetId)));
        // As with the class diagram tools, a rejected drag keeps the tool so the user can try again.
        Assert.Equal(GraphEdgeKind.Transition, h.ViewModel.ActiveTool.EdgeKind);
    }

    [AvaloniaFact]
    public void DoubleClickingANode_RenamesItInPlace()
    {
        var h = new DiagramHarness(DiagramType.UseCase);
        var useCase = h.ViewModel.Editor.AddNode(GraphNodeKind.UseCase, 200, 150);
        h.ViewModel.Renderer.UpdateLayout(h.ViewModel.Document);

        h.DoubleClick(200, 150);
        Assert.True(h.InlineEditor.IsVisible);
        h.Type("Place order");
        h.Press(Key.Enter);

        Assert.Equal("Place order", useCase.Name);
        Assert.StartsWith("Selected: UseCase Place order", h.ViewModel.StatusText);
    }

    [AvaloniaFact]
    public async Task EditDialog_RenamesNodes_AndLabelsOrUnlabelsEdges()
    {
        var h = new DiagramHarness(DiagramType.Activity);
        var a = h.ViewModel.Editor.AddNode(GraphNodeKind.Action, 100, 100);
        var d = h.ViewModel.Editor.AddNode(GraphNodeKind.Decision, 300, 100);
        var flow = h.ViewModel.Editor.AddEdge(GraphEdgeKind.ControlFlow, a.Id, d.Id)!;
        h.Services.AcceptDialogs = true;

        h.Services.FillDialog = dialog => ((TextInputViewModel)dialog).Text = "Check stock";
        await h.ViewModel.EditElementAsync(a.Id);
        h.Services.FillDialog = dialog => ((TextInputViewModel)dialog).Text = "[in stock]";
        await h.ViewModel.EditElementAsync(flow.Id);
        Assert.Equal(("Check stock", "[in stock]"), (a.Name, flow.Label));

        h.Services.FillDialog = dialog =>
        {
            var input = (TextInputViewModel)dialog;
            input.Text = string.Empty;
            Assert.True(input.TryAccept()); // an edge label may be removed
        };
        await h.ViewModel.EditElementAsync(flow.Id);
        Assert.Null(flow.Label);
    }

    [AvaloniaFact]
    public void DraggingASystemBoundary_MovesTheUseCasesInside()
    {
        var h = new DiagramHarness(DiagramType.UseCase);
        var system = h.ViewModel.Editor.AddNode(GraphNodeKind.SystemBoundary, 300, 250);
        var useCase = h.ViewModel.Editor.AddNode(GraphNodeKind.UseCase, 300, 250);
        h.ViewModel.Renderer.UpdateLayout(h.ViewModel.Document);
        var before = h.ViewModel.Document.Layout.Find(useCase.Id)!.X;

        // Grab the boundary in an empty spot inside it.
        h.Drag(170, 350, 270, 350);

        Assert.Equal(before + 100, h.ViewModel.Document.Layout.Find(useCase.Id)!.X, 1);
        Assert.Equal(system.Id, h.ViewModel.Editor.SelectedId);
    }

    [AvaloniaFact]
    public void NewDiagramDropDown_IsOnTheToolbarAfterSaveProject_AndTheSidePanelHasNoButtons()
    {
        using var f = new ExplorerFixture();
        var window = new MainWindow { DataContext = f.Main };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var dropDown = window.FindControl<DropDownButton>("NewDiagramButton")!;
        var openProject = window.FindControl<Button>("OpenProjectButton")!;
        var save = window.FindControl<Button>("SaveProjectButton")!;
        var undo = window.GetLogicalDescendants().OfType<DiagramToolbar>().Single().GetLogicalDescendants().OfType<Button>().First();
        Assert.Same(openProject.Parent, dropDown.Parent);
        double Left(Visual v) => v.TranslatePoint(default, window)!.Value.X;
        Assert.True(Left(openProject) < Left(save) && Left(save) < Left(dropDown) && Left(dropDown) < Left(undo));
        Assert.True(dropDown.IsEffectivelyEnabled);
        var flyout = Assert.IsType<MenuFlyout>(dropDown.Flyout);
        Assert.Same(f.Main.NewDiagramMenu, flyout.ItemsSource);
        Assert.Equal(DiagramTypeInfo.All.Select(i => $"{i.DisplayName}…"), f.Main.NewDiagramMenu.Select(e => e.Header));

        // The side panel has no toolbar left: no buttons at all.
        var sidePanel = window.FindControl<TreeView>("DiagramTree")!.Parent!;
        Assert.DoesNotContain(sidePanel.GetLogicalDescendants().OfType<Button>(), b => b.IsEffectivelyVisible);

        // Without a project there is nothing to add a diagram to.
        f.Main.CloseProjectCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();
        Assert.False(dropDown.IsEffectivelyEnabled);
        window.Close();
    }

    [Theory]
    [MemberData(nameof(GraphTypes))]
    public async Task NewDiagramMenuEntry_CreatesAndOpensADiagramOfItsType(DiagramType type)
    {
        using var f = new ExplorerFixture();
        f.Services.FillDialog = d =>
        {
            var dialog = (NewDiagramViewModel)d;
            Assert.Equal(type, dialog.Type);
            Assert.StartsWith(DiagramTypeInfo.For(type).NamePrefix, dialog.Name);
        };
        var entry = f.Main.NewDiagramMenu.Single(e => e.Type == type);

        await ((CommunityToolkit.Mvvm.Input.IAsyncRelayCommand<DiagramType>)entry.Command).ExecuteAsync(entry.Type);

        Assert.Equal(type, f.Main.ActiveDiagram?.DiagramType);
        Assert.Contains(f.Saved.Diagrams, d => d.DiagramType == type);
        Assert.Equal(DiagramTypeInfo.For(type).DisplayName, ((DiagramItemViewModel)f.Main.SelectedNode!).TypeName);
    }

    [AvaloniaFact]
    public void CutCopyPaste_AreNoLongerGroupedInAFrame()
    {
        var services = new FakeServices();
        var window = new MainWindow { DataContext = new MainWindowViewModel(services, services) };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var toolbar = window.GetLogicalDescendants().OfType<DiagramToolbar>().Single();
        var cut = toolbar.GetLogicalDescendants().OfType<Button>().Single(b => ToolTip.GetTip(b) is string tip && tip.StartsWith("Cut"));

        Assert.IsType<StackPanel>(cut.Parent);
        Assert.Same(toolbar.GetLogicalDescendants().OfType<Button>().First().Parent, cut.Parent);
        Assert.DoesNotContain(toolbar.GetLogicalDescendants().OfType<Border>(), b => b.Classes.Contains("buttonGroup"));
        window.Close();
    }

    [Fact]
    public void DiagramTypeInfo_CoversEveryDiagramType()
    {
        Assert.Equal(Enum.GetValues<DiagramType>(), DiagramTypeInfo.All.Select(i => i.Type));
        Assert.Equal("State machine diagram", DiagramTypeInfo.NameConverter.Convert(DiagramType.StateMachine, typeof(string), null, System.Globalization.CultureInfo.InvariantCulture));
    }
}
