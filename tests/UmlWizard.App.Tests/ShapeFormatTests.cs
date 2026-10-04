using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Threading;
using SkiaSharp;
using UmlWizard.App.Controls;
using UmlWizard.App.ViewModels;
using UmlWizard.App.ViewModels.Dialogs;
using UmlWizard.App.Views;
using UmlWizard.Core.Model;
using UmlWizard.Core.Rendering;

namespace UmlWizard.App.Tests;

public sealed class ShapeFormatTests
{
    [AvaloniaFact]
    public void FormatShapeContextItem_SetsFillAndBorder_AndUndoRestoresDefaults()
    {
        var h = new DiagramHarness(DiagramType.Class);
        var cls = h.ViewModel.Editor.AddClass(ClassifierKind.Interface, 100, 100);
        h.Services.AcceptDialogs = true;
        h.Services.FillDialog = d =>
        {
            var shape = Assert.IsType<ShapeEditorViewModel>(d);
            Assert.StartsWith("Format Shape — ", shape.Title);

            // The defaults are the interface's scheme colours.
            Assert.True(shape.UseDefaultFill);
            Assert.Equal(ShapeColors.ToHex(RenderTheme.Light.InterfaceHeader), shape.FillHex);
            Assert.True(shape.UseDefaultBorder);
            Assert.Equal(1.2m, shape.BorderWidth);

            shape.SetFill("#FDE7E7");
            shape.UseDefaultBorder = false;
            shape.BorderHex = "#c62828";
            shape.BorderWidth = 3;
        };

        h.Click(150, 112, MouseButton.Right);
        h.Canvas.LastContextMenu!.Items.OfType<MenuItem>().Single(i => (string)i.Header! == "Shape…")
            .RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        Dispatcher.UIThread.RunJobs();

        var style = h.ViewModel.Document.Layout.Find(cls.Id)!.Style!;
        Assert.Equal(("#FDE7E7", "#C62828", 3d), (style.Fill, style.Border, style.BorderWidth));
        Assert.True(h.ViewModel.IsDirty);

        h.ViewModel.UndoCommand.Execute(null);
        Assert.Null(h.ViewModel.Document.Layout.Find(cls.Id)!.Style);
    }

    [AvaloniaFact]
    public void FormatShapeContextItem_IsOfferedForShapes_NotForLines()
    {
        var h = new DiagramHarness(DiagramType.Class);
        var a = h.ViewModel.Editor.AddClass(ClassifierKind.Class, 100, 100);
        var b = h.ViewModel.Editor.AddClass(ClassifierKind.Class, 500, 100);
        var relationship = h.ViewModel.Editor.AddRelationship(RelationshipKind.Association, a.Id, b.Id)!;

        h.Click(150, 112, MouseButton.Right);
        Assert.Contains(h.Canvas.LastContextMenu!.Items.OfType<MenuItem>(), i => (string)i.Header! == "Shape…");

        h.ViewModel.Editor.SelectedId = relationship.Id;
        Assert.False(h.ViewModel.FormatShapeCommand.CanExecute(null));
        Assert.False(h.ViewModel.CanFormatShape);
    }

    [AvaloniaFact]
    public void FormatMenu_ShapeItemFollowsSelection()
    {
        var h = new DiagramHarness(DiagramType.Class);
        var main = new MainWindowViewModel(h.Services, h.Services);
        var window = new MainWindow { DataContext = main };
        window.Show();
        main.OpenDiagrams.Add(h.ViewModel);
        main.ActiveDiagram = h.ViewModel;
        Dispatcher.UIThread.RunJobs();

        var format = window.GetLogicalDescendants().OfType<MenuItem>().Single(m => (string?)m.Header == "F_ormat");
        var shape = format.Items.OfType<MenuItem>().Single(m => (string?)m.Header == "_Shape…");
        format.Open();
        Dispatcher.UIThread.RunJobs();
        Assert.False(shape.IsEffectivelyEnabled);

        h.ViewModel.Editor.AddNote(100, 100);
        Dispatcher.UIThread.RunJobs();
        Assert.True(shape.IsEffectivelyEnabled);
        Assert.True(main.FormatShapeCommand.CanExecute(null));
        window.Close();
    }

