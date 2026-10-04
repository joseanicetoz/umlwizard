using SkiaSharp;
using UmlWizard.Core.Editing;
using UmlWizard.Core.Model;
using UmlWizard.Core.Rendering;
using UmlWizard.Core.Serialization;

namespace UmlWizard.Core.Tests;

public sealed class ZOrderTests
{
    /// <summary>Classes A, B, C (back to front) where A and C overlap and B is far away; layouts are measured.</summary>
    private static (DiagramEditor Editor, UmlClass A, UmlClass B, UmlClass C) Stack()
    {
        var editor = new DiagramEditor(DiagramDocument.Create("Test", DiagramType.Class));
        var a = editor.AddClass(ClassifierKind.Class, 100, 100);
        var b = editor.AddClass(ClassifierKind.Class, 600, 600);
        var c = editor.AddClass(ClassifierKind.Class, 130, 110);
        using var renderer = DiagramRenderer.Create(DiagramType.Class);
        renderer.UpdateLayout(editor.Document);
        return (editor, a, b, c);
    }

    private static string[] Order(DiagramEditor editor) =>
        ((ClassDiagramModel)editor.Model).Classes.Select(c => c.Name).ToArray();

    [Fact]
    public void BringToFront_AndSendToBack_MoveToTheEnds()
    {
        var (editor, a, _, c) = Stack();

        Assert.True(editor.ChangeZOrder(a.Id, ZOrderChange.BringToFront));
        Assert.Equal([ "Class2", "Class3", "Class1" ], Order(editor));

        Assert.True(editor.ChangeZOrder(c.Id, ZOrderChange.SendToBack));
        Assert.Equal([ "Class3", "Class2", "Class1" ], Order(editor));
    }

    [Fact]
    public void BringForward_PassesTheNextOverlappingClass()
    {
        var (editor, a, _, _) = Stack();

        // B does not overlap A, so moving one step would change nothing visible: A goes above C instead.
        Assert.True(editor.ChangeZOrder(a.Id, ZOrderChange.BringForward));

        Assert.Equal([ "Class2", "Class3", "Class1" ], Order(editor));
    }

    [Fact]
    public void SendBackward_PassesTheNextOverlappingClass_OrMovesOneStep()
    {
        var (editor, _, b, c) = Stack();

        Assert.True(editor.ChangeZOrder(c.Id, ZOrderChange.SendBackward));
        Assert.Equal([ "Class3", "Class1", "Class2" ], Order(editor));

        // B overlaps nothing: it moves one step.
        Assert.True(editor.ChangeZOrder(b.Id, ZOrderChange.SendBackward));
        Assert.Equal([ "Class3", "Class2", "Class1" ], Order(editor));
    }

    [Fact]
    public void CannotMovePastTheEnds_OrReorderRelationships()
    {
        var (editor, a, b, c) = Stack();
        var relationship = editor.AddRelationship(RelationshipKind.Association, a.Id, b.Id)!;

        Assert.False(editor.CanChangeZOrder(c.Id, ZOrderChange.BringToFront));
        Assert.False(editor.CanChangeZOrder(c.Id, ZOrderChange.BringForward));
        Assert.True(editor.CanChangeZOrder(c.Id, ZOrderChange.SendBackward));
        Assert.False(editor.CanChangeZOrder(a.Id, ZOrderChange.SendToBack));
        Assert.False(editor.CanChangeZOrder(relationship.Id, ZOrderChange.BringToFront));
        Assert.False(editor.ChangeZOrder(c.Id, ZOrderChange.BringToFront));
    }

    [Fact]
    public void ZOrderChange_IsUndoable_AndSaved()
    {
        var (editor, a, _, _) = Stack();
        editor.ChangeZOrder(a.Id, ZOrderChange.BringToFront);

        Assert.Equal([ "Class2", "Class3", "Class1" ], Order(DiagramEditorFor(DiagramSerializer.Serialize(editor.Document))));

        editor.Undo();
        Assert.Equal([ "Class1", "Class2", "Class3" ], Order(editor));

        static DiagramEditor DiagramEditorFor(string json) => new(DiagramSerializer.Deserialize(json));
    }

    [Fact]
    public void FrontMostClass_IsDrawnOnTop_AndHitFirst()
    {
        var (editor, a, _, c) = Stack();
        using var renderer = DiagramRenderer.Create(DiagramType.Class);
        var overlap = new SKPoint(140, 115);
        Assert.Equal(c.Id, renderer.HitTest(editor.Document, overlap)?.ElementId);

        editor.ChangeZOrder(a.Id, ZOrderChange.BringToFront);

        Assert.Equal(a.Id, renderer.HitTest(editor.Document, overlap)?.ElementId);
    }

    [Fact]
    public void Lifelines_HaveAStackingOrder_ButMessagesDoNot()
    {
        var editor = new DiagramEditor(DiagramDocument.Create("Test", DiagramType.Sequence));
        var first = editor.AddLifeline(LifelineKind.Object, 0);
        var second = editor.AddLifeline(LifelineKind.Object, 200);
        var message = editor.AddMessage(MessageKind.Synchronous, first.Id, second.Id, 150)!;

        Assert.False(editor.CanChangeZOrder(message.Id, ZOrderChange.BringToFront));
        Assert.True(editor.ChangeZOrder(first.Id, ZOrderChange.BringToFront));
        Assert.Equal([second.Id, first.Id], ((SequenceDiagramModel)editor.Model).Lifelines.Select(l => l.Id));
    }

