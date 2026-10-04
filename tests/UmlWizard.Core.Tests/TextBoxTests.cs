using SkiaSharp;
using UmlWizard.Core.Editing;
using UmlWizard.Core.Model;
using UmlWizard.Core.Rendering;
using UmlWizard.Core.Serialization;

namespace UmlWizard.Core.Tests;

public sealed class TextBoxTests
{
    private static DiagramEditor Editor(DiagramType type = DiagramType.Class) => new(DiagramDocument.Create("Test", type));

    [Theory]
    [InlineData(DiagramType.Class)]
    [InlineData(DiagramType.Sequence)]
    public void AddTextBox_WorksInEveryDiagramType_AndIsUndoable(DiagramType type)
    {
        var editor = Editor(type);

        var textBox = editor.AddTextBox(50, 60);

        Assert.Same(textBox, Assert.Single(editor.Model.TextBoxes));
        Assert.Same(textBox, editor.Model.FindElement(textBox.Id));
        var layout = editor.Layout.Find(textBox.Id)!;
        Assert.Equal((50, 60, TextBoxElement.DefaultWidth, TextBoxElement.DefaultHeight), (layout.X, layout.Y, layout.Width, layout.Height));
        Assert.Equal(textBox.Id, editor.SelectedId);

        editor.Undo();
        Assert.Empty(editor.Model.TextBoxes);
    }

    [Fact]
    public void TextBox_SurvivesSaving_WithTextAlignmentFontAndSize()
    {
        var editor = Editor(DiagramType.Sequence);
        var textBox = editor.AddTextBox(10, 20);
        textBox.Text = "First line\nSecond line";
        textBox.HorizontalAlignment = HorizontalTextAlignment.Right;
        textBox.VerticalAlignment = VerticalTextAlignment.Bottom;
        editor.SetFont(textBox.Id, new DiagramFont { Family = "Arial", Size = 18, Bold = true, Italic = true });
        editor.ResizeElement(textBox.Id, 300, 120);

        var loaded = DiagramSerializer.Deserialize(DiagramSerializer.Serialize(editor.Document));

        var copy = Assert.Single(loaded.Model.TextBoxes);
        Assert.Equal(("First line\nSecond line", HorizontalTextAlignment.Right, VerticalTextAlignment.Bottom),
            (copy.Text, copy.HorizontalAlignment, copy.VerticalAlignment));
        var layout = loaded.Layout.Find(copy.Id)!;
        Assert.Equal((300, 120), (layout.Width, layout.Height));
        Assert.Equal(("Arial", 18.0, true, true), (layout.Font!.Family, layout.Font.Size, layout.Font.Bold, layout.Font.Italic));
    }

    [Fact]
    public void DiagramsSavedWithoutTextBoxes_StillLoad()
    {
        var root = System.Text.Json.Nodes.JsonNode.Parse(DiagramSerializer.Serialize(DiagramDocument.Create("Old", DiagramType.Class)))!;
        root["model"]!.AsObject().Remove("textBoxes");
        var json = root.ToJsonString();
        Assert.DoesNotContain("textBoxes", json);

        Assert.Empty(DiagramSerializer.Deserialize(json).Model.TextBoxes);
    }

    [Fact]
    public void Resize_ChangesWidthAndHeight_DownToAMinimum_WhileClassesOnlyChangeWidth()
    {
        var editor = Editor();
        var textBox = editor.AddTextBox(0, 0);
        var cls = editor.AddClass(ClassifierKind.Class, 300, 0);
        var classHeight = editor.Layout.Find(cls.Id)!.Height;

        editor.ResizeElement(textBox.Id, 250, 90);
        Assert.Equal((250, 90), (editor.Layout.Find(textBox.Id)!.Width, editor.Layout.Find(textBox.Id)!.Height));

        editor.ResizeElement(textBox.Id, 1, 1);
        Assert.Equal((TextBoxElement.MinWidth, TextBoxElement.MinHeight), (editor.Layout.Find(textBox.Id)!.Width, editor.Layout.Find(textBox.Id)!.Height));

        editor.ResizeElement(cls.Id, 200, 500);
        Assert.Equal((200, classHeight), (editor.Layout.Find(cls.Id)!.Width, editor.Layout.Find(cls.Id)!.Height));
    }

