using Avalonia.Headless.XUnit;
using Avalonia.Input;
using UmlWizard.App.ViewModels;
using UmlWizard.Core.Model;

namespace UmlWizard.App.Tests;

public sealed class NoteAttachmentCanvasTests
{
    private static ElementLayout Layout(DiagramHarness h, string id) => h.ViewModel.Document.Layout.Find(id)!;

    private static (double X, double Y) Center(DiagramHarness h, string id)
    {
        var layout = Layout(h, id);
        return (layout.X + layout.Width / 2, layout.Y + layout.Height / 2);
    }

    private static void UseAttachTool(DiagramHarness h) =>
        h.ViewModel.ActiveTool = h.ViewModel.Tools.Single(t => t.Kind == ToolKind.NoteAttachment);

    [Fact]
    public void AttachNoteTool_IsInBothToolboxes_AsAConnectorTool()
    {
        Assert.Contains(ToolItem.For(DiagramType.Class), t => t.Kind == ToolKind.NoteAttachment && t.IsConnectorTool);
        Assert.Contains(ToolItem.For(DiagramType.Sequence), t => t.Kind == ToolKind.NoteAttachment && t.IsConnectorTool);
    }

    [AvaloniaFact]
    public void DraggingFromANoteToAClass_AttachesThem_AndSoDoesTheOtherWayRound()
    {
        var h = new DiagramHarness(DiagramType.Class);
        var a = h.ViewModel.Editor.AddClass(ClassifierKind.Class, 60, 60);
        var b = h.ViewModel.Editor.AddClass(ClassifierKind.Class, 500, 60);
        var note = h.ViewModel.Editor.AddNote(250, 350);
        h.ViewModel.Renderer.UpdateLayout(h.ViewModel.Document);
        var (nx, ny) = Center(h, note.Id);
        var (ax, ay) = Center(h, a.Id);
        var (bx, by) = Center(h, b.Id);

        UseAttachTool(h);
        h.Drag(nx, ny, ax, ay);
        UseAttachTool(h);
        h.Drag(bx, by, nx, ny);

        Assert.Equal([(note.Id, a.Id), (note.Id, b.Id)],
            h.ViewModel.Document.Model.NoteAttachments.Select(x => (x.NoteId, x.TargetId)));
        Assert.Equal(ToolKind.Select, h.ViewModel.ActiveTool.Kind);
    }

    [AvaloniaFact]
    public void DraggingBetweenTwoClasses_WithTheAttachTool_DoesNothing()
    {
        var h = new DiagramHarness(DiagramType.Class);
        var a = h.ViewModel.Editor.AddClass(ClassifierKind.Class, 60, 60);
        var b = h.ViewModel.Editor.AddClass(ClassifierKind.Class, 500, 60);
        h.ViewModel.Renderer.UpdateLayout(h.ViewModel.Document);
        var (ax, ay) = Center(h, a.Id);
        var (bx, by) = Center(h, b.Id);

        UseAttachTool(h);
        h.Drag(ax, ay, bx, by);

        Assert.Empty(h.ViewModel.Document.Model.NoteAttachments);
        Assert.Empty(((ClassDiagramModel)h.ViewModel.Document.Model).Relationships);
    }

    [AvaloniaFact]
    public void InASequenceDiagram_ANoteAttachesToALifelineAtAnotherHeight()
    {
        var h = new DiagramHarness(DiagramType.Sequence);
        var lifeline = h.ViewModel.Editor.AddLifeline(LifelineKind.Object, 100);
        var note = h.ViewModel.Editor.AddNote(400, 300);
        h.ViewModel.Renderer.UpdateLayout(h.ViewModel.Document);
        var (lx, ly) = Center(h, lifeline.Id);
        var (nx, ny) = Center(h, note.Id);

        UseAttachTool(h);
        h.Drag(nx, ny, lx, ly);

        var attachment = Assert.Single(h.ViewModel.Document.Model.NoteAttachments);
        Assert.Equal((note.Id, lifeline.Id), (attachment.NoteId, attachment.TargetId));
    }

    [AvaloniaFact]
    public void ASelectedAttachment_IsDeletedWithTheDeleteKey_AndHasNoFont()
    {
        var h = new DiagramHarness(DiagramType.Class);
        var a = h.ViewModel.Editor.AddClass(ClassifierKind.Class, 60, 60);
        var note = h.ViewModel.Editor.AddNote(60, 350);
        var attachment = h.ViewModel.Editor.AttachNote(note.Id, a.Id)!;
        h.ViewModel.Renderer.UpdateLayout(h.ViewModel.Document);
        Assert.True(h.ViewModel.Renderer.TryGetAttachmentLine(h.ViewModel.Document, attachment, out var start, out var end));

        h.Click((start.X + end.X) / 2, (start.Y + end.Y) / 2);
        Assert.Equal(attachment.Id, h.ViewModel.Editor.SelectedId);
        Assert.Equal("Selected: Note attachment", h.ViewModel.StatusText.Split(" — ")[0]);
        Assert.False(h.ViewModel.EditFontCommand.CanExecute(null));

        h.Press(Key.Delete);

        Assert.Empty(h.ViewModel.Document.Model.NoteAttachments);
        Assert.Single(h.ViewModel.Document.Model.Notes);
    }
}
