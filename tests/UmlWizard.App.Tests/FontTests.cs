using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.LogicalTree;
using UmlWizard.App.ViewModels;
using UmlWizard.App.Views;
using UmlWizard.App.ViewModels.Dialogs;
using UmlWizard.Core.Model;

namespace UmlWizard.App.Tests;

public sealed class FontTests
{
    [AvaloniaFact]
    public void FontMenuItem_SetsElementFont_AndInlineEditorUsesIt()
    {
        var h = new DiagramHarness(DiagramType.Class);
        var cls = h.ViewModel.Editor.AddClass(ClassifierKind.Class, 100, 100);
        h.Services.AcceptDialogs = true;
        h.Services.FillDialog = d =>
        {
            var font = Assert.IsType<FontEditorViewModel>(d);
            Assert.True(font.UseInherited);
            Assert.Equal("Use the diagram font", font.InheritLabel);
            font.UseInherited = false;
            font.Family = "Courier New";
            font.Size = 20;
            font.Bold = true;
        };

        h.Click(150, 112, MouseButton.Right);
        h.Canvas.LastContextMenu!.Items.OfType<MenuItem>().Single(i => (string)i.Header! == "Font…")
            .RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        Dispatcher.UIThread.RunJobs();

        var stored = h.ViewModel.Document.Layout.Find(cls.Id)!.Font!;
        Assert.Equal("Courier New", stored.Family);
        Assert.Equal(20, stored.Size);
        Assert.True(stored.Bold);
        Assert.True(h.ViewModel.IsDirty);

        h.DoubleClick(150, 115);
        Assert.True(h.InlineEditor.IsVisible);
        Assert.Equal(20, h.InlineEditor.FontSize);
        Assert.Equal(FontWeight.Bold, h.InlineEditor.FontWeight);
        Assert.Equal("Courier New", h.InlineEditor.FontFamily.Name);
    }

    [AvaloniaFact]
    public void DiagramFontMenuItem_SetsDiagramFont_AndUndoRestoresDefault()
    {
        var h = new DiagramHarness(DiagramType.Class);
        h.Services.AcceptDialogs = true;
        h.Services.FillDialog = d =>
        {
            var font = Assert.IsType<FontEditorViewModel>(d);
            font.UseInherited = false;
            font.Size = 16;
            font.Italic = true;
        };

        h.Click(500, 400, MouseButton.Right);
        h.Canvas.LastContextMenu!.Items.OfType<MenuItem>().Single(i => (string)i.Header! == "Diagram Font…")
            .RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        Dispatcher.UIThread.RunJobs();

        var font = h.ViewModel.Document.Layout.Font!;
        Assert.Null(font.Family);
        Assert.Equal(16, font.Size);
        Assert.True(font.Italic);

        h.ViewModel.UndoCommand.Execute(null);
        Assert.Null(h.ViewModel.Document.Layout.Font);
    }

    [AvaloniaFact]
    public void FontCommand_IsEnabledOnlyWhileAnElementIsSelected()
    {
        var h = new DiagramHarness(DiagramType.Class);
        var main = new MainWindowViewModel(h.Services, h.Services);
        var changes = 0;
        main.EditFontCommand.CanExecuteChanged += (_, _) => changes++;

        Assert.False(main.EditFontCommand.CanExecute(null));

        main.OpenDiagrams.Add(h.ViewModel);
        main.ActiveDiagram = h.ViewModel;
        Assert.False(main.EditFontCommand.CanExecute(null));

        var cls = h.ViewModel.Editor.AddClass(ClassifierKind.Class, 100, 100);
        Assert.True(main.EditFontCommand.CanExecute(null));
        Assert.True(h.ViewModel.EditFontCommand.CanExecute(null));

        h.ViewModel.Editor.SelectedId = null;
        Assert.False(main.EditFontCommand.CanExecute(null));

        h.ViewModel.Editor.SelectedId = cls.Id;
        h.ViewModel.DeleteCommand.Execute(null);
        Assert.False(main.EditFontCommand.CanExecute(null));

        h.ViewModel.UndoCommand.Execute(null);
        h.ViewModel.Editor.SelectedId = cls.Id;
        Assert.True(main.EditFontCommand.CanExecute(null));

        main.ActiveDiagram = null;
        Assert.False(main.EditFontCommand.CanExecute(null));
        Assert.True(changes >= 6);
    }

    [AvaloniaFact]
    public void FormatMenu_FontItemFollowsSelection()
    {
        var h = new DiagramHarness(DiagramType.Class);
        var main = new MainWindowViewModel(h.Services, h.Services);
        var window = new MainWindow { DataContext = main };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var format = window.GetLogicalDescendants().OfType<MenuItem>().Single(m => (string?)m.Header == "F_ormat");
        var font = format.Items.OfType<MenuItem>().Single(m => (string?)m.Header == "_Font…");
        var diagramFont = format.Items.OfType<MenuItem>().Single(m => (string?)m.Header == "_Diagram Font…");
        Assert.False(format.IsEnabled);

        main.OpenDiagrams.Add(h.ViewModel);
        main.ActiveDiagram = h.ViewModel;
        Dispatcher.UIThread.RunJobs();
        Assert.True(format.IsEnabled);

        // Sub-items only resolve their enabled state while the menu is open, as it is when the user checks.
        format.Open();
        Dispatcher.UIThread.RunJobs();
        Assert.False(font.IsEffectivelyEnabled);
        Assert.True(diagramFont.IsEffectivelyEnabled);

        h.ViewModel.Editor.AddClass(ClassifierKind.Class, 100, 100);
        Dispatcher.UIThread.RunJobs();
        Assert.True(font.IsEffectivelyEnabled);

        h.ViewModel.Editor.SelectedId = null;
        Dispatcher.UIThread.RunJobs();
        Assert.False(font.IsEffectivelyEnabled);
        window.Close();
    }

    [AvaloniaFact]
    public void FontEditor_InheritingShowsInheritedFontAndReturnsNull()
    {
        var inherited = new DiagramFont { Family = "Arial", Size = 15, Italic = true };
        var dialog = new FontEditorViewModel("Font", new DiagramFont { Size = 30, Bold = true }, inherited, "Use the diagram font");

        Assert.False(dialog.UseInherited);
        Assert.Equal(30, dialog.Size);
        Assert.Equal(FontEditorViewModel.DefaultFamilyName, dialog.Family);

        dialog.UseInherited = true;

        Assert.Equal("Arial", dialog.Family);
        Assert.Equal(15, dialog.Size);
        Assert.False(dialog.Bold);
        Assert.True(dialog.Italic);
        Assert.Null(dialog.ToFont());
    }

    [AvaloniaFact]
    public void FontEditor_RejectsSizeOutOfRange_AndKeepsUnknownFamily()
    {
        var dialog = new FontEditorViewModel("Font", new DiagramFont { Family = "Not Installed Sans" }, null, "Use the default font");

        Assert.Contains("Not Installed Sans", dialog.Families);
        Assert.Equal("Not Installed Sans", dialog.ToFont()!.Family);

        dialog.Size = 200;
        Assert.False(dialog.TryAccept());
        dialog.Size = 12;
        Assert.True(dialog.TryAccept());
    }
}
