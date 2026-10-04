using Avalonia.Headless.XUnit;
using Avalonia.Input;
using UmlWizard.App.ViewModels;
using UmlWizard.App.ViewModels.Dialogs;
using UmlWizard.Core.Model;

namespace UmlWizard.App.Tests;

public sealed class TextBoxAndPropertiesTests
{
    [Fact]
    public void TextBoxTool_IsInBothToolboxes()
    {
        Assert.Contains(ToolItem.For(DiagramType.Class), t => t.Kind == ToolKind.TextBox && t.IsNodeTool);
        Assert.Contains(ToolItem.For(DiagramType.Sequence), t => t.Kind == ToolKind.TextBox && t.IsNodeTool);
    }

    [AvaloniaFact]
    public void PlacingATextBox_StartsMultiLineEditing_AndCtrlEnterFinishes()
    {
        var h = new DiagramHarness(DiagramType.Class);
        h.ViewModel.ActiveTool = h.ViewModel.Tools.Single(t => t.Kind == ToolKind.TextBox);

        h.Click(100, 80);

        var textBox = Assert.Single(h.ViewModel.Document.Model.TextBoxes);
        Assert.Equal((100, 80), (h.ViewModel.Document.Layout.Find(textBox.Id)!.X, h.ViewModel.Document.Layout.Find(textBox.Id)!.Y));
        Assert.True(h.InlineEditor.IsVisible);
        Assert.True(h.InlineEditor.AcceptsReturn);
        Assert.Equal(ToolKind.Select, h.ViewModel.ActiveTool.Kind);

        h.Type("Line one");
        h.Press(Key.Enter);
        Assert.True(h.InlineEditor.IsVisible);
        h.Type("Line two");
        h.Press(Key.Enter, Shortcuts.CommandModifier == KeyModifiers.Meta ? RawInputModifiers.Meta : RawInputModifiers.Control);

        Assert.False(h.InlineEditor.IsVisible);
        Assert.Equal(["Line one", "Line two"], textBox.Text.ReplaceLineEndings("\n").Split('\n'));
    }

    [Fact]
    public void NoteTool_IsInBothToolboxes()
    {
        Assert.Contains(ToolItem.For(DiagramType.Class), t => t.Kind == ToolKind.Note && t.IsNodeTool);
        Assert.Contains(ToolItem.For(DiagramType.Sequence), t => t.Kind == ToolKind.Note && t.IsNodeTool);
    }

    [AvaloniaFact]
    public void PlacingANote_StartsMultiLineEditing()
    {
        var h = new DiagramHarness(DiagramType.Sequence);
        h.ViewModel.ActiveTool = h.ViewModel.Tools.Single(t => t.Kind == ToolKind.Note);

        h.Click(200, 150);

        var note = Assert.Single(h.ViewModel.Document.Model.Notes);
        Assert.True(h.InlineEditor.IsVisible);
        Assert.True(h.InlineEditor.AcceptsReturn);
        h.Type("Needs review");
        h.Press(Key.Escape);
        Assert.Equal("Note", note.Text);

        h.DoubleClick(220, 170);
        h.Type("Needs review");
        h.Press(Key.Enter, Shortcuts.CommandModifier == KeyModifiers.Meta ? RawInputModifiers.Meta : RawInputModifiers.Control);
        Assert.Equal("Needs review", note.Text);
        Assert.StartsWith("Selected: Note \"Needs review\"", h.ViewModel.StatusText);
    }

    [AvaloniaFact]
    public async Task EditDialog_ForANote_IsTitledEditNote_AndFormatsIt()
    {
        var h = new DiagramHarness(DiagramType.Class);
        var note = h.ViewModel.Editor.AddNote(0, 0);
        h.Services.AcceptDialogs = true;
        h.Services.FillDialog = d =>
        {
            var dialog = (TextBoxEditorViewModel)d;
            Assert.Equal("Edit Note", dialog.Title);
            dialog.Text = "Invariant: total >= 0";
            dialog.HorizontalAlignment = HorizontalTextAlignment.Right;
            dialog.VerticalAlignment = VerticalTextAlignment.Bottom;
            dialog.Font.UseInherited = false;
            dialog.Font.Bold = true;
        };

        await h.ViewModel.EditElementAsync(note.Id);

        Assert.Equal(("Invariant: total >= 0", HorizontalTextAlignment.Right, VerticalTextAlignment.Bottom),
            (note.Text, note.HorizontalAlignment, note.VerticalAlignment));
        Assert.True(h.ViewModel.Document.Layout.Find(note.Id)!.Font!.Bold);
        Assert.Equal("Edit Text Box", new TextBoxEditorViewModel(new TextBoxElement(), null, null).Title);
    }

