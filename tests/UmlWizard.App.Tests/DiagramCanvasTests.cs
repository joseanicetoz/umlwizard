using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using UmlWizard.App.ViewModels;
using UmlWizard.App.ViewModels.Dialogs;
using UmlWizard.Core.Model;

namespace UmlWizard.App.Tests;

public sealed class DiagramCanvasTests
{
    [AvaloniaFact]
    public void DoubleClickOnClassName_EditsNameInline()
    {
        var h = new DiagramHarness(DiagramType.Class);
        var cls = h.ViewModel.Editor.AddClass(ClassifierKind.Class, 100, 100);

        h.DoubleClick(150, 112);
        Assert.True(h.InlineEditor.IsVisible);
        Assert.Equal(cls.Name, h.InlineEditor.Text);

        h.Type("Customer");
        h.Press(Key.Enter);

        Assert.Equal("Customer", cls.Name);
        Assert.False(h.InlineEditor.IsVisible);
        Assert.True(h.ViewModel.IsDirty);
    }

    [AvaloniaFact]
    public void DoubleClickOnEmptyAttributes_AddsMembersInSuccession()
    {
        var h = new DiagramHarness(DiagramType.Class);
        var cls = h.ViewModel.Editor.AddClass(ClassifierKind.Class, 100, 100);
        var layout = h.ViewModel.Document.Layout.Find(cls.Id)!;
        h.ViewModel.Renderer.UpdateLayout(h.ViewModel.Document);

        // Header is 30px tall (one line); the empty attribute compartment follows.
        h.DoubleClick(150, layout.Y + 36);
        h.Type("+id: int");
        h.Press(Key.Enter);
        h.Type("-name: string = \"\"");
        h.Press(Key.Enter);
        h.Press(Key.Escape);

        Assert.Collection(cls.Attributes,
            a => Assert.Equal(("id", "int", UmlVisibility.Public), (a.Name, a.Type, a.Visibility)),
            a => Assert.Equal(("name", "string", UmlVisibility.Private, "\"\""), (a.Name, a.Type, a.Visibility, a.DefaultValue)));
    }

    [AvaloniaFact]
    public void RightClickOnElement_OffersCutCopyEdit_AndEditOpensClassDialog()
    {
        var h = new DiagramHarness(DiagramType.Class);
        var cls = h.ViewModel.Editor.AddClass(ClassifierKind.Class, 100, 100);
        h.Services.AcceptDialogs = true;

        h.Click(150, 112, MouseButton.Right);

        var items = h.Canvas.LastContextMenu!.Items.OfType<MenuItem>().ToList();
        Assert.Equal(
            ["Cut", "Copy", "Edit…", "Font…", "Shape…", "Bring to Front", "Bring Forward", "Send Backward", "Send to Back"],
            items.Select(i => (string)i.Header!));

        // The only class is already both front-most and back-most.
        Assert.All(items.Skip(5), i => Assert.False(i.IsEnabled));

        items[2].RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        Dispatcher.UIThread.RunJobs();

        var dialog = Assert.IsType<ClassEditorViewModel>(Assert.Single(h.Services.ShownDialogs));
        Assert.Equal(cls.Name, dialog.Name);
    }

    [AvaloniaFact]
    public void CutThenPasteFromBackgroundMenu_MovesClass()
    {
        var h = new DiagramHarness(DiagramType.Class);
        var cls = h.ViewModel.Editor.AddClass(ClassifierKind.Class, 100, 100);

        h.Click(150, 112, MouseButton.Right);
        h.Canvas.LastContextMenu!.Items.OfType<MenuItem>().First().RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        Dispatcher.UIThread.RunJobs();
        Assert.Empty(h.ViewModel.Document.Model.AllElements());
        Assert.NotNull(h.Services.ClipboardText);

        h.Click(500, 400, MouseButton.Right);
        var backgroundItems = h.Canvas.LastContextMenu!.Items.OfType<MenuItem>().ToList();
        Assert.Equal(["Paste", "Diagram Font…", "Arrange Diagram"], backgroundItems.Select(i => (string)i.Header!));
        var paste = backgroundItems[0];
        paste.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        Dispatcher.UIThread.RunJobs();

        var pasted = Assert.IsType<UmlClass>(Assert.Single(h.ViewModel.Document.Model.AllElements()));
        Assert.Equal(cls.Name, pasted.Name);
        Assert.Equal(500, h.ViewModel.Document.Layout.Find(pasted.Id)!.X);
    }

