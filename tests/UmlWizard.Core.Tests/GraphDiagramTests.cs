using SkiaSharp;
using UmlWizard.Core.Editing;
using UmlWizard.Core.Model;
using UmlWizard.Core.Rendering;
using UmlWizard.Core.Serialization;

namespace UmlWizard.Core.Tests;

public sealed class GraphDiagramTests
{
    public static TheoryData<DiagramType> GraphTypes =>
        [DiagramType.StateMachine, DiagramType.Activity, DiagramType.UseCase, DiagramType.Component, DiagramType.Deployment];

    private static DiagramEditor Editor(DiagramType type) => new(DiagramDocument.Create("Test", type));

    [Theory]
    [MemberData(nameof(GraphTypes))]
    public void EveryNodeAndEdgeKind_IsAdded_Drawn_Saved_AndLoadedBack(DiagramType type)
    {
        var editor = Editor(type);
        // Two nodes of every kind, so edges between nodes of the same kind (e.g. «include») can be drawn.
        var nodes = GraphRules.NodeKinds(type).Concat(GraphRules.NodeKinds(type))
            .Select((kind, i) => editor.AddNode(kind, 100 + i % 6 * 170, 100 + i / 6 * 200))
            .ToList();
        var model = (GraphDiagramModel)editor.Model;

        // Connect the first two nodes that accept each edge kind.
        foreach (var kind in GraphRules.EdgeKinds(type))
        {
            var pair = nodes.SelectMany(s => nodes.Select(t => (s, t)))
                .FirstOrDefault(p => p.s != p.t && editor.CheckEdge(kind, p.s.Id, p.t.Id) is null);
            Assert.NotNull(pair.s);
            Assert.NotNull(editor.AddEdge(kind, pair.s.Id, pair.t.Id));
        }

        using var renderer = DiagramRenderer.Create(type);
        renderer.UpdateLayout(editor.Document);
        Assert.False(renderer.GetContentBounds(editor.Document).IsEmpty);
        using var bitmap = new SKBitmap(1200, 700);
        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(SKColors.White);
            renderer.Render(canvas, editor.Document, new RenderOptions());
        }