    public static TheoryData<DiagramType> GraphTypes =>
        [DiagramType.StateMachine, DiagramType.Activity, DiagramType.UseCase, DiagramType.Component, DiagramType.Deployment];

    [Theory]
    [MemberData(nameof(GraphTypes))]
    public void GraphNodes_CanBeStacked_AndTheFrontMostIsHitFirst(DiagramType type)
    {
        var editor = new DiagramEditor(DiagramDocument.Create("Test", type));
        var kind = GraphRules.NodeKinds(type).First(k => !GraphRules.IsContainer(k) && GraphRules.IsResizable(k));
        var back = editor.AddNode(kind, 100, 100);
        var front = editor.AddNode(kind, 110, 105);
        using var renderer = DiagramRenderer.Create(type);
        renderer.UpdateLayout(editor.Document);
        var overlap = new SKPoint(105, 102);
        Assert.Equal(front.Id, renderer.HitTest(editor.Document, overlap)?.ElementId);

        Assert.True(editor.ChangeZOrder(back.Id, ZOrderChange.BringForward));

        Assert.Equal(back.Id, renderer.HitTest(editor.Document, overlap)?.ElementId);
        Assert.False(editor.CanChangeZOrder(back.Id, ZOrderChange.BringToFront));
        editor.Undo();
        Assert.Equal(front.Id, renderer.HitTest(editor.Document, overlap)?.ElementId);
    }

    [Fact]
    public void Containers_AreStackedAmongThemselves_AndStayUnderTheNodesNestedInThem()
    {
        var editor = new DiagramEditor(DiagramDocument.Create("Test", DiagramType.Deployment));
        GraphNode Place(GraphNodeKind kind, double x, double y, double width, double height)
        {
            var node = editor.AddNode(kind, 0, 0);
            editor.ResizeElement(node.Id, width, height);
            (editor.Layout.Find(node.Id)!.X, editor.Layout.Find(node.Id)!.Y) = (x, y);
            return node;
        }

        var left = Place(GraphNodeKind.Node, 0, 0, 300, 200);
        var right = Place(GraphNodeKind.Node, 200, 50, 300, 200); // overlaps the left node
        var nested = Place(GraphNodeKind.ExecutionEnvironment, 20, 60, 150, 100); // inside the left node
        var artifact = Place(GraphNodeKind.Artifact, 600, 0, 150, 60);
        using var renderer = DiagramRenderer.Create(DiagramType.Deployment);
        renderer.UpdateLayout(editor.Document);
        var overlap = new SKPoint(250, 150);
        Assert.Equal(right.Id, renderer.HitTest(editor.Document, overlap)?.ElementId);

        // Containers are not stacked with the artifact: the left node only passes the right one.
        Assert.True(editor.ChangeZOrder(left.Id, ZOrderChange.BringToFront));
        Assert.Equal(left.Id, renderer.HitTest(editor.Document, overlap)?.ElementId);
        Assert.False(editor.CanChangeZOrder(left.Id, ZOrderChange.BringForward));
        Assert.False(editor.CanChangeZOrder(artifact.Id, ZOrderChange.SendBackward));

        // The environment nested in the left node stays above it, even when sent to the back.
        editor.ChangeZOrder(nested.Id, ZOrderChange.SendToBack);
        Assert.Equal(nested.Id, renderer.HitTest(editor.Document, new SKPoint(50, 100))?.ElementId);
    }

    [Fact]
    public void NotesAndTextBoxes_AreStackedTogether_AboveTheUmlElements()
    {
        var (editor, a, _, _) = Stack();
        var note = editor.AddNote(100, 100);
        var textBox = editor.AddTextBox(110, 110);
        var later = editor.AddNote(120, 120);
        using var renderer = DiagramRenderer.Create(DiagramType.Class);
        var overlap = new SKPoint(125, 125);
        Assert.Equal(later.Id, renderer.HitTest(editor.Document, overlap)?.ElementId);

        // A note can go above a text box, and they never mix with the classes' stack.
        Assert.True(editor.ChangeZOrder(note.Id, ZOrderChange.BringToFront));
        Assert.Equal([textBox.Id, later.Id, note.Id], editor.Model.TextElements().Select(e => e.Id));
        Assert.Equal(note.Id, renderer.HitTest(editor.Document, overlap)?.ElementId);
        Assert.False(editor.CanChangeZOrder(a.Id, ZOrderChange.SendToBack));
        Assert.True(editor.HasStackingOrder(textBox.Id));

        // The order is saved, undoable, and a new element goes on top.
        var reloaded = DiagramSerializer.Deserialize(DiagramSerializer.Serialize(editor.Document));
        Assert.Equal([textBox.Id, later.Id, note.Id], reloaded.Model.TextElements().Select(e => e.Id));
        var added = editor.AddTextBox(0, 0);
        Assert.Equal(added.Id, editor.Model.TextElements().Last().Id);
        editor.Undo();
        editor.Undo();
        Assert.Equal([note.Id, textBox.Id, later.Id], editor.Model.TextElements().Select(e => e.Id));
    }

    [Fact]
    public void FilesWithoutAStackingOrder_KeepNotesUnderTextBoxes()
    {
        var model = new ClassDiagramModel();
        model.TextBoxes.Add(new TextBoxElement { Id = "text" });
        model.Notes.Add(new NoteElement { Id = "note" });

        Assert.Equal(["note", "text"], model.TextElements().Select(e => e.Id));
        Assert.DoesNotContain("zIndex", DiagramSerializer.Serialize(new DiagramDocument { Model = model }), StringComparison.OrdinalIgnoreCase);
    }
}
