using SkiaSharp;
using UmlWizard.Core.Editing;
using UmlWizard.Core.Model;
using UmlWizard.Core.Rendering;
using UmlWizard.Core.Serialization;

namespace UmlWizard.Core.Tests;

public sealed class LineStyleTests
{
    private static readonly SKColor Red = new(0xC6, 0x28, 0x28);

    [Fact]
    public void SetLineStyle_IsUndoable_AndEmptyStyleRestoresDefaults()
    {
        var (editor, _, _, relationship) = TwoClasses(RelationshipKind.Association);
        var style = new LineStyle { Color = "#C62828", Width = 4 };

        editor.SetLineStyle(relationship.Id, style);
        style.Width = 1;
        Assert.Equal(4, editor.Layout.Find(relationship.Id)!.Line!.Width);

        editor.SetLineStyle(relationship.Id, new LineStyle());
        Assert.Null(editor.Layout.Find(relationship.Id)!.Line);

        editor.Undo();
        Assert.Equal("#C62828", editor.Layout.Find(relationship.Id)!.Line!.Color);
        editor.Undo();
        Assert.Null(editor.Layout.Find(relationship.Id)?.Line);
    }

    [Fact]
    public void OnlyConnectors_HaveALineStyle()
    {
        var (editor, a, _, relationship) = TwoClasses(RelationshipKind.Association);

        Assert.True(DiagramEditor.HasLine(relationship));
        Assert.False(DiagramEditor.HasLine(a));

        editor.SetLineStyle(a.Id, new LineStyle { Width = 5 });
        Assert.Null(editor.Layout.Find(a.Id)!.Line);

        var sequence = new DiagramEditor(DiagramDocument.Create("S", DiagramType.Sequence));
        var from = sequence.AddLifeline(LifelineKind.Object, 50);
        var to = sequence.AddLifeline(LifelineKind.Object, 300);
        Assert.True(DiagramEditor.HasLine(sequence.AddMessage(MessageKind.Synchronous, from.Id, to.Id, 120)));

        var graph = new DiagramEditor(DiagramDocument.Create("G", DiagramType.StateMachine));
        var s1 = graph.AddNode(GraphNodeKind.State, 0, 0);
        var s2 = graph.AddNode(GraphNodeKind.State, 300, 0);
        Assert.True(DiagramEditor.HasLine(graph.AddEdge(GraphEdgeKind.Transition, s1.Id, s2.Id)));
    }

    [Fact]
    public void LineStyle_RoundTripsInLayout_AndIsOmittedWhenNotSet()
    {
        var document = DiagramDocument.Create("D", DiagramType.Class);
        document.Layout.Add(new ElementLayout { ElementId = "r1", Line = new LineStyle { Color = "#1A73E8", Width = 3 } });
        document.Layout.Add(new ElementLayout { ElementId = "c1" });

        var json = DiagramSerializer.Serialize(document);
        var restored = DiagramSerializer.Deserialize(json);

        var line = restored.Layout.Find("r1")!.Line!;
        Assert.Equal(("#1A73E8", 3d), (line.Color, line.Width));
        Assert.Null(restored.Layout.Find("c1")!.Line);
        Assert.Equal(1, json.Split("\"line\"").Length - 1);
    }

    [Fact]
    public void CopyPaste_OfAConnector_KeepsItsLineStyle()
    {
        var (editor, _, _, relationship) = TwoClasses(RelationshipKind.Association);
        editor.SetLineStyle(relationship.Id, new LineStyle { Width = 3 });

        var pastedId = editor.Paste(editor.Copy(relationship.Id))!;

        Assert.Equal(3, editor.Layout.Find(pastedId)!.Line!.Width);
        editor.Layout.Find(pastedId)!.Line!.Width = 6;
        Assert.Equal(3, editor.Layout.Find(relationship.Id)!.Line!.Width);
    }

    [Fact]
    public void Relationship_IsDrawnInItsColourAndThickness()
    {
        var (editor, a, _, relationship) = TwoClasses(RelationshipKind.Association);
        editor.SelectedId = null;
        using var renderer = DiagramRenderer.Create(DiagramType.Class);
        var y = (int)renderer.GetElementBounds(editor.Document, a.Id)!.Value.MidY;

        using (var plain = Render(renderer, editor.Document))
        {
            // A thin, anti-aliased line: its centre is drawn, the pixels beside it are not.
            Assert.NotEqual(RenderTheme.Light.CanvasBackground, plain.GetPixel(300, y));
            Assert.Equal(RenderTheme.Light.CanvasBackground, plain.GetPixel(300, y + 3));
        }

        editor.SetLineStyle(relationship.Id, new LineStyle { Color = "#C62828", Width = 8 });
        using var styled = Render(renderer, editor.Document);
        Assert.Equal(Red, styled.GetPixel(300, y));
        Assert.Equal(Red, styled.GetPixel(300, y + 3));
        Assert.Equal(Red, styled.GetPixel(300, y - 3));
    }

    [Fact]
    public void SelectedThickLine_StaysAtLeastAsThickAsItsStyle()
    {
        var (editor, a, _, relationship) = TwoClasses(RelationshipKind.Association);
        editor.SetLineStyle(relationship.Id, new LineStyle { Width = 8 });
        using var renderer = DiagramRenderer.Create(DiagramType.Class);
        var y = (int)renderer.GetElementBounds(editor.Document, a.Id)!.Value.MidY;

        using var bitmap = Render(renderer, editor.Document, selectedId: relationship.Id);

        Assert.Equal(RenderTheme.Light.Selection, bitmap.GetPixel(300, y + 3));
    }

    private static (DiagramEditor Editor, UmlClass A, UmlClass B, UmlRelationship Relationship) TwoClasses(RelationshipKind kind)
    {
        var editor = new DiagramEditor(DiagramDocument.Create("T", DiagramType.Class));
        var a = editor.AddClass(ClassifierKind.Class, 20, 20);
        var b = editor.AddClass(ClassifierKind.Class, 420, 20);
        return (editor, a, b, editor.AddRelationship(kind, a.Id, b.Id)!);
    }

    private static SKBitmap Render(DiagramRenderer renderer, DiagramDocument document, string? selectedId = null)
    {
        var bitmap = new SKBitmap(640, 200);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(renderer.Theme.CanvasBackground);
        renderer.Render(canvas, document, new RenderOptions { SelectedId = selectedId });
        canvas.Flush();
        return bitmap;
    }
}