        var json = DiagramSerializer.Serialize(editor.Document);
        var loaded = DiagramSerializer.Deserialize(json);
        var loadedModel = Assert.IsAssignableFrom<GraphDiagramModel>(loaded.Model);
        Assert.Equal(type, loaded.Model.DiagramType);
        Assert.Equal(model.Nodes.Select(n => (n.Kind, n.Name)), loadedModel.Nodes.Select(n => (n.Kind, n.Name)));
        Assert.Equal(model.Edges.Select(e => (e.Kind, e.SourceId, e.TargetId)), loadedModel.Edges.Select(e => (e.Kind, e.SourceId, e.TargetId)));
    }

    [Fact]
    public void Nodes_GetNumberedNames_ExceptPseudostates()
    {
        var editor = Editor(DiagramType.StateMachine);

        Assert.Equal("State1", editor.AddNode(GraphNodeKind.State, 0, 0).Name);
        Assert.Equal("State2", editor.AddNode(GraphNodeKind.State, 0, 0).Name);
        Assert.Null(editor.AddNode(GraphNodeKind.InitialState, 0, 0).Name);
        Assert.Equal("System", Editor(DiagramType.UseCase).AddNode(GraphNodeKind.SystemBoundary, 0, 0).Name);
    }

    [Fact]
    public void AddNode_CentresTheNodeOnThePoint_AndRejectsKindsOfOtherDiagrams()
    {
        var editor = Editor(DiagramType.Activity);

        var action = editor.AddNode(GraphNodeKind.Action, 200, 100);

        var layout = editor.Layout.Find(action.Id)!;
        Assert.Equal((140, 75), (layout.X, layout.Y));
        Assert.Throws<ArgumentException>(() => editor.AddNode(GraphNodeKind.State, 0, 0));
    }

    [Fact]
    public void StateMachineRules()
    {
        var editor = Editor(DiagramType.StateMachine);
        var initial = editor.AddNode(GraphNodeKind.InitialState, 0, 0);
        var state = editor.AddNode(GraphNodeKind.State, 200, 0);
        var final = editor.AddNode(GraphNodeKind.FinalState, 400, 0);

        Assert.Null(editor.CheckEdge(GraphEdgeKind.Transition, initial.Id, state.Id));
        Assert.Null(editor.CheckEdge(GraphEdgeKind.Transition, state.Id, state.Id)); // self-transition
        Assert.NotNull(editor.CheckEdge(GraphEdgeKind.Transition, state.Id, initial.Id));
        Assert.NotNull(editor.CheckEdge(GraphEdgeKind.Transition, final.Id, state.Id));
        Assert.NotNull(editor.CheckEdge(GraphEdgeKind.ControlFlow, initial.Id, state.Id)); // wrong diagram
    }

    [Fact]
    public void UseCaseRules()
    {
        var editor = Editor(DiagramType.UseCase);
        var actor = editor.AddNode(GraphNodeKind.Actor, 0, 0);
        var admin = editor.AddNode(GraphNodeKind.Actor, 0, 200);
        var order = editor.AddNode(GraphNodeKind.UseCase, 200, 0);
        var login = editor.AddNode(GraphNodeKind.UseCase, 200, 200);
        var system = editor.AddNode(GraphNodeKind.SystemBoundary, 400, 400);

        Assert.Null(editor.CheckEdge(GraphEdgeKind.Association, actor.Id, order.Id));
        Assert.Null(editor.CheckEdge(GraphEdgeKind.Include, order.Id, login.Id));
        Assert.NotNull(editor.CheckEdge(GraphEdgeKind.Include, actor.Id, login.Id));
        Assert.Null(editor.CheckEdge(GraphEdgeKind.Generalization, admin.Id, actor.Id));
        Assert.NotNull(editor.CheckEdge(GraphEdgeKind.Generalization, admin.Id, order.Id));
        Assert.NotNull(editor.CheckEdge(GraphEdgeKind.Association, actor.Id, system.Id));
        Assert.NotNull(editor.CheckEdge(GraphEdgeKind.Association, actor.Id, actor.Id));
    }

    [Fact]
    public void ComponentRules_RealizationEndsAtAnInterface()
    {
        var editor = Editor(DiagramType.Component);
        var a = editor.AddNode(GraphNodeKind.Component, 0, 0);
        var b = editor.AddNode(GraphNodeKind.Component, 300, 0);
        var i = editor.AddNode(GraphNodeKind.Interface, 300, 200);

        Assert.Null(editor.CheckEdge(GraphEdgeKind.Realization, a.Id, i.Id));
        Assert.NotNull(editor.CheckEdge(GraphEdgeKind.Realization, a.Id, b.Id));
        Assert.Null(editor.CheckEdge(GraphEdgeKind.Dependency, a.Id, b.Id));
        Assert.Null(editor.AddEdge(GraphEdgeKind.Realization, a.Id, b.Id));
    }

    [Fact]
    public void DeletingANode_RemovesItsEdgesAndNoteAttachments_AndUndoRestoresThem()
    {
        var editor = Editor(DiagramType.Activity);
        var a = editor.AddNode(GraphNodeKind.Action, 0, 0);
        var b = editor.AddNode(GraphNodeKind.Action, 300, 0);
        var flow = editor.AddEdge(GraphEdgeKind.ControlFlow, a.Id, b.Id)!;
        var note = editor.AddNote(0, 200);
        editor.AttachNote(note.Id, flow.Id);

        var removed = editor.Delete(a.Id);

        Assert.Equal(3, removed.Count);
        var model = (GraphDiagramModel)editor.Model;
        Assert.Empty(model.Edges);
        Assert.Empty(model.NoteAttachments);

        editor.Undo();
        Assert.Equal(2, ((GraphDiagramModel)editor.Model).Nodes.Count);
        Assert.Single(((GraphDiagramModel)editor.Model).Edges);
    }

    [Fact]
    public void MovingASystemBoundary_TakesTheNodesInsideItAlong()
    {
        var editor = Editor(DiagramType.UseCase);
        var system = editor.AddNode(GraphNodeKind.SystemBoundary, 200, 200); // 320 x 280, from (40, 60)
        var inside = editor.AddNode(GraphNodeKind.UseCase, 200, 200);
        var outside = editor.AddNode(GraphNodeKind.Actor, 600, 200);
        var (insideX, outsideX) = (editor.Layout.Find(inside.Id)!.X, editor.Layout.Find(outside.Id)!.X);

        editor.MoveElementTo(system.Id, 140, 60);

        Assert.Equal(insideX + 100, editor.Layout.Find(inside.Id)!.X);
        Assert.Equal(outsideX, editor.Layout.Find(outside.Id)!.X);
    }

    [Fact]
    public void Resize_OnlyChangesResizableNodes_DownToTheirMinimum()
    {
        var editor = Editor(DiagramType.Activity);
        var action = editor.AddNode(GraphNodeKind.Action, 0, 0);
        var initial = editor.AddNode(GraphNodeKind.InitialNode, 0, 0);
        var bar = editor.AddNode(GraphNodeKind.ForkJoin, 0, 0);

        editor.ResizeElement(action.Id, 200, 90);
        editor.ResizeElement(initial.Id, 200, 90);
        editor.ResizeElement(bar.Id, 8, 120); // a vertical bar

        Assert.Equal((200, 90), (editor.Layout.Find(action.Id)!.Width, editor.Layout.Find(action.Id)!.Height));
        Assert.Equal((22, 22), (editor.Layout.Find(initial.Id)!.Width, editor.Layout.Find(initial.Id)!.Height));
        Assert.Equal((8, 120), (editor.Layout.Find(bar.Id)!.Width, editor.Layout.Find(bar.Id)!.Height));

        editor.ResizeElement(action.Id, 1, 1);
        Assert.Equal((60, 36), (editor.Layout.Find(action.Id)!.Width, editor.Layout.Find(action.Id)!.Height));
    }

    [Fact]
    public void NodesGrow_ToFitALongName()
    {
        var editor = Editor(DiagramType.StateMachine);
        var state = editor.AddNode(GraphNodeKind.State, 0, 0);
        state.Name = "WaitingForPaymentConfirmation";
        using var renderer = DiagramRenderer.Create(DiagramType.StateMachine);

        renderer.UpdateLayout(editor.Document);

        Assert.True(editor.Layout.Find(state.Id)!.Width > 120);
    }

    [Fact]
    public void InlineEdit_RenamesNodes_AndLabelsEdges()
    {
        var editor = Editor(DiagramType.StateMachine);
        var a = editor.AddNode(GraphNodeKind.State, 0, 0);
        var b = editor.AddNode(GraphNodeKind.State, 300, 0);
        var transition = editor.AddEdge(GraphEdgeKind.Transition, a.Id, b.Id)!;
        var initial = editor.AddNode(GraphNodeKind.InitialState, 0, 200);

        editor.BeginInlineEdit(new HitInfo(a.Id, HitPart.Node))!.Commit("Idle");
        editor.BeginInlineEdit(new HitInfo(b.Id, HitPart.Node))!.Commit("  "); // an empty name is ignored
        editor.BeginInlineEdit(new HitInfo(transition.Id, HitPart.Edge))!.Commit("submit [valid] / start()");

        Assert.Equal(("Idle", "State2"), (a.Name, b.Name));
        Assert.Equal("submit [valid] / start()", transition.Label);
        Assert.Null(editor.BeginInlineEdit(new HitInfo(initial.Id, HitPart.Node)));

        editor.BeginInlineEdit(new HitInfo(transition.Id, HitPart.Edge))!.Commit(string.Empty);
        Assert.Null(transition.Label);
    }

    [Fact]
    public void HitTest_FindsNodesEdgesAndLoops_WithBoundariesUnderneath()
    {
        var editor = Editor(DiagramType.StateMachine);
        var a = editor.AddNode(GraphNodeKind.State, 100, 100);
        var b = editor.AddNode(GraphNodeKind.State, 500, 100);
        var ab = editor.AddEdge(GraphEdgeKind.Transition, a.Id, b.Id)!;
        var loop = editor.AddEdge(GraphEdgeKind.Transition, b.Id, b.Id)!;
        using var renderer = DiagramRenderer.Create(DiagramType.StateMachine);
        renderer.UpdateLayout(editor.Document);

        Assert.Equal(new HitInfo(a.Id, HitPart.Node), renderer.HitTest(editor.Document, new SKPoint(100, 100)));
        Assert.True(renderer.HitTest(editor.Document, new SKPoint(100, 100))!.IsNode);
        Assert.Equal(new HitInfo(ab.Id, HitPart.Edge), renderer.HitTest(editor.Document, new SKPoint(300, 100)));

        // The loop rises above the top-right corner of B (which spans 440..560, 75..125).
        Assert.Equal(loop.Id, renderer.HitTest(editor.Document, new SKPoint(560, 70))?.ElementId);

        var useCases = Editor(DiagramType.UseCase);
        var system = useCases.AddNode(GraphNodeKind.SystemBoundary, 200, 200);
        var useCase = useCases.AddNode(GraphNodeKind.UseCase, 200, 200);
        using var useCaseRenderer = DiagramRenderer.Create(DiagramType.UseCase);
        useCaseRenderer.UpdateLayout(useCases.Document);

        Assert.Equal(useCase.Id, useCaseRenderer.HitTest(useCases.Document, new SKPoint(200, 200))?.ElementId);
        var boundaryHit = useCaseRenderer.HitTest(useCases.Document, new SKPoint(60, 300))!;
        Assert.Equal(new HitInfo(system.Id, HitPart.Boundary), boundaryHit);
        Assert.False(boundaryHit.IsNode);
    }

    [Fact]
    public void CopyAndPaste_NodesAndEdges()
    {
        var editor = Editor(DiagramType.Component);
        var a = editor.AddNode(GraphNodeKind.Component, 0, 0);
        var b = editor.AddNode(GraphNodeKind.Component, 300, 0);
        var dependency = editor.AddEdge(GraphEdgeKind.Dependency, a.Id, b.Id)!;
        dependency.Label = "uses";

        var pastedNode = editor.Paste(editor.Copy(a.Id), 50, 300);
        var pastedEdge = editor.Paste(editor.Copy(dependency.Id));

        var model = (GraphDiagramModel)editor.Model;
        // Like pasted classes, the copy is named after the original and numbered.
        Assert.Equal(["Component1", "Component2", "Component12"], model.Nodes.Select(n => n.Name));
        Assert.Equal((50, 300), (editor.Layout.Find(pastedNode!)!.X, editor.Layout.Find(pastedNode!)!.Y));
        Assert.Equal(2, model.Edges.Count(e => e.Label == "uses"));
        Assert.NotEqual(dependency.Id, pastedEdge);

        // Nodes only paste into diagrams of their own type.
        Assert.False(Editor(DiagramType.Activity).CanPaste(editor.Copy(a.Id)));
    }

    [Fact]
    public void EdgesToMissingNodes_AreDroppedWhenLoading()
    {
        var editor = Editor(DiagramType.Activity);
        var a = editor.AddNode(GraphNodeKind.Action, 0, 0);
        ((GraphDiagramModel)editor.Model).Edges.Add(new GraphEdge { Kind = GraphEdgeKind.ControlFlow, SourceId = a.Id, TargetId = "gone" });

        var loaded = DiagramSerializer.Deserialize(DiagramSerializer.Serialize(editor.Document));

        Assert.Empty(((GraphDiagramModel)loaded.Model).Edges);
    }

    [Fact]
    public void NoteAttachments_ConnectToGraphNodesAndEdges()
    {
        var editor = Editor(DiagramType.UseCase);
        var actor = editor.AddNode(GraphNodeKind.Actor, 0, 0);
        var useCase = editor.AddNode(GraphNodeKind.UseCase, 300, 0);
        var association = editor.AddEdge(GraphEdgeKind.Association, actor.Id, useCase.Id)!;
        var note = editor.AddNote(100, 200);
        var toNode = editor.AttachNote(note.Id, useCase.Id)!;
        var toEdge = editor.AttachNote(note.Id, association.Id)!;
        using var renderer = DiagramRenderer.Create(DiagramType.UseCase);
        renderer.UpdateLayout(editor.Document);

        Assert.True(renderer.TryGetAttachmentLine(editor.Document, toNode, out _, out _));
        Assert.True(renderer.TryGetAttachmentLine(editor.Document, toEdge, out _, out _));
    }
}