    [Fact]
    public void Move_And_Delete()
    {
        var editor = Editor();
        var textBox = editor.AddTextBox(0, 0);

        editor.MoveElementTo(textBox.Id, 70, 80);
        Assert.Equal((70, 80), (editor.Layout.Find(textBox.Id)!.X, editor.Layout.Find(textBox.Id)!.Y));

        Assert.Equal([textBox.Id], editor.Delete(textBox.Id));
        Assert.Empty(editor.Model.TextBoxes);
        Assert.Null(editor.Layout.Find(textBox.Id));
    }

    [Fact]
    public void InlineEdit_KeepsLineBreaks()
    {
        var editor = Editor();
        var textBox = editor.AddTextBox(0, 0);

        var session = editor.BeginInlineEdit(new HitInfo(textBox.Id, HitPart.Text))!;
        Assert.Equal("Text", session.InitialText);
        session.Commit("Note:\nkeep it short\n");

        Assert.Equal("Note:\nkeep it short", textBox.Text);
        editor.Undo();
        Assert.Equal("Text", Assert.Single(editor.Model.TextBoxes).Text);
    }

    [Fact]
    public void CopiedTextBox_PastesIntoAnyDiagramType()
    {
        var source = Editor(DiagramType.Class);
        var textBox = source.AddTextBox(10, 10);
        textBox.Text = "Shared";
        var clipboard = source.Copy(textBox.Id);
        var target = Editor(DiagramType.Sequence);

        Assert.True(target.CanPaste(clipboard));
        var pastedId = target.Paste(clipboard, 100, 120);

        var pasted = Assert.Single(target.Model.TextBoxes);
        Assert.Equal(pastedId, pasted.Id);
        Assert.NotEqual(textBox.Id, pasted.Id);
        Assert.Equal("Shared", pasted.Text);
        Assert.Equal((100, 120), (target.Layout.Find(pasted.Id)!.X, target.Layout.Find(pasted.Id)!.Y));

        // Other elements still only paste into their own diagram type.
        Assert.False(target.CanPaste(source.Copy(source.AddClass(ClassifierKind.Class, 0, 0).Id)));
    }

    [Fact]
    public void Text_WrapsToTheBoxWidth_AtLineBreaksAndBetweenWords()
    {
        var editor = Editor();
        var textBox = editor.AddTextBox(0, 0);
        textBox.Text = "alpha beta gamma delta epsilon\nzeta";
        editor.ResizeElement(textBox.Id, 90, 200);
        using var renderer = DiagramRenderer.Create(DiagramType.Class);

        var lines = renderer.GetTextLines(editor.Document, textBox.Id);

        Assert.True(lines.Count > 2, string.Join("|", lines));
        Assert.Equal("zeta", lines[^1]);
        Assert.Equal("alpha beta gamma delta epsilon", string.Join(" ", lines.Take(lines.Count - 1)));
    }

    [Fact]
    public void HitTest_FindsTextBoxAboveClasses_AndItsResizeHandleWhenSelected()
    {
        var editor = Editor();
        var cls = editor.AddClass(ClassifierKind.Class, 100, 100);
        var textBox = editor.AddTextBox(120, 110);
        editor.ResizeElement(textBox.Id, 100, 50);
        using var renderer = DiagramRenderer.Create(DiagramType.Class);
        renderer.UpdateLayout(editor.Document);

        var inside = renderer.HitTest(editor.Document, new SKPoint(150, 130));
        Assert.Equal(new HitInfo(textBox.Id, HitPart.Text), inside);
        Assert.False(inside!.IsNode);
        Assert.Equal(cls.Id, renderer.HitTest(editor.Document, new SKPoint(105, 105))?.ElementId);
        Assert.Equal(new HitInfo(textBox.Id, HitPart.ResizeHandle), renderer.HitTest(editor.Document, new SKPoint(220, 160), textBox.Id));
        Assert.Equal(SKRect.Create(120, 110, 100, 50), renderer.GetEditRect(editor.Document, textBox.Id, HitPart.Text));
    }

