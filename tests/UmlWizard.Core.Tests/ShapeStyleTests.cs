using SkiaSharp;
using UmlWizard.Core.Editing;
using UmlWizard.Core.Model;
using UmlWizard.Core.Rendering;
using UmlWizard.Core.Serialization;

namespace UmlWizard.Core.Tests;

public sealed class ShapeStyleTests
{
    private static readonly SKColor Red = new(0xC6, 0x28, 0x28);
    private static readonly SKColor Pink = new(0xFD, 0xE7, 0xE7);

    [Fact]
    public void SetShapeStyle_IsUndoable_AndEmptyStyleRestoresDefaults()
    {
        var editor = new DiagramEditor(DiagramDocument.Create("T", DiagramType.Class));
        var cls = editor.AddClass(ClassifierKind.Class, 0, 0);
        var style = new ShapeStyle { Fill = "#FDE7E7", Border = "#C62828", BorderWidth = 3 };

        editor.SetShapeStyle(cls.Id, style);
        style.Fill = "#000000";
        Assert.Equal("#FDE7E7", editor.Layout.Find(cls.Id)!.Style!.Fill);

        editor.SetShapeStyle(cls.Id, new ShapeStyle());
        Assert.Null(editor.Layout.Find(cls.Id)!.Style);

        editor.Undo();
        Assert.Equal(3, editor.Layout.Find(cls.Id)!.Style!.BorderWidth);
        editor.Undo();
        Assert.Null(editor.Layout.Find(cls.Id)!.Style);
    }

    [Fact]
    public void OnlyShapes_HaveAStyle()
    {
        var editor = new DiagramEditor(DiagramDocument.Create("T", DiagramType.Class));
        var a = editor.AddClass(ClassifierKind.Class, 0, 0);
        var b = editor.AddClass(ClassifierKind.Class, 300, 0);
        var relationship = editor.AddRelationship(RelationshipKind.Association, a.Id, b.Id)!;

        Assert.True(DiagramEditor.HasShape(a));
        Assert.True(DiagramEditor.HasShape(editor.AddNote(0, 200)));
        Assert.True(DiagramEditor.HasShape(editor.AddTextBox(0, 300)));
        Assert.False(DiagramEditor.HasShape(relationship));

        editor.SetShapeStyle(relationship.Id, new ShapeStyle { Fill = "#FF0000" });
        Assert.Null(editor.Layout.Find(relationship.Id));
    }

    [Fact]
    public void Style_RoundTripsInLayout_AndIsOmittedWhenNotSet()
    {
        var document = DiagramDocument.Create("D", DiagramType.Class);
        document.Layout.Add(new ElementLayout { ElementId = "c1", Style = new ShapeStyle { Fill = "#E4F3E8", BorderWidth = 2.5 } });
        document.Layout.Add(new ElementLayout { ElementId = "c2" });

        var json = DiagramSerializer.Serialize(document);
        var restored = DiagramSerializer.Deserialize(json);

        var style = restored.Layout.Find("c1")!.Style!;
        Assert.Equal(("#E4F3E8", null, 2.5), (style.Fill, style.Border, style.BorderWidth));
        Assert.Null(restored.Layout.Find("c2")!.Style);
        Assert.Equal(1, json.Split("\"style\"").Length - 1);
        Assert.DoesNotContain("\"border\"", json);
    }

    [Fact]
    public void CopyPaste_KeepsStyleAsIndependentCopy()
    {
        var editor = new DiagramEditor(DiagramDocument.Create("T", DiagramType.Class));
        var cls = editor.AddClass(ClassifierKind.Class, 0, 0);
        editor.SetShapeStyle(cls.Id, new ShapeStyle { Fill = "#FDE7E7" });

        var pastedId = editor.Paste(editor.Copy(cls.Id))!;
        editor.Layout.Find(pastedId)!.Style!.Fill = "#000000";

        Assert.Equal("#FDE7E7", editor.Layout.Find(cls.Id)!.Style!.Fill);
    }

    [Fact]
    public void DefaultFill_FollowsTheScheme()
    {
        var theme = RenderTheme.Light;

        Assert.Equal(theme.InterfaceHeader, theme.DefaultFill(new UmlClass { Kind = ClassifierKind.Interface }));
        Assert.Equal(theme.ClassHeader, theme.DefaultFill(new UmlClass()));
        Assert.Equal(theme.UseCaseFill, theme.DefaultFill(new GraphNode { Kind = GraphNodeKind.UseCase }));
        Assert.Equal(theme.Line, theme.DefaultFill(new GraphNode { Kind = GraphNodeKind.InitialState }));
        Assert.Equal(theme.LifelineHead, theme.DefaultFill(new Lifeline()));
        Assert.Equal(theme.NodeFill, theme.DefaultFill(new Lifeline { Kind = LifelineKind.Actor }));
        Assert.Equal(theme.NoteFill, theme.DefaultFill(new NoteElement()));
        Assert.Null(theme.DefaultFill(new TextBoxElement()));
        Assert.Null(theme.DefaultBorder(new TextBoxElement()));
        Assert.Equal(theme.Border, theme.DefaultBorder(new NoteElement()));
    }