    [AvaloniaFact]
    public void DraggingTheCornerHandle_ResizesWidthAndHeight_AsOneUndoStep()
    {
        var h = new DiagramHarness(DiagramType.Sequence);
        var textBox = h.ViewModel.Editor.AddTextBox(100, 100);
        var layout = h.ViewModel.Document.Layout.Find(textBox.Id)!;
        var (right, bottom) = (layout.X + layout.Width, layout.Y + layout.Height);

        h.Drag(right, bottom, right + 90, bottom + 40);

        Assert.Equal((TextBoxElement.DefaultWidth + 90, TextBoxElement.DefaultHeight + 40), (layout.Width, layout.Height));
        h.ViewModel.UndoCommand.Execute(null);
        var restored = h.ViewModel.Document.Layout.Find(textBox.Id)!;
        Assert.Equal((TextBoxElement.DefaultWidth, TextBoxElement.DefaultHeight), (restored.Width, restored.Height));
    }

    [AvaloniaFact]
    public async Task EditDialog_SetsTextAlignmentAndFont_AsOneUndoStep()
    {
        var h = new DiagramHarness(DiagramType.Class);
        var textBox = h.ViewModel.Editor.AddTextBox(0, 0);
        h.Services.AcceptDialogs = true;
        h.Services.FillDialog = d =>
        {
            var dialog = (TextBoxEditorViewModel)d;
            dialog.Text = "Centred\r\nnote";
            dialog.HorizontalAlignment = HorizontalTextAlignment.Center;
            dialog.VerticalAlignment = VerticalTextAlignment.Middle;
            dialog.Font.UseInherited = false;
            dialog.Font.Size = 20;
            dialog.Font.Bold = true;
            dialog.Font.Italic = true;
        };

        await h.ViewModel.EditElementAsync(textBox.Id);

        Assert.Equal(("Centred\nnote", HorizontalTextAlignment.Center, VerticalTextAlignment.Middle),
            (textBox.Text, textBox.HorizontalAlignment, textBox.VerticalAlignment));
        var font = h.ViewModel.Document.Layout.Find(textBox.Id)!.Font!;
        Assert.Equal((20.0, true, true), (font.Size, font.Bold, font.Italic));

        h.ViewModel.UndoCommand.Execute(null);
        var restored = Assert.Single(h.ViewModel.Document.Model.TextBoxes);
        Assert.Equal(("Text", HorizontalTextAlignment.Left), (restored.Text, restored.HorizontalAlignment));
        Assert.Null(h.ViewModel.Document.Layout.Find(restored.Id)!.Font);
    }

    [AvaloniaFact]
    public void EditDialog_RejectsAnInvalidFontSize()
    {
        var dialog = new TextBoxEditorViewModel(new TextBoxElement(), null, null);
        dialog.Font.UseInherited = false;
        dialog.Font.Size = 500;

        Assert.False(dialog.TryAccept());
        Assert.NotNull(dialog.ErrorMessage);
    }

    [AvaloniaFact]
    public async Task DiagramProperties_SavesNameDescriptionAndAuthor_AndUpdatesAnOpenTabWithoutDirtyingIt()
    {
        using var f = new ExplorerFixture();
        f.Main.OpenDiagramCommand.Execute((DiagramItemViewModel)f.Node("A"));
        f.Main.SelectedNode = f.Node("A");
        var id = f.Node("A").Id;
        f.Services.FillDialog = d =>
        {
            var dialog = Assert.IsType<DiagramPropertiesViewModel>(d);
            Assert.Equal(("A", "Class diagram"), (dialog.Name, dialog.TypeName));
            dialog.Name = "  Domain  ";
            dialog.Description = "The order domain";
            dialog.Author = "Grace";
        };

        await f.Main.EditDiagramPropertiesCommand.ExecuteAsync(null);

        var saved = f.Saved.FindDiagram(id)!;
        Assert.Equal(("Domain", "The order domain", "Grace"), (saved.Name, saved.Description, saved.Author));
        Assert.Equal("Domain", f.Main.AllNodes().Single(n => n.Id == id).Name);
        var tab = f.Main.OpenDiagrams.Single(d => d.Id == id);
        Assert.Equal(("Domain", "Grace"), (tab.Document.Name, tab.Document.Author));
        Assert.False(tab.IsDirty);
    }

    [AvaloniaFact]
    public async Task DiagramProperties_RequiresAName_AndIsOnlyForDiagrams()
    {
        var dialog = new DiagramPropertiesViewModel(DiagramDocument.Create("A", DiagramType.Sequence)) { Name = "  " };
        Assert.False(dialog.TryAccept());

        using var f = new ExplorerFixture();
        f.Main.SelectedNode = f.Node("Model");
        await f.Main.EditDiagramPropertiesCommand.ExecuteAsync(null);
        Assert.Empty(f.Services.ShownDialogs);
    }

    [AvaloniaFact]
    public async Task NewDiagram_TakesTheProjectAuthor()
    {
        using var f = new ExplorerFixture();
        f.Services.FillDialog = d => ((NewDiagramViewModel)d).Name = "Orders";

        await f.Main.NewClassDiagramCommand.ExecuteAsync(null);

        Assert.Equal("Ada", f.Saved.Diagrams.Single(d => d.Name == "Orders").Author);
    }
}