    [Theory]
    [InlineData(HorizontalTextAlignment.Left, VerticalTextAlignment.Top)]
    [InlineData(HorizontalTextAlignment.Center, VerticalTextAlignment.Middle)]
    [InlineData(HorizontalTextAlignment.Right, VerticalTextAlignment.Bottom)]
    public void Text_IsDrawnWhereItsAlignmentPutsIt(HorizontalTextAlignment horizontal, VerticalTextAlignment vertical)
    {
        var editor = Editor();
        var textBox = editor.AddTextBox(0, 0);
        textBox.Text = "Hi";
        textBox.HorizontalAlignment = horizontal;
        textBox.VerticalAlignment = vertical;
        editor.ResizeElement(textBox.Id, 300, 150);
        using var renderer = DiagramRenderer.Create(DiagramType.Class);

        using var bitmap = new SKBitmap(300, 150);
        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(SKColors.White);
            renderer.Render(canvas, editor.Document, new RenderOptions());
        }

        var ink = InkBounds(bitmap);
        Assert.False(ink.IsEmpty, "No text was drawn.");
        Assert.Equal(horizontal, (ink.MidX / 100f) switch { < 1 => HorizontalTextAlignment.Left, < 2 => HorizontalTextAlignment.Center, _ => HorizontalTextAlignment.Right });
        Assert.Equal(vertical, (ink.MidY / 50f) switch { < 1 => VerticalTextAlignment.Top, < 2 => VerticalTextAlignment.Middle, _ => VerticalTextAlignment.Bottom });
    }

    [Fact]
    public void ContentBounds_IncludeTextBoxes_AndOutlinesAreOnlyDrawnWhenAsked()
    {
        var editor = Editor();
        var textBox = editor.AddTextBox(500, 400);
        textBox.Text = string.Empty;
        using var renderer = DiagramRenderer.Create(DiagramType.Class);

        Assert.Equal(SKRect.Create(500, 400, 160, 60), renderer.GetContentBounds(editor.Document));

        using var bitmap = new SKBitmap(800, 600);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.White);
        renderer.Render(canvas, editor.Document, new RenderOptions());
        canvas.Flush();
        Assert.True(InkBounds(bitmap).IsEmpty);

        renderer.Render(canvas, editor.Document, new RenderOptions { ShowTextBoxOutlines = true });
        canvas.Flush();
        Assert.Equal(SKRect.Create(500, 400, 160, 60), InkBounds(bitmap), new RectTolerance(1f));
    }

    [Fact]
    public void DiagramAuthor_IsSaved_AndRestoredByUndo()
    {
        var editor = Editor();
        editor.Document.Author = "Ada";
        Assert.Equal("Ada", DiagramSerializer.Deserialize(DiagramSerializer.Serialize(editor.Document)).Author);

        editor.Execute(() => editor.Document.Author = "Grace");
        editor.Undo();

        Assert.Equal("Ada", editor.Document.Author);
    }

    /// <summary>Bounds of all pixels that are not (almost) white.</summary>
    private static SKRect InkBounds(SKBitmap bitmap)
    {
        var bounds = SKRect.Empty;
        for (var y = 0; y < bitmap.Height; y++)
        {
            for (var x = 0; x < bitmap.Width; x++)
            {
                var c = bitmap.GetPixel(x, y);
                if (c.Red < 240 || c.Green < 240 || c.Blue < 240)
                {
                    var pixel = SKRect.Create(x, y, 1, 1);
                    bounds = bounds.IsEmpty ? pixel : SKRect.Union(bounds, pixel);
                }
            }
        }

        return bounds;
    }
}
