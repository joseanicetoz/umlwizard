using SkiaSharp;
using UmlWizard.Core.Editing;
using UmlWizard.Core.Model;
using UmlWizard.Core.Rendering;
using UmlWizard.Core.Serialization;

namespace UmlWizard.Core.Tests;

public sealed class ArrangeTests
{
    private static readonly string[] Samples = ["OnlineShop/OnlineShop.umlwiz", "GangOfFour/GangOfFour.umlwiz"];

    public static TheoryData<string, int> SampleDiagrams()
    {
        var data = new TheoryData<string, int>();
        foreach (var sample in Samples)
        {
            var count = Load(sample).Diagrams.Count;
            for (var i = 0; i < count; i++)
            {
                data.Add(sample, i);
            }
        }

        return data;
    }

    private static UmlProject Load(string sample) => ProjectSerializer.Load(Path.Combine(AppContext.BaseDirectory, "samples", sample));

    [Theory]
    [MemberData(nameof(SampleDiagrams))]
    public void EverySampleDiagram_IsArranged_WithoutOverlaps_AndNoMoreCrossingsThanBefore(string sample, int index)
    {
        var document = Load(sample).Diagrams[index];
        var crossingsBefore = Crossings(document);
        var model = DiagramSerializer.Serialize(document);

        DiagramArranger.Arrange(document);

        Assert.Empty(Overlaps(document));
        Assert.True(Crossings(document) <= crossingsBefore, $"{document.Name}: {crossingsBefore} crossings before, {Crossings(document)} after");

        // Only positions (and container sizes) change, never the model.
        Assert.Equal(
            System.Text.Json.Nodes.JsonNode.Parse(model)!["model"]!.ToJsonString(),
            System.Text.Json.Nodes.JsonNode.Parse(DiagramSerializer.Serialize(document))!["model"]!.ToJsonString());

        // Arranging an arranged diagram changes nothing.
        var arranged = DiagramSerializer.Serialize(document);
        DiagramArranger.Arrange(document);
        Assert.Equal(arranged, DiagramSerializer.Serialize(document));
    }

    [Fact]
    public void ClassDiagram_PutsBaseClassesAboveSubclasses_AndWholesAboveParts()
    {
        var editor = new DiagramEditor(DiagramDocument.Create("Test", DiagramType.Class));
        // Deliberately upside down and on one line.
        var dog = editor.AddClass(ClassifierKind.Class, 100, 100);
        var cat = editor.AddClass(ClassifierKind.Class, 300, 100);
        var animal = editor.AddClass(ClassifierKind.AbstractClass, 500, 100);
        var pet = editor.AddClass(ClassifierKind.Interface, 700, 100);
        var tail = editor.AddClass(ClassifierKind.Class, 900, 100);
        editor.AddRelationship(RelationshipKind.Inheritance, dog.Id, animal.Id);
        editor.AddRelationship(RelationshipKind.Inheritance, cat.Id, animal.Id);
        editor.AddRelationship(RelationshipKind.Realization, dog.Id, pet.Id);
        editor.AddRelationship(RelationshipKind.Composition, animal.Id, tail.Id);

        Assert.True(editor.Arrange());

        double Top(UmlClass c) => editor.Layout.Find(c.Id)!.Y;
        Assert.True(Top(animal) < Top(dog) && Top(animal) < Top(cat));
        Assert.True(Top(pet) < Top(dog));
        Assert.True(Top(animal) < Top(tail));
        Assert.Empty(Overlaps(editor.Document));
    }

    [Fact]
    public void ClearingLines_DoesNotBlowTheLayoutUp_OnADenselyConnectedGraph()
    {
        // A deep tree with as many lines again across its layers, like the class diagram of a large code base. Moving
        // nodes off every line that would cross them used to make such layouts many times wider, nearly all empty.
        var ids = Enumerable.Range(0, 60).Select(i => $"n{i}").ToList();
        var size = ids.ToDictionary(id => id, _ => (160.0, 80.0));
        var next = 12345u;
        int Next(int max) => (int)((next = next * 1103515245 + 12345) / 65536 % (uint)max);
        var edges = Enumerable.Range(1, ids.Count - 1)
            .SelectMany(i => new[] { ($"n{Next(i)}", $"n{i}"), ($"n{Next(i)}", $"n{i}") })
            .Distinct().ToList();
        double Width(double growth)
        {
            var positions = LayeredLayout.Place(ids, size, edges, leftToRight: false, maxClearingGrowth: growth).Positions.Values;
            return positions.Max(p => p.X + 160) - positions.Min(p => p.X);
        }

        var uncleared = Width(0);
        Assert.True(Width(double.PositiveInfinity) > uncleared * LayeredLayout.MaxClearingGrowth, "the graph no longer makes clearing run away");
        Assert.True(Width(LayeredLayout.MaxClearingGrowth) <= uncleared * LayeredLayout.MaxClearingGrowth);
    }