    [AvaloniaFact]
    public void Dialog_KeepsDefaultsUnset_AndResetClearsEverything()
    {
        var dialog = new ShapeEditorViewModel("T", new ShapeStyle { Fill = "#1A73E8", BorderWidth = 4 },
            RenderTheme.Light.StateFill, RenderTheme.Light.Border, RenderTheme.Light);

        Assert.False(dialog.UseDefaultFill);
        Assert.Equal("#1A73E8", dialog.FillHex);
        Assert.True(dialog.UseDefaultBorder);
        Assert.Equal(4m, dialog.BorderWidth);

        // White text on the dark blue fill.
        Assert.Equal(Color.FromRgb(RenderTheme.Dark.Text.Red, RenderTheme.Dark.Text.Green, RenderTheme.Dark.Text.Blue),
            ((ISolidColorBrush)dialog.PreviewText).Color);

        var style = dialog.ToStyle()!;
        Assert.Equal(("#1A73E8", (string?)null, (double?)4), (style.Fill, style.Border, style.BorderWidth));

        dialog.ResetToDefaultsCommand.Execute(null);
        Assert.Null(dialog.ToStyle());
        Assert.Equal(ShapeColors.ToHex(RenderTheme.Light.StateFill), dialog.FillHex);
    }

    [AvaloniaFact]
    public void Dialog_RejectsColoursThatAreNotHex()
    {
        var dialog = new ShapeEditorViewModel("T", null, RenderTheme.Light.NoteFill, RenderTheme.Light.Border, RenderTheme.Light)
        {
            UseDefaultFill = false,
            FillHex = "red",
        };

        Assert.False(dialog.TryAccept());
        Assert.Equal("Enter colours as #RRGGBB, e.g. #E8EEF9.", dialog.ErrorMessage);

        dialog.FillHex = "#FF0000";
        Assert.True(dialog.TryAccept());
    }

    [AvaloniaFact]
    public void Dialog_ForATextBox_SaysItHasNoFillOrBorderByDefault()
    {
        var theme = RenderTheme.Light;
        var textBox = new TextBoxElement();
        var dialog = new ShapeEditorViewModel("T", null, theme.DefaultFill(textBox), theme.DefaultBorder(textBox), theme);

        Assert.True(dialog.HasNoDefaultFill);
        Assert.True(dialog.HasNoDefaultBorder);
        Assert.Equal(Colors.Transparent, ((ISolidColorBrush)dialog.PreviewBorder).Color);
        Assert.Null(dialog.ToStyle());
    }

    [AvaloniaFact]
    public async Task DialogView_HasAColourBoxForFillAndBorder_WhoseSwatchOpensTheColourPicker()
    {
        var services = new FakeServices { AcceptDialogs = true };
        services.FillDialog = d => Assert.IsType<ColorPickerViewModel>(d).HexText = "#336699";
        var dialog = new ShapeEditorViewModel("Format Shape — Class A", null, RenderTheme.Light.ClassHeader, RenderTheme.Light.Border, RenderTheme.Light)
        {
            Dialogs = services,
        };
        var window = new DialogWindow { DataContext = dialog };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var boxes = window.GetLogicalDescendants().OfType<ColorBox>().ToList();
        Assert.Equal(["FillColor", "BorderColor"], boxes.Select(b => b.Name));
        Assert.Same(dialog.EditFillCommand, boxes[0].EditCommand);

        // The picker starts from the default fill, and its colour becomes the element's own.
        await dialog.EditFillCommand.ExecuteAsync(null);
        var picker = Assert.IsType<ColorPickerViewModel>(Assert.Single(services.ShownDialogs));
        Assert.Equal("Edit Colour — Fill", picker.Title);
        Assert.False(dialog.UseDefaultFill);
        Assert.Equal("#336699", dialog.FillHex);
        Assert.Equal("#336699", dialog.ToStyle()!.Fill);

        // Typing a colour does the same for the border.
        dialog.BorderHex = "#C62828";
        Assert.False(dialog.UseDefaultBorder);
        window.Close();
    }

