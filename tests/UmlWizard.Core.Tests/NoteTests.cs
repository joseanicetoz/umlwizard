using SkiaSharp;
using UmlWizard.Core.Editing;
using UmlWizard.Core.Model;
using UmlWizard.Core.Rendering;
using UmlWizard.Core.Serialization;

namespace UmlWizard.Core.Tests;

public sealed class NoteTests
{
    private static DiagramEditor Editor(DiagramType type = DiagramType.Class) => new(DiagramDocument.Create("Test", type));

    [Theory]
    [InlineData(DiagramType.Class)]
    [InlineData(DiagramType.Sequence)]
    public void AddNote_WorksInEveryDiagramType_AndIsUndoable(DiagramType type)
    {
        var editor = Editor(type);

        var note = editor.AddNote(30, 40);

        Assert.Same(note, Assert.Single(editor.Model.Notes));
        Assert.Empty(editor.Model.TextBoxes);
        Assert.Equal("Note", note.Text);
        var layout = editor.Layout.Find(note.Id)!;
        Assert.Equal((30, 40, NoteElement.DefaultWidth, NoteElement.DefaultHeight), (layout.X, layout.Y, layout.Width, layout.Height));
        Assert.Same(note, editor.Model.FindElement(note.Id));

        editor.Undo();
        Assert.Empty(editor.Model.Notes);
    }

    [Fact]
    public void Note_IsFormattedMovedResizedEditedAndDeleted_LikeATextBox()
    {
        var editor = Editor();
        var note = editor.AddNote(0, 0);
        note.HorizontalAlignment = HorizontalTextAlignment.Center;
        note.VerticalAlignment = VerticalTextAlignment.Middle;
        editor.SetFont(note.Id, new DiagramFont { Size = 16, Italic = true });

        editor.MoveElementTo(note.Id, 50, 60);
        editor.ResizeElement(note.Id, 220, 100);
        editor.BeginInlineEdit(new HitInfo(note.Id, HitPart.Text))!.Commit("Remember:\nvalidate input");

        var loaded = DiagramSerializer.Deserialize(DiagramSerializer.Serialize(editor.Document));
        var copy = Assert.Single(loaded.Model.Notes);
        Assert.Empty(loaded.Model.TextBoxes);
        Assert.Equal(("Remember:\nvalidate input", HorizontalTextAlignment.Center, VerticalTextAlignment.Middle),
            (copy.Text, copy.HorizontalAlignment, copy.VerticalAlignment));
        var layout = loaded.Layout.Find(copy.Id)!;
        Assert.Equal((50, 60, 220, 100, 16.0, true), (layout.X, layout.Y, layout.Width, layout.Height, layout.Font!.Size, layout.Font.Italic));

        Assert.Equal([note.Id], editor.Delete(note.Id));
        Assert.Empty(editor.Model.Notes);
    }

    [Fact]
    public void DiagramsSavedWithoutNotes_StillLoad()
    {
        var root = System.Text.Json.Nodes.JsonNode.Parse(DiagramSerializer.Serialize(DiagramDocument.Create("Old", DiagramType.Sequence)))!;
        root["model"]!.AsObject().Remove("notes");

        Assert.Empty(DiagramSerializer.Deserialize(root.ToJsonString()).Model.Notes);
    }

    [Fact]
    public void CopiedNote_PastesAsANote_IntoAnyDiagramType()
    {
        var source = Editor(DiagramType.Sequence);
        var note = source.AddNote(0, 0);
        note.Text = "Shared note";
        var target = Editor(DiagramType.Class);

        target.Paste(source.Copy(note.Id), 10, 20);

        var pasted = Assert.Single(target.Model.Notes);
        Assert.Empty(target.Model.TextBoxes);
        Assert.NotEqual(note.Id, pasted.Id);
        Assert.Equal("Shared note", pasted.Text);
    }

    [Fact]
    public void HitTest_FindsNotes_WithTextBoxesAboveThem()
    {
        var editor = Editor();
        var note = editor.AddNote(0, 0);
        var textBox = editor.AddTextBox(100, 50);
        using var renderer = DiagramRenderer.Create(DiagramType.Class);

        Assert.Equal(new HitInfo(note.Id, HitPart.Text), renderer.HitTest(editor.Document, new SKPoint(20, 20)));
        Assert.Equal(textBox.Id, renderer.HitTest(editor.Document, new SKPoint(120, 70))?.ElementId);
        Assert.Equal(new HitInfo(note.Id, HitPart.ResizeHandle), renderer.HitTest(editor.Document, new SKPoint(160, 80), note.Id));
    }

    [Fact]
    public void Note_IsDrawnWithAFoldedCorner_InExportsToo()
    {
        var editor = Editor();
        var note = editor.AddNote(0, 0);
        note.Text = string.Empty;
        editor.ResizeElement(note.Id, 200, 100);
        using var renderer = DiagramRenderer.Create(DiagramType.Class);

        using var bitmap = Render(renderer, editor.Document, 220, 120);

        Assert.Equal(RenderTheme.Light.NoteFill, bitmap.GetPixel(100, 60));
        Assert.Equal(RenderTheme.Light.NoteFold, bitmap.GetPixel(193, 7));
        Assert.Equal(SKColors.White, bitmap.GetPixel(198, 2)); // cut off by the fold
    }

    [Fact]
    public void RightAlignedNoteText_StaysClearOfTheFold()
    {
        var editor = Editor();
        var note = editor.AddNote(0, 0);
        note.Text = "Right";
        note.HorizontalAlignment = HorizontalTextAlignment.Right;
        editor.ResizeElement(note.Id, 200, 100);
        using var renderer = DiagramRenderer.Create(DiagramType.Class);

        using var bitmap = Render(renderer, editor.Document, 220, 120);

        // Text pixels are dark; the note's border and fold are excluded by looking inside the note only.
        var textRight = 0;
        for (var y = 3; y < 97; y++)
        {
            for (var x = 3; x < 197; x++)
            {
                var c = bitmap.GetPixel(x, y);
                if (c.Red < 120 && c.Green < 120 && c.Blue < 120 && !(x > 185 && y < 16))
                {
                    textRight = Math.Max(textRight, x);
                }
            }
        }

        Assert.InRange(textRight, 150, 200 - 12);
    }

    private static SKBitmap Render(DiagramRenderer renderer, DiagramDocument document, int width, int height)
    {
        var bitmap = new SKBitmap(width, height);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.White);
        renderer.Render(canvas, document, new RenderOptions());
        canvas.Flush();
        return bitmap;
    }
}