    [Fact]
    public async Task ArrangingOnAnotherThread_GivesTheSameLayout_AsOneUndoStep()
    {
        var document = Load(Samples[1]).Diagrams[0];
        var expected = DiagramSerializer.Clone(document);
        DiagramArranger.Arrange(expected);
        var editor = new DiagramEditor(document);
        var before = DiagramSerializer.Serialize(document);

        Assert.True(await editor.ArrangeAsync());

        Assert.Equal(DiagramSerializer.Serialize(expected), DiagramSerializer.Serialize(editor.Document));
        Assert.True(editor.IsDirty);
        Assert.True(editor.Undo());
        Assert.Equal(before, DiagramSerializer.Serialize(editor.Document));
        Assert.False(editor.CanUndo);
    }

    [Fact]
    public async Task CancellingArranging_LeavesTheDiagramAsItWas()
    {
        var document = Load(Samples[1]).Diagrams[0];
        var editor = new DiagramEditor(document);
        var before = DiagramSerializer.Serialize(document);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => editor.ArrangeAsync(new CancellationToken(canceled: true)));

        Assert.Equal(before, DiagramSerializer.Serialize(editor.Document));
        Assert.False(editor.CanUndo);
        Assert.False(editor.IsDirty);
    }

    [Fact]
    public void ClassesWithoutRelationships_ArePackedInRows_NotInOneLongRow()
    {
        var editor = new DiagramEditor(DiagramDocument.Create("Test", DiagramType.Class));
        for (var i = 0; i < 30; i++)
        {
            editor.AddClass(ClassifierKind.Class, 100 + i * 200, 100);
        }

        editor.Arrange();

        using var renderer = DiagramRenderer.Create(DiagramType.Class);
        var bounds = renderer.GetContentBounds(editor.Document);
        Assert.True(bounds.Width < bounds.Height * 4, $"{bounds.Width} × {bounds.Height}");
        Assert.Empty(Overlaps(editor.Document));
    }

    [Fact]
    public void StateMachine_ReadsFromTopToBottom()
    {
        var editor = new DiagramEditor(DiagramDocument.Create("Test", DiagramType.StateMachine));
        var final = editor.AddNode(GraphNodeKind.FinalState, 100, 100);
        var b = editor.AddNode(GraphNodeKind.State, 300, 100);
        var a = editor.AddNode(GraphNodeKind.State, 500, 100);
        var start = editor.AddNode(GraphNodeKind.InitialState, 700, 100);
        editor.AddEdge(GraphEdgeKind.Transition, start.Id, a.Id);
        editor.AddEdge(GraphEdgeKind.Transition, a.Id, b.Id);
        editor.AddEdge(GraphEdgeKind.Transition, b.Id, a.Id);
        editor.AddEdge(GraphEdgeKind.Transition, b.Id, final.Id);

        editor.Arrange();

        double Top(GraphNode n) => editor.Layout.Find(n.Id)!.Y;
        Assert.True(Top(start) < Top(a) && Top(a) < Top(b) && Top(b) < Top(final));
    }

    [Fact]
    public void NodesStayInTheirContainers()
    {
        var editor = new DiagramEditor(DiagramDocument.Create("Test", DiagramType.Deployment));
        var server = editor.AddNode(GraphNodeKind.Node, 300, 300);
        editor.ResizeElement(server.Id, 400, 300);
        var serverLayout = editor.Layout.Find(server.Id)!;
        var app = editor.AddNode(GraphNodeKind.Artifact, serverLayout.X + 120, serverLayout.Y + 150);
        var client = editor.AddNode(GraphNodeKind.Device, 900, 100);
        editor.AddEdge(GraphEdgeKind.CommunicationPath, client.Id, server.Id);
        Assert.True(Inside(editor.Layout.Find(app.Id)!, serverLayout));

        editor.Arrange();

        Assert.True(Inside(editor.Layout.Find(app.Id)!, editor.Layout.Find(server.Id)!));
        Assert.False(Inside(editor.Layout.Find(client.Id)!, editor.Layout.Find(server.Id)!));
    }

    [Fact]
    public void Sequence_OrdersLifelinesByTheirFirstMessage_KeepsTheMessageOrder_AndFitsTheLabels()
    {
        var editor = new DiagramEditor(DiagramDocument.Create("Test", DiagramType.Sequence));
        var c = editor.AddLifeline(LifelineKind.Object, 40);
        var b = editor.AddLifeline(LifelineKind.Object, 240);
        var a = editor.AddLifeline(LifelineKind.Actor, 440);
        var unused = editor.AddLifeline(LifelineKind.Object, 640);
        var first = editor.AddMessage(MessageKind.Synchronous, a.Id, b.Id, 100)!;
        var second = editor.AddMessage(MessageKind.Synchronous, b.Id, c.Id, 300)!;
        second.Text = "a rather long operation name(with, several, parameters)";
        var third = editor.AddMessage(MessageKind.Reply, c.Id, b.Id, 310)!;
        var self = editor.AddMessage(MessageKind.Synchronous, b.Id, b.Id, 500)!;
        var last = editor.AddMessage(MessageKind.Reply, b.Id, a.Id, 900)!;

        editor.Arrange();

        double X(Lifeline l) => editor.Layout.Find(l.Id)!.X;
        Assert.True(X(a) < X(b) && X(b) < X(c) && X(c) < X(unused));

        double Y(SequenceMessage m) => editor.Layout.Find(m.Id)!.Y;
        Assert.True(Y(first) < Y(second) && Y(second) < Y(third) && Y(third) < Y(self) && Y(self) < Y(last));
        Assert.Equal(Y(second) - Y(first), Y(third) - Y(second), 3);

        // The long label fits between its lifelines.
        using var renderer = DiagramRenderer.Create(DiagramType.Sequence);
        var label = renderer.GetEditRect(editor.Document, second.Id, HitPart.Message)!.Value;
        var bCentre = X(b) + editor.Layout.Find(b.Id)!.Width / 2;
        var cCentre = X(c) + editor.Layout.Find(c.Id)!.Width / 2;
        Assert.True(label.Width <= cCentre - bCentre, $"{label.Width} > {cCentre - bCentre}");
    }

    [Fact]
    public void ATitleStaysAbove_AndANoteGoesBesideWhatItIsAttachedTo()
    {
        var editor = new DiagramEditor(DiagramDocument.Create("Test", DiagramType.Class));
        var title = editor.AddTextBox(300, 10);
        var a = editor.AddClass(ClassifierKind.Class, 100, 200);
        var b = editor.AddClass(ClassifierKind.Class, 400, 200);
        var c = editor.AddClass(ClassifierKind.Class, 700, 200);
        editor.AddRelationship(RelationshipKind.Inheritance, b.Id, a.Id);
        editor.AddRelationship(RelationshipKind.Inheritance, c.Id, b.Id);
        var note = editor.AddNote(100, 500);
        editor.AttachNote(note.Id, c.Id);

        editor.Arrange();

        var titleLayout = editor.Layout.Find(title.Id)!;
        using var renderer = DiagramRenderer.Create(DiagramType.Class);
        renderer.UpdateLayout(editor.Document);
        var classes = new[] { a, b, c }.Select(x => renderer.GetElementBounds(editor.Document, x.Id)!.Value).ToList();
        Assert.True(titleLayout.Y + titleLayout.Height <= classes.Min(r => r.Top));

        var noteLayout = editor.Layout.Find(note.Id)!;
        var target = renderer.GetElementBounds(editor.Document, c.Id)!.Value;
        Assert.True(noteLayout.X >= classes.Max(r => r.Right));
        Assert.Equal(target.Top, noteLayout.Y, 1);
    }

    [Fact]
    public void Arrange_IsOneUndoableStep()
    {
        var editor = new DiagramEditor(DiagramDocument.Create("Test", DiagramType.Class));
        var a = editor.AddClass(ClassifierKind.Class, 500, 500);
        var b = editor.AddClass(ClassifierKind.Class, 100, 100);
        editor.AddRelationship(RelationshipKind.Inheritance, b.Id, a.Id);
        var before = DiagramSerializer.Serialize(editor.Document);

        Assert.True(editor.Arrange());
        Assert.NotEqual(before, DiagramSerializer.Serialize(editor.Document));

        Assert.True(editor.Undo());
        Assert.Equal(before, DiagramSerializer.Serialize(editor.Document));
    }

    [Fact]
    public void AnEmptyDiagram_HasNothingToArrange()
    {
        var editor = new DiagramEditor(DiagramDocument.Create("Test", DiagramType.Component));
        editor.AddNote(100, 100);

        Assert.False(editor.CanArrange);
        Assert.False(editor.Arrange());

        // No undo step was recorded: the first undo takes the note away.
        editor.Undo();
        Assert.Empty(editor.Model.Notes);
    }

    private static bool Inside(ElementLayout inner, ElementLayout outer) =>
        inner.X >= outer.X && inner.Y >= outer.Y && inner.X + inner.Width <= outer.X + outer.Width && inner.Y + inner.Height <= outer.Y + outer.Height;

    /// <summary>The drawn bounds of the classes or nodes (not lifelines), and of the notes and text boxes.</summary>
    private static Dictionary<string, SKRect> Shapes(DiagramDocument document)
    {
        using var renderer = DiagramRenderer.Create(document.DiagramType);
        renderer.UpdateLayout(document);
        IEnumerable<string> ids = document.Model switch
        {
            ClassDiagramModel m => m.Classes.Select(c => c.Id),
            GraphDiagramModel m => m.Nodes.Select(n => n.Id),
            _ => [],
        };
        return ids.Concat(document.Model.TextElements().Select(t => t.Id))
            .Select(id => (id, Bounds: renderer.GetElementBounds(document, id)))
            .Where(p => p.Bounds is not null)
            .ToDictionary(p => p.id, p => p.Bounds!.Value);
    }

    /// <summary>Pairs of shapes that overlap without one holding the other (a container and what is in it).</summary>
    private static List<(string, string)> Overlaps(DiagramDocument document)
    {
        var shapes = Shapes(document).ToList();
        var result = new List<(string, string)>();
        for (var i = 0; i < shapes.Count; i++)
        {
            for (var j = i + 1; j < shapes.Count; j++)
            {
                var (a, b) = (shapes[i].Value, shapes[j].Value);
                var overlap = SKRect.Intersect(a, b);
                if (overlap.Width > 1 && overlap.Height > 1 && !Contains(a, b) && !Contains(b, a))
                {
                    result.Add((shapes[i].Key, shapes[j].Key));
                }
            }
        }

        return result;

        static bool Contains(SKRect outer, SKRect inner) =>
            inner.Left >= outer.Left - 0.5f && inner.Top >= outer.Top - 0.5f && inner.Right <= outer.Right + 0.5f && inner.Bottom <= outer.Bottom + 0.5f;
    }

    /// <summary>Pairs of connectors whose straight lines, from centre to centre, cross (connectors that share an end do not count).</summary>
    private static int Crossings(DiagramDocument document)
    {
        var shapes = Shapes(document);
        IEnumerable<(string Source, string Target)> lines = document.Model switch
        {
            ClassDiagramModel m => m.Relationships.Select(r => (r.SourceId, r.TargetId)),
            GraphDiagramModel m => m.Edges.Select(e => (e.SourceId, e.TargetId)),
            _ => [],
        };
        var segments = lines
            .Where(l => l.Source != l.Target && shapes.ContainsKey(l.Source) && shapes.ContainsKey(l.Target))
            .Select(l => (l.Source, l.Target, From: Centre(shapes[l.Source]), To: Centre(shapes[l.Target])))
            .ToList();
        var count = 0;
        for (var i = 0; i < segments.Count; i++)
        {
            for (var j = i + 1; j < segments.Count; j++)
            {
                var (s, t) = (segments[i], segments[j]);
                if (s.Source == t.Source || s.Source == t.Target || s.Target == t.Source || s.Target == t.Target)
                {
                    continue;
                }

                if (Cross(s.From, s.To, t.From, t.To))
                {
                    count++;
                }
            }
        }

        return count;

        static SKPoint Centre(SKRect r) => new(r.MidX, r.MidY);

        static bool Cross(SKPoint a, SKPoint b, SKPoint c, SKPoint d)
        {
            static double Side(SKPoint p, SKPoint q, SKPoint r) => ((double)q.X - p.X) * ((double)r.Y - p.Y) - ((double)q.Y - p.Y) * ((double)r.X - p.X);
            return Side(a, b, c) * Side(a, b, d) < 0 && Side(c, d, a) * Side(c, d, b) < 0;
        }
    }
}
