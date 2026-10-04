using SkiaSharp;
using UmlWizard.Core.Editing;
using UmlWizard.Core.Model;
using UmlWizard.Core.Rendering;
using UmlWizard.Core.Serialization;

namespace UmlWizard.Core.Tests;

public sealed class NoteAttachmentTests
{
    private static (DiagramEditor Editor, NoteElement Note, UmlClass A, UmlClass B) ClassDiagram()
    {
        var editor = new DiagramEditor(DiagramDocument.Create("Test", DiagramType.Class));
        var a = editor.AddClass(ClassifierKind.Class, 0, 0);
        var b = editor.AddClass(ClassifierKind.Class, 400, 0);
        var note = editor.AddNote(0, 250);
        return (editor, note, a, b);
    }

    [Fact]
    public void AttachNote_WorksInEitherOrder_AndIsUndoable()
    {
        var (editor, note, a, b) = ClassDiagram();

        var first = editor.AttachNote(note.Id, a.Id)!;
        var second = editor.AttachNote(b.Id, note.Id)!;

        Assert.Equal((note.Id, a.Id), (first.NoteId, first.TargetId));
        Assert.Equal((note.Id, b.Id), (second.NoteId, second.TargetId));
        Assert.Equal(second.Id, editor.SelectedId);
        Assert.Same(second, editor.Model.FindElement(second.Id));

        editor.Undo();
        Assert.Single(editor.Model.NoteAttachments);
    }

    [Fact]
    public void AttachNote_RejectsInvalidPairs()
    {
        var (editor, note, a, b) = ClassDiagram();
        var textBox = editor.AddTextBox(300, 300);
        var otherNote = editor.AddNote(600, 300);
        editor.AttachNote(note.Id, a.Id);

        Assert.False(editor.CanAttachNote(note.Id, a.Id));        // already attached
        Assert.False(editor.CanAttachNote(a.Id, b.Id));           // no note
        Assert.False(editor.CanAttachNote(note.Id, textBox.Id));  // text boxes are not UML elements
        Assert.False(editor.CanAttachNote(note.Id, otherNote.Id));
        Assert.False(editor.CanAttachNote(note.Id, note.Id));
        Assert.Null(editor.AttachNote(a.Id, b.Id));
        Assert.Single(editor.Model.NoteAttachments);
    }

    [Fact]
    public void Attachments_GoWithTheirNote_TheirTarget_AndTargetsRemovedWithIt()
    {
        var (editor, note, a, b) = ClassDiagram();
        var relationship = editor.AddRelationship(RelationshipKind.Association, a.Id, b.Id)!;
        var toClass = editor.AttachNote(note.Id, b.Id)!;
        var toRelationship = editor.AttachNote(note.Id, relationship.Id)!;
        var other = editor.AttachNote(editor.AddNote(600, 250).Id, a.Id)!;

        // Deleting A also removes the relationship, and so both attachments that point at them.
        var removed = editor.Delete(a.Id);
        Assert.Contains(toRelationship.Id, removed);
        Assert.Contains(other.Id, removed);
        Assert.Equal([toClass.Id], editor.Model.NoteAttachments.Select(x => x.Id));

        editor.Delete(note.Id);
        Assert.Empty(editor.Model.NoteAttachments);
    }

    [Fact]
    public void DeletingAnAttachment_KeepsNoteAndTarget()
    {
        var (editor, note, a, _) = ClassDiagram();
        var attachment = editor.AttachNote(note.Id, a.Id)!;

        Assert.Equal([attachment.Id], editor.Delete(attachment.Id));
        Assert.NotNull(editor.Model.FindNote(note.Id));
        Assert.NotNull(editor.Model.FindElement(a.Id));
    }

    [Fact]
    public void Attachments_AreSaved_AndDanglingOnesAreDroppedOnLoad()
    {
        var (editor, note, a, _) = ClassDiagram();
        editor.AttachNote(note.Id, a.Id);
        editor.Model.NoteAttachments.Add(new NoteAttachment { NoteId = note.Id, TargetId = "missing" });

        var loaded = DiagramSerializer.Deserialize(DiagramSerializer.Serialize(editor.Document));

        var attachment = Assert.Single(loaded.Model.NoteAttachments);
        Assert.Equal((note.Id, a.Id), (attachment.NoteId, attachment.TargetId));
    }