    [AvaloniaFact]
    public void LineContextItem_OnARelationship_SetsItsColourAndThickness()
    {
        var h = new DiagramHarness(DiagramType.Class);
        var a = h.ViewModel.Editor.AddClass(ClassifierKind.Class, 100, 100);
        var b = h.ViewModel.Editor.AddClass(ClassifierKind.Class, 500, 100);
        var relationship = h.ViewModel.Editor.AddRelationship(RelationshipKind.Association, a.Id, b.Id)!;
        using var renderer = DiagramRenderer.Create(DiagramType.Class);
        var y = renderer.GetElementBounds(h.ViewModel.Document, a.Id)!.Value.MidY;
        h.Services.AcceptDialogs = true;
        h.Services.FillDialog = d =>
        {
            var line = Assert.IsType<ShapeEditorViewModel>(d);
            Assert.StartsWith("Format Line — ", line.Title);
            Assert.True(line.IsLine);
            Assert.False(line.HasFill);
            Assert.Equal("Line", line.BorderHeading);
            Assert.Equal(ShapeColors.ToHex(RenderTheme.Light.Line), line.BorderHex);
            Assert.Equal(1.3m, line.BorderWidth);
            line.SetBorder("#1A73E8");
            line.BorderWidth = 4;
        };

        h.Click(400, y, MouseButton.Right);
        Assert.Equal(relationship.Id, h.ViewModel.Editor.SelectedId);
        var items = h.Canvas.LastContextMenu!.Items.OfType<MenuItem>().Select(i => (string)i.Header!).ToList();
        Assert.Contains("Line…", items);
        Assert.DoesNotContain("Shape…", items);
        h.Canvas.LastContextMenu!.Items.OfType<MenuItem>().Single(i => (string)i.Header! == "Line…")
            .RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        Dispatcher.UIThread.RunJobs();

        var style = h.ViewModel.Document.Layout.Find(relationship.Id)!.Line!;
        Assert.Equal(("#1A73E8", (double?)4), (style.Color, style.Width));
        h.ViewModel.UndoCommand.Execute(null);
        Assert.Null(h.ViewModel.Document.Layout.Find(relationship.Id)?.Line);
    }

    [AvaloniaFact]
    public void LineDialog_KeepsDefaultsUnset()
    {
        var dialog = ShapeEditorViewModel.ForLine("T", new LineStyle { Width = 2 }, RenderTheme.Light);

        Assert.True(dialog.UseDefaultBorder);
        var style = dialog.ToLineStyle()!;
        Assert.Equal(((string?)null, (double?)2), (style.Color, style.Width));

        dialog.ResetToDefaultsCommand.Execute(null);
        Assert.Equal(1.3m, dialog.BorderWidth);
        Assert.Null(dialog.ToLineStyle());
    }

    [AvaloniaFact]
    public void FormatMenu_LineItem_AndToolbarButtons_FollowTheSelection()
    {
        var h = new DiagramHarness(DiagramType.StateMachine);
        var main = new MainWindowViewModel(h.Services, h.Services);
        var window = new MainWindow { DataContext = main };
        window.Show();
        main.OpenDiagrams.Add(h.ViewModel);
        main.ActiveDiagram = h.ViewModel;
        var s1 = h.ViewModel.Editor.AddNode(GraphNodeKind.State, 100, 100);
        var s2 = h.ViewModel.Editor.AddNode(GraphNodeKind.State, 400, 100);
        h.ViewModel.Editor.AddEdge(GraphEdgeKind.Transition, s1.Id, s2.Id);
        Dispatcher.UIThread.RunJobs();

        var format = window.GetLogicalDescendants().OfType<MenuItem>().Single(m => (string?)m.Header == "F_ormat");
        var line = format.Items.OfType<MenuItem>().Single(m => (string?)m.Header == "_Line…");
        var shape = format.Items.OfType<MenuItem>().Single(m => (string?)m.Header == "_Shape…");
        var toolbarButtons = window.GetLogicalDescendants().OfType<Button>().ToList();
        var shapeButton = toolbarButtons.Single(b => b.Name == "ShapeButton");
        var lineButton = toolbarButtons.Single(b => b.Name == "LineButton");
        Assert.Equal(toolbarButtons.IndexOf(shapeButton) + 1, toolbarButtons.IndexOf(lineButton));
        format.Open();
        Dispatcher.UIThread.RunJobs();
        Assert.True(line.IsEffectivelyEnabled);
        Assert.False(shape.IsEffectivelyEnabled);
        Assert.True(lineButton.IsEffectivelyEnabled);
        Assert.False(shapeButton.IsEffectivelyEnabled);

        // The Line button opens the line dialog of the selected transition.
        h.Services.AcceptDialogs = false;
        lineButton.Command!.Execute(null);
        Assert.True(Assert.IsType<ShapeEditorViewModel>(h.Services.ShownDialogs[^1]).IsLine);

        h.ViewModel.Editor.SelectedId = s1.Id;
        Dispatcher.UIThread.RunJobs();
        Assert.False(line.IsEffectivelyEnabled);
        Assert.True(shape.IsEffectivelyEnabled);
        Assert.False(lineButton.IsEffectivelyEnabled);
        Assert.True(shapeButton.IsEffectivelyEnabled);
        shapeButton.Command!.Execute(null);
        Assert.False(Assert.IsType<ShapeEditorViewModel>(h.Services.ShownDialogs[^1]).IsLine);
        window.Close();
    }
}