    [Fact]
    public void Note_IsDrawnWithItsFillAndBorder()
    {
        var editor = new DiagramEditor(DiagramDocument.Create("T", DiagramType.Class));
        var note = editor.AddNote(20, 20);
        note.Text = string.Empty;
        editor.ResizeElement(note.Id, 200, 100);
        editor.SelectedId = null;
        editor.SetShapeStyle(note.Id, new ShapeStyle { Fill = "#FDE7E7", Border = "#C62828", BorderWidth = 6 });

        using var bitmap = Render(editor.Document);

        Assert.Equal(Pink, bitmap.GetPixel(120, 70));
        Assert.Equal(Red, bitmap.GetPixel(21, 70));
    }

    [Fact]
    public void Class_WithCustomFill_IsFilledEntirely_OtherwiseItsHeaderIsTintedByKind()
    {
        var editor = new DiagramEditor(DiagramDocument.Create("T", DiagramType.Class));
        var plain = editor.AddClass(ClassifierKind.Interface, 20, 20);
        var filled = editor.AddClass(ClassifierKind.Class, 300, 20);
        editor.SelectedId = null;
        editor.SetShapeStyle(filled.Id, new ShapeStyle { Fill = "#FDE7E7" });

        using var renderer = DiagramRenderer.Create(DiagramType.Class);
        var plainBottom = (int)renderer.GetElementBounds(editor.Document, plain.Id)!.Value.Bottom;
        var filledBottom = (int)renderer.GetElementBounds(editor.Document, filled.Id)!.Value.Bottom;
        using var bitmap = Render(editor.Document);

        // Just inside the top-left corner (the header) and the bottom-left corner (the operations).
        Assert.Equal(RenderTheme.Light.InterfaceHeader, bitmap.GetPixel(24, 24));
        Assert.Equal(RenderTheme.Light.NodeFill, bitmap.GetPixel(24, plainBottom - 4));
        Assert.Equal(Pink, bitmap.GetPixel(304, 24));
        Assert.Equal(Pink, bitmap.GetPixel(304, filledBottom - 4));
    }

    [Fact]
    public void TextBox_HasNoFillOrBorder_UntilItIsGivenOne()
    {
        var editor = new DiagramEditor(DiagramDocument.Create("T", DiagramType.Class));
        var textBox = editor.AddTextBox(20, 20);
        textBox.Text = string.Empty;
        editor.ResizeElement(textBox.Id, 200, 100);
        editor.SelectedId = null;

        using (var plain = Render(editor.Document))
        {
            Assert.Equal(RenderTheme.Light.CanvasBackground, plain.GetPixel(120, 70));
            Assert.Equal(RenderTheme.Light.CanvasBackground, plain.GetPixel(20, 70));
        }

        editor.SetShapeStyle(textBox.Id, new ShapeStyle { Fill = "#FDE7E7", Border = "#C62828", BorderWidth = 4 });
        using var styled = Render(editor.Document);
        Assert.Equal(Pink, styled.GetPixel(120, 70));
        Assert.Equal(Red, styled.GetPixel(20, 70));
    }

    [Fact]
    public void GraphNode_IsDrawnWithItsFill()
    {
        var editor = new DiagramEditor(DiagramDocument.Create("T", DiagramType.StateMachine));
        var state = editor.AddNode(GraphNodeKind.State, 20, 20);
        state.Name = string.Empty;
        editor.SelectedId = null;
        var layout = editor.Layout.Find(state.Id)!;
        var center = (X: (int)(layout.X + layout.Width / 2), Y: (int)(layout.Y + layout.Height / 2));

        using (var plain = Render(editor.Document))
        {
            Assert.Equal(RenderTheme.Light.StateFill, plain.GetPixel(center.X, center.Y));
        }

        editor.SetShapeStyle(state.Id, new ShapeStyle { Fill = "#FDE7E7" });
        using var styled = Render(editor.Document);
        Assert.Equal(Pink, styled.GetPixel(center.X, center.Y));
    }

    [Theory]
    [InlineData("#000000", false)]
    [InlineData("#1A73E8", false)]
    [InlineData("#FFFFFF", true)]
    [InlineData("#FFF8C5", true)]
    public void TextOnACustomFill_IsDarkOrLight_WhicheverReadsBetter(string fill, bool dark)
    {
        var (text, _) = ShapeColors.TextColorsOn(ShapeColors.Parse(fill)!.Value);

        Assert.Equal(dark ? RenderTheme.Light.Text : RenderTheme.Dark.Text, text);
    }

    [Theory]
    [InlineData("#C62828", true)]
    [InlineData(" #c62828 ", true)]
    [InlineData("C62828", false)]
    [InlineData("red", false)]
    [InlineData("#12", false)]
    [InlineData(null, false)]
    public void Parse_AcceptsHexColours(string? text, bool valid)
    {
        Assert.Equal(valid, ShapeColors.Parse(text) is not null);
    }

    private static SKBitmap Render(DiagramDocument document)
    {
        using var renderer = DiagramRenderer.Create(document.DiagramType);
        var bitmap = new SKBitmap(600, 300);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(renderer.Theme.CanvasBackground);
        renderer.Render(canvas, document, new RenderOptions());
        canvas.Flush();
        return bitmap;
    }
}