    [Fact]
    public void Line_RunsBetweenTheEdgesOfNoteAndClass_AndCanBeHit()
    {
        var (editor, note, a, _) = ClassDiagram();
        var attachment = editor.AttachNote(note.Id, a.Id)!;
        using var renderer = DiagramRenderer.Create(DiagramType.Class);
        renderer.UpdateLayout(editor.Document);
        var classLayout = editor.Layout.Find(a.Id)!;
        var noteLayout = editor.Layout.Find(note.Id)!;

        Assert.True(renderer.TryGetAttachmentLine(editor.Document, attachment, out var start, out var end));
        Assert.Equal(noteLayout.Y, start.Y, 1);
        Assert.Equal(classLayout.Y + classLayout.Height, end.Y, 1);

        var middle = new SKPoint((start.X + end.X) / 2, (start.Y + end.Y) / 2);
        Assert.Equal(new HitInfo(attachment.Id, HitPart.NoteAttachment), renderer.HitTest(editor.Document, middle));
        Assert.False(renderer.HitTest(editor.Document, middle)!.IsNode);
    }

    [Fact]
    public void Line_EndsAtTheMiddleOfARelationship_OrOfAMessage()
    {
        var (editor, note, a, b) = ClassDiagram();
        var relationship = editor.AddRelationship(RelationshipKind.Association, a.Id, b.Id)!;
        var attachment = editor.AttachNote(note.Id, relationship.Id)!;
        using var classRenderer = DiagramRenderer.Create(DiagramType.Class);
        classRenderer.UpdateLayout(editor.Document);

        Assert.True(classRenderer.TryGetAttachmentLine(editor.Document, attachment, out _, out var end));
        var (la, lb) = (editor.Layout.Find(a.Id)!, editor.Layout.Find(b.Id)!);
        Assert.Equal(la.Y + la.Height / 2, end.Y, 1);
        Assert.Equal((la.X + la.Width + lb.X) / 2, end.X, 1);

        var sequence = new DiagramEditor(DiagramDocument.Create("Seq", DiagramType.Sequence));
        var x = sequence.AddLifeline(LifelineKind.Object, 0);
        var y = sequence.AddLifeline(LifelineKind.Object, 300);
        var message = sequence.AddMessage(MessageKind.Synchronous, x.Id, y.Id, 200)!;
        var seqNote = sequence.AddNote(100, 300);
        var toMessage = sequence.AttachNote(seqNote.Id, message.Id)!;
        using var sequenceRenderer = DiagramRenderer.Create(DiagramType.Sequence);
        sequenceRenderer.UpdateLayout(sequence.Document);

        Assert.True(sequenceRenderer.TryGetAttachmentLine(sequence.Document, toMessage, out _, out var messageEnd));
        Assert.Equal(sequence.Layout.Find(message.Id)!.Y, messageEnd.Y, 1);
        var (lx, ly) = (sequence.Layout.Find(x.Id)!, sequence.Layout.Find(y.Id)!);
        Assert.InRange(messageEnd.X, lx.X + lx.Width / 2, ly.X + ly.Width / 2);
    }

    [Fact]
    public void Line_IsDrawnDashed()
    {
        var (editor, note, a, _) = ClassDiagram();
        editor.AttachNote(note.Id, a.Id);
        editor.SelectedId = null;
        using var renderer = DiagramRenderer.Create(DiagramType.Class);
        renderer.UpdateLayout(editor.Document);
        var classLayout = editor.Layout.Find(a.Id)!;

        using var bitmap = new SKBitmap(500, 400);
        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(SKColors.White);
            renderer.Render(canvas, editor.Document, new RenderOptions());
        }

        // Walk down the line between class and note: it has both drawn and empty stretches.
        var x = (int)Math.Round((classLayout.X + classLayout.Width / 2 + NoteElement.DefaultWidth / 2) / 2);
        var column = Enumerable.Range((int)(classLayout.Y + classLayout.Height) + 4, 150)
            .Select(y => Enumerable.Range(x - 3, 7).Any(dx => bitmap.GetPixel(dx, y).Red < 200))
            .ToList();
        Assert.Contains(true, column);
        Assert.Contains(false, column);
    }
}