    [AvaloniaFact]
    public async Task ArrangeDiagram_FromTheBackgroundMenu_ArrangesTheDiagram_AsOneUndoStep()
    {
        var h = new DiagramHarness(DiagramType.Class);
        h.Click(500, 400, MouseButton.Right);
        Assert.False(h.Canvas.LastContextMenu!.Items.OfType<MenuItem>().Single(i => (string)i.Header! == "Arrange Diagram").IsEnabled);
        Assert.False(h.ViewModel.ArrangeCommand.CanExecute(null));

        // A base class below its subclass, far to the right.
        var editor = h.ViewModel.Editor;
        var sub = editor.AddClass(ClassifierKind.Class, 100, 100);
        var baseClass = editor.AddClass(ClassifierKind.Class, 600, 300);
        editor.AddRelationship(RelationshipKind.Inheritance, sub.Id, baseClass.Id);
        Assert.True(h.ViewModel.ArrangeCommand.CanExecute(null));
        var before = (editor.Layout.Find(sub.Id)!.Y, editor.Layout.Find(baseClass.Id)!.Y);

        h.Click(700, 600, MouseButton.Right);
        var arrange = h.Canvas.LastContextMenu!.Items.OfType<MenuItem>().Single(i => (string)i.Header! == "Arrange Diagram");
        Assert.True(arrange.IsEnabled);
        arrange.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));

        // The layout is worked out on another thread.
        await h.ViewModel.ArrangeCommand.ExecutionTask!;
        Assert.True(editor.Layout.Find(baseClass.Id)!.Y < editor.Layout.Find(sub.Id)!.Y);
        Assert.True(h.ViewModel.IsDirty);

        h.ViewModel.UndoCommand.Execute(null);
        Assert.Equal(before, (editor.Layout.Find(sub.Id)!.Y, editor.Layout.Find(baseClass.Id)!.Y));
    }

    [AvaloniaFact]
    public async Task Arranging_FitsTheViewToTheArrangedDiagram()
    {
        var h = new DiagramHarness(DiagramType.Class);
        var editor = h.ViewModel.Editor;
        var sub = editor.AddClass(ClassifierKind.Class, 100, 100);
        var baseClass = editor.AddClass(ClassifierKind.Class, 600, 300);
        editor.AddRelationship(RelationshipKind.Inheritance, sub.Id, baseClass.Id);
        var zooms = new List<ZoomAction>();
        h.ViewModel.ZoomRequested += (_, action) => zooms.Add(action);

        await h.ViewModel.ArrangeCommand.ExecuteAsync(null);

        Assert.Equal([ZoomAction.Fit], zooms);
    }

    [AvaloniaFact]
    public void FittingADiagramTooLargeForTheSmallestZoom_ShowsItsTopLeftCorner()
    {
        var h = new DiagramHarness(DiagramType.Class);
        var editor = h.ViewModel.Editor;
        var first = editor.AddClass(ClassifierKind.Class, 100, 100);
        editor.AddClass(ClassifierKind.Class, 100_000, 100);

        h.ViewModel.ZoomFitCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();

        var view = editor.Document.Layout.View;
        Assert.Equal(DiagramViewModel.MinZoom, view.Zoom);
        var left = editor.Layout.Find(first.Id)!.X * view.Zoom + view.OffsetX;
        Assert.InRange(left, 0, 100);
    }

    [AvaloniaFact]
    public void DraggingAClass_MovesItAsOneUndoStep()
    {
        var h = new DiagramHarness(DiagramType.Class);
        var cls = h.ViewModel.Editor.AddClass(ClassifierKind.Class, 100, 100);

        h.Drag(150, 112, 350, 262);

        var layout = h.ViewModel.Document.Layout.Find(cls.Id)!;
        Assert.Equal((300.0, 250.0), (layout.X, layout.Y));

        h.ViewModel.UndoCommand.Execute(null);
        Assert.Equal((100.0, 100.0), (h.ViewModel.Document.Layout.Find(cls.Id)!.X, h.ViewModel.Document.Layout.Find(cls.Id)!.Y));
    }

    [AvaloniaFact]
    public void ArrowKeys_MoveSelectedElement_ByOneOrAGridStepWithShift()
    {
        var h = new DiagramHarness(DiagramType.Class);
        var cls = h.ViewModel.Editor.AddClass(ClassifierKind.Class, 100, 100);
        var layout = h.ViewModel.Document.Layout.Find(cls.Id)!;
        h.Click(150, 112);
        h.ViewModel.Editor.MarkSaved();

        h.Press(Key.Right);
        h.Press(Key.Up);
        Assert.Equal((101.0, 99.0), (layout.X, layout.Y));
        Assert.True(h.ViewModel.IsDirty);

        h.Press(Key.Down, RawInputModifiers.Shift);
        h.Press(Key.Left, RawInputModifiers.Shift);
        Assert.Equal((81.0, 119.0), (layout.X, layout.Y));

        // Each press is its own undo step.
        h.ViewModel.UndoCommand.Execute(null);
        layout = h.ViewModel.Document.Layout.Find(cls.Id)!;
        Assert.Equal((101.0, 119.0), (layout.X, layout.Y));
    }

    [AvaloniaFact]
    public void HoldingAnArrowKey_MovesAsOneUndoStep()
    {
        var h = new DiagramHarness(DiagramType.Class);
        var cls = h.ViewModel.Editor.AddClass(ClassifierKind.Class, 100, 100);
        h.Click(150, 112);

        h.KeyDown(Key.Right);
        h.KeyDown(Key.Right);
        h.KeyDown(Key.Right);
        h.KeyUp(Key.Right);
        Assert.Equal(103.0, h.ViewModel.Document.Layout.Find(cls.Id)!.X);

        h.ViewModel.UndoCommand.Execute(null);
        Assert.Equal(100.0, h.ViewModel.Document.Layout.Find(cls.Id)!.X);
    }

    [AvaloniaFact]
    public void ArrowKeys_WithoutMovableSelection_ChangeNothing()
    {
        var h = new DiagramHarness(DiagramType.Sequence);
        var lifeline = h.ViewModel.Editor.AddLifeline(LifelineKind.Object, 400);
        var layout = h.ViewModel.Document.Layout.Find(lifeline.Id)!;
        h.Click(20, 600);
        h.ViewModel.Editor.MarkSaved();

        h.Press(Key.Right);
        Assert.Null(h.ViewModel.Editor.SelectedId);
        Assert.Equal(400.0, layout.X);

        // Lifelines only move horizontally: Up neither moves it nor records an undo step.
        h.ViewModel.Editor.SelectedId = lifeline.Id;
        h.Press(Key.Up);
        Assert.False(h.ViewModel.IsDirty);
        h.Press(Key.Right);
        Assert.Equal(401.0, layout.X);
        Assert.True(h.ViewModel.IsDirty);
    }

    [AvaloniaFact]
    public void InheritanceTool_ConnectsTwoClasses()
    {
        var h = new DiagramHarness(DiagramType.Class);
        var child = h.ViewModel.Editor.AddClass(ClassifierKind.Class, 100, 300);
        var parent = h.ViewModel.Editor.AddClass(ClassifierKind.AbstractClass, 100, 50);
        h.ViewModel.ActiveTool = h.ViewModel.Tools.Single(t => t.Kind == ToolKind.Inheritance);

        h.Drag(150, 312, 150, 62);

        var relationship = Assert.Single(((ClassDiagramModel)h.ViewModel.Document.Model).Relationships);
        Assert.Equal((RelationshipKind.Inheritance, child.Id, parent.Id),
            (relationship.Kind, relationship.SourceId, relationship.TargetId));
        Assert.Equal(ToolKind.Select, h.ViewModel.ActiveTool.Kind);
    }

    [AvaloniaFact]
    public void SequenceTools_PlaceLifelinesAndMessages_AndMessageEditsInline()
    {
        var h = new DiagramHarness(DiagramType.Sequence);
        h.ViewModel.ActiveTool = h.ViewModel.Tools.Single(t => t.Kind == ToolKind.Actor);
        h.Click(100, 200);
        h.ViewModel.ActiveTool = h.ViewModel.Tools.Single(t => t.Kind == ToolKind.Lifeline);
        h.Click(400, 200);
        var model = (SequenceDiagramModel)h.ViewModel.Document.Model;
        Assert.Equal([LifelineKind.Actor, LifelineKind.Object], model.Lifelines.Select(l => l.Kind));

        h.ViewModel.ActiveTool = h.ViewModel.Tools.Single(t => t.Kind == ToolKind.SyncMessage);
        h.Drag(100, 180, 400, 180);
        var message = Assert.Single(model.Messages);

        h.DoubleClick(250, 180);
        h.Type("checkout(cart)");
        h.Press(Key.Enter);

        Assert.Equal("checkout(cart)", message.Text);
    }

    [AvaloniaFact]
    public void CapturesFrameWhileEditingInline()
    {
        var h = new DiagramHarness(DiagramType.Class);
        var cls = h.ViewModel.Editor.AddClass(ClassifierKind.Class, 60, 60);
        cls.Attributes.Add(new UmlAttribute { Name = "id", Type = "Guid" });
        cls.Operations.Add(new UmlOperation { Name = "save", ReturnType = "bool" });
        h.ViewModel.Renderer.UpdateLayout(h.ViewModel.Document);
        var layout = h.ViewModel.Document.Layout.Find(cls.Id)!;

        h.DoubleClick(90, layout.Y + 30 + 6 + 9);
        h.Type("+id: long");

        var frame = h.Window.CaptureRenderedFrame();
        Assert.NotNull(frame);
        frame!.Save(Path.Combine(Path.GetTempPath(), "umlwizard-inline-edit.png"));
    }
}
