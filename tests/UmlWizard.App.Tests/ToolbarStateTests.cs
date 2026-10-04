using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using UmlWizard.App.Controls;
using UmlWizard.App.ViewModels;
using UmlWizard.App.ViewModels.Dialogs;
using UmlWizard.App.Views;
using UmlWizard.Core.Model;
using UmlWizard.Core.Rendering;

namespace UmlWizard.App.Tests;

public sealed class ToolbarStateTests
{
    [AvaloniaFact]
    public void SelectionCommands_AreEnabledOnlyWithASelection()
    {
        var h = new DiagramHarness(DiagramType.Class);
        var vm = h.ViewModel;
        Assert.False(vm.CutCommand.CanExecute(null));
        Assert.False(vm.CopyCommand.CanExecute(null));
        Assert.False(vm.DeleteCommand.CanExecute(null));

        var cls = vm.Editor.AddClass(ClassifierKind.Class, 100, 100);
        vm.Editor.SelectedId = cls.Id;

        Assert.True(vm.CutCommand.CanExecute(null));
        Assert.True(vm.CopyCommand.CanExecute(null));
        Assert.True(vm.DeleteCommand.CanExecute(null));

        vm.Editor.SelectedId = null;

        Assert.False(vm.CutCommand.CanExecute(null));
    }

    [AvaloniaFact]
    public void UndoRedo_FollowTheHistory()
    {
        var h = new DiagramHarness(DiagramType.Class);
        var vm = h.ViewModel;
        Assert.False(vm.UndoCommand.CanExecute(null));
        Assert.False(vm.RedoCommand.CanExecute(null));

        vm.Editor.AddClass(ClassifierKind.Class, 100, 100);
        Assert.True(vm.UndoCommand.CanExecute(null));
        Assert.False(vm.RedoCommand.CanExecute(null));

        vm.UndoCommand.Execute(null);
        Assert.False(vm.UndoCommand.CanExecute(null));
        Assert.True(vm.RedoCommand.CanExecute(null));
    }

    [AvaloniaFact]
    public async Task Paste_IsEnabledOnlyWithAPasteableClipboard()
    {
        var h = new DiagramHarness(DiagramType.Class);
        var vm = h.ViewModel;
        h.Services.ClipboardText = "not a diagram element";
        await vm.RefreshCanPasteAsync();
        Assert.False(vm.PasteCommand.CanExecute(null));

        vm.Editor.SelectedId = vm.Editor.AddClass(ClassifierKind.Class, 100, 100).Id;
        await vm.CopyCommand.ExecuteAsync(null);

        Assert.True(vm.PasteCommand.CanExecute(null));

        // An element of a class diagram cannot be pasted into a sequence diagram.
        var sequence = new DiagramHarness(DiagramType.Sequence) { Services = { ClipboardText = h.Services.ClipboardText } };
        await sequence.ViewModel.RefreshCanPasteAsync();
        Assert.False(sequence.ViewModel.PasteCommand.CanExecute(null));
    }

    [AvaloniaFact]
    public void Save_IsEnabledOnlyWithUnsavedChanges()
    {
        using var f = new ExplorerFixture();
        f.Main.OpenDiagramCommand.Execute((DiagramItemViewModel)f.Node("A"));
        var diagram = f.Main.ActiveDiagram!;
        Assert.False(f.Main.SaveProjectCommand.CanExecute(null));

        diagram.Editor.AddClass(ClassifierKind.Class, 100, 100);

        Assert.True(f.Main.SaveProjectCommand.CanExecute(null));
    }

    [AvaloniaFact]
    public void EditMenuCommands_FollowTheActiveDiagram()
    {
        using var f = new ExplorerFixture();
        f.Main.OpenDiagramCommand.Execute((DiagramItemViewModel)f.Node("A"));
        var diagram = f.Main.ActiveDiagram!;
        Assert.False(f.Main.CutCommand.CanExecute(null));

        diagram.Editor.SelectedId = diagram.Editor.AddClass(ClassifierKind.Class, 100, 100).Id;

        Assert.True(f.Main.CutCommand.CanExecute(null));
        Assert.True(f.Main.UndoCommand.CanExecute(null));
    }

    [AvaloniaFact]
    public void Toolbar_UnderTheMenu_FollowsTheActiveDiagram()
    {
        using var f = new ExplorerFixture();
        var window = new MainWindow { DataContext = f.Main };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        var toolbar = window.GetLogicalDescendants().OfType<DiagramToolbar>().Single();

        // Without open diagrams the command toolbar is shown but disabled, and there is no drawing toolbar.
        Assert.True(toolbar.IsEffectivelyVisible);
        Assert.False(toolbar.IsEffectivelyEnabled);
        Assert.Empty(window.GetLogicalDescendants().OfType<DrawingToolbar>());

        f.Main.OpenDiagramCommand.Execute((DiagramItemViewModel)f.Node("A"));
        f.Main.OpenDiagramCommand.Execute((DiagramItemViewModel)f.Node("B"));
        Dispatcher.UIThread.RunJobs();

        Assert.True(toolbar.IsEffectivelyVisible);
        Assert.True(toolbar.IsEffectivelyEnabled);
        Assert.Same(f.Main.ActiveDiagram, toolbar.DataContext);
        Assert.Equal("B", ((DiagramViewModel)toolbar.DataContext!).Name);

        // The drawing tools are a toolbar of their own, and hold no commands.
        var drawing = window.GetLogicalDescendants().OfType<DrawingToolbar>().Single();
        Assert.Same(f.Main.ActiveDiagram, drawing.DataContext);
        Assert.Contains(drawing.GetLogicalDescendants(), c => c is ListBox { Classes: var cls } && cls.Contains("toolbox"));
        Assert.Empty(drawing.GetLogicalDescendants().OfType<Button>());
        Assert.Empty(toolbar.GetLogicalDescendants().OfType<ListBox>());

        // The tab separators reach the line above the canvas: no gap under the tab strip.
        var tabs = window.GetLogicalDescendants().OfType<TabControl>().Single(t => t.Classes.Contains("documents"));
        var strip = tabs.GetVisualDescendants().OfType<ItemsPresenter>().Single(p => p.Name == "PART_ItemsPresenter" && p.TemplatedParent == tabs);
        Assert.Equal(default, strip.Margin);
        Assert.Empty(window.GetLogicalDescendants().OfType<DiagramView>().SelectMany(v => v.GetLogicalDescendants()).OfType<DiagramToolbar>());

        // The drawing toolbar sits between the tab strip and the canvas.
        Assert.True(drawing.IsEffectivelyVisible);
        var canvas = window.GetLogicalDescendants().OfType<DiagramView>().Single();
        double Top(Visual v) => v.TranslatePoint(default, window)!.Value.Y;
        Assert.True(Top(drawing) >= Top(strip) + strip.Bounds.Height);
        Assert.True(Top(canvas) >= Top(drawing) + drawing.Bounds.Height);

        // Closing the last tab disables the command toolbar and removes the drawing toolbar again.
        f.Main.CloseAllTabsCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();
        Assert.True(toolbar.IsEffectivelyVisible);
        Assert.False(toolbar.IsEffectivelyEnabled);
        Assert.Empty(window.GetLogicalDescendants().OfType<DrawingToolbar>());
        window.Close();
    }

    [AvaloniaFact]
    public void Tabs_StartRightAfterTheSidePanel_WhichStaysResizable()
    {
        using var f = new ExplorerFixture();
        var window = new MainWindow { DataContext = f.Main, Width = 1200, Height = 800 };
        window.Show();
        f.Main.OpenDiagramCommand.Execute((DiagramItemViewModel)f.Node("A"));
        Dispatcher.UIThread.RunJobs();

        var tabs = window.GetLogicalDescendants().OfType<TabControl>().Single(t => t.Classes.Contains("documents"));
        var tree = window.FindControl<TreeView>("DiagramTree")!;
        var panel = tree.FindAncestorOfType<Border>(includeSelf: false)!;
        while (panel.Parent is not Grid)
        {
            panel = panel.FindAncestorOfType<Border>()!;
        }

        double Right(Visual v) => v.TranslatePoint(new Point(v.Bounds.Width, 0), window)!.Value.X;
        double Left(Visual v) => v.TranslatePoint(new Point(0, 0), window)!.Value.X;
        Assert.Equal(Right(panel), Left(tabs));

        // Drag the panel's right edge 100px to the right.
        var y = tree.TranslatePoint(new Point(0, 100), window)!.Value.Y;
        var edge = Right(panel) - 2;
        window.MouseDown(new Point(edge, y), MouseButton.Left);
        window.MouseMove(new Point(edge + 50, y));
        window.MouseMove(new Point(edge + 100, y));
        window.MouseUp(new Point(edge + 100, y), MouseButton.Left);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(370, Right(panel), precision: 0);
        Assert.Equal(Right(panel), Left(tabs));
        window.Close();
    }

    [AvaloniaFact]
    public void NewAndOpenProjectButtons_ComeBeforeSave_AndWorkWithoutOpenDiagrams()
    {
        var services = new FakeServices();
        var main = new MainWindowViewModel(services, services);
        var window = new MainWindow { DataContext = main };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var newProject = window.FindControl<Button>("NewProjectButton")!;
        var openProject = window.FindControl<Button>("OpenProjectButton")!;
        var save = window.FindControl<Button>("SaveProjectButton")!;
        var undo = window.GetLogicalDescendants().OfType<DiagramToolbar>().Single().GetLogicalDescendants().OfType<Button>().First();

        Assert.Same(main.NewProjectCommand, newProject.Command);
        Assert.Same(main.SaveProjectCommand, save.Command);
        Assert.Same(main.OpenProjectCommand, openProject.Command);
        Assert.Equal($"New project ({Shortcuts.Format(Shortcuts.NewProject)})", ToolTip.GetTip(newProject));
        Assert.Equal($"Open project ({Shortcuts.Format(Shortcuts.OpenProject)})", ToolTip.GetTip(openProject));
        double Left(Visual v) => v.TranslatePoint(default, window)!.Value.X;
        Assert.True(Left(newProject) < Left(openProject) && Left(openProject) < Left(save) && Left(save) < Left(undo));

        // No diagram is open: the project buttons work, the diagram commands do not, and there is nothing to save.
        Assert.True(newProject.IsEffectivelyEnabled);
        Assert.True(openProject.IsEffectivelyEnabled);
        Assert.False(save.IsEffectivelyEnabled);
        Assert.False(undo.IsEffectivelyEnabled);
        window.Close();
    }

    [AvaloniaFact]
    public void EditFontAndShapeButtons_FollowFitToScreen_AndOpenTheirDialogsForTheSelection()
    {
        using var f = new ExplorerFixture();
        var window = new MainWindow { DataContext = f.Main };
        window.Show();
        f.Main.OpenDiagramCommand.Execute((DiagramItemViewModel)f.Node("A"));
        Dispatcher.UIThread.RunJobs();

        var diagram = f.Main.ActiveDiagram!;
        var toolbar = window.GetLogicalDescendants().OfType<DiagramToolbar>().Single();
        var buttons = toolbar.GetLogicalDescendants().OfType<Button>().ToList();
        var fit = buttons.Single(b => b.Command == diagram.ZoomFitCommand);
        var edit = buttons.Single(b => b.Name == "EditButton");
        var font = buttons.Single(b => b.Name == "FontButton");
        var shape = buttons.Single(b => b.Name == "ShapeButton");
        Assert.Equal(buttons.IndexOf(fit) + 1, buttons.IndexOf(edit));
        Assert.Equal(buttons.IndexOf(edit) + 1, buttons.IndexOf(font));
        Assert.Equal(buttons.IndexOf(font) + 1, buttons.IndexOf(shape));

        // Nothing selected: nothing to edit.
        Assert.False(edit.IsEffectivelyEnabled);
        Assert.False(font.IsEffectivelyEnabled);
        Assert.False(shape.IsEffectivelyEnabled);

        diagram.Editor.SelectedId = diagram.Editor.AddClass(ClassifierKind.Class, 100, 100).Id;
        Dispatcher.UIThread.RunJobs();
        Assert.True(edit.IsEffectivelyEnabled);
        Assert.True(font.IsEffectivelyEnabled);
        Assert.True(shape.IsEffectivelyEnabled);

        f.Services.AcceptDialogs = false;
        edit.Command!.Execute(null);
        font.Command!.Execute(null);
        shape.Command!.Execute(null);
        Dispatcher.UIThread.RunJobs();
        Assert.IsType<ClassEditorViewModel>(f.Services.ShownDialogs[^3]);
        Assert.IsType<FontEditorViewModel>(f.Services.ShownDialogs[^2]);
        Assert.IsType<ShapeEditorViewModel>(f.Services.ShownDialogs[^1]);
        window.Close();
    }

    [AvaloniaFact]
    public void DisabledToolbarButtons_KeepATransparentBackground_AndGreyOutTheirIcon()
    {
        var services = new FakeServices();
        var window = new MainWindow { DataContext = new MainWindowViewModel(services, services) };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var presenter = (Button b) => b.GetVisualDescendants().OfType<ContentPresenter>().First(p => p.Name == "PART_ContentPresenter");
        var toolbar = window.GetLogicalDescendants().OfType<DiagramToolbar>().Single();
        var disabled = presenter(toolbar.GetLogicalDescendants().OfType<Button>().First());
        var enabled = presenter(window.FindControl<Button>("NewProjectButton")!);

        Assert.Equal(Colors.Transparent, ((ISolidColorBrush)disabled.Background!).Color);
        var grey = Color.Parse("#B4BAC3");
        Assert.Equal(grey, ((ISolidColorBrush)disabled.Foreground!).Color);
        Assert.NotEqual(grey, ((ISolidColorBrush)enabled.Foreground!).Color);

        // The icon itself takes the grey.
        var icon = disabled.GetVisualDescendants().OfType<Material.Icons.Avalonia.MaterialIcon>().Single();
        Assert.Equal(grey, ((ISolidColorBrush)icon.Foreground!).Color);
        window.Close();
    }

    [Fact]
    public void EveryToolbox_HasADividerRightBeforeNote_ThatIsNoTool()
    {
        foreach (var type in Enum.GetValues<DiagramType>())
        {
            var tools = ToolItem.For(type).ToList();
            var note = tools.FindIndex(t => t.Kind == ToolKind.Note);
            Assert.True(tools[note - 1].IsSeparator, $"{type}");
            Assert.Single(tools, t => t.IsSeparator);
            Assert.False(ToolItem.Separator.IsNodeTool);
            Assert.False(ToolItem.Separator.IsConnectorTool);
        }
    }

    [Fact]
    public void SelectNoteAttachNoteAndTextBox_HaveIcons_ConnectorsAreDrawn_WhileNodeToolsKeepTheirLabels()
    {
        foreach (var type in Enum.GetValues<DiagramType>())
        {
            var tools = ToolItem.For(type);
            Assert.Equal(
                [(ToolKind.Select, Material.Icons.MaterialIconKind.CursorDefaultOutline),
                 (ToolKind.Note, Material.Icons.MaterialIconKind.StickerTextOutline),
                 (ToolKind.NoteAttachment, Material.Icons.MaterialIconKind.VectorLine),
                 (ToolKind.TextBox, Material.Icons.MaterialIconKind.FormTextbox)],
                tools.Where(t => t.HasIcon).Select(t => (t.Kind, t.IconKind)));

            // Every connector tool (but attaching a note, which has an icon) is drawn as its connector, and no two of
            // a toolbox look the same.
            var connectors = tools.Where(t => t.IsConnectorTool && !t.HasIcon).ToList();
            Assert.NotEmpty(connectors);
            Assert.All(connectors, t => Assert.True(t.HasConnector, $"{type}: {t.Label}"));
            Assert.Equal(connectors.Count, connectors.Select(t => t.Connector).Distinct().Count());

            // Tools without a label say what they are first in their tooltip: an icon tool its label, a connector its
            // full name (Synchronous call: for Sync).
            Assert.All(tools.Where(t => t.HasIcon), t => Assert.StartsWith(t.Label + ":", t.ToolTip));
            Assert.All(connectors, t => Assert.Matches(@"^[A-Z][\w ]+: ", t.ToolTip));
            Assert.All(tools.Where(t => !t.HasIcon && !t.HasConnector && !t.IsSeparator), t => Assert.True(t.ShowsLabel));
            Assert.All(connectors, t => Assert.False(t.ShowsLabel));
        }
    }

    [AvaloniaFact]
    public void InTheToolbar_IconToolsShowTheirIcon_AndNoText()
    {
        using var f = new ExplorerFixture();
        var window = new MainWindow { DataContext = f.Main };
        window.Show();
        f.Main.OpenDiagramCommand.Execute((DiagramItemViewModel)f.Node("A"));
        Dispatcher.UIThread.RunJobs();

        var toolbox = window.GetLogicalDescendants().OfType<DrawingToolbar>().Single().GetLogicalDescendants().OfType<ListBox>().Single();
        var tools = f.Main.ActiveDiagram!.Tools.ToList();
        Control Item(ToolKind kind) => toolbox.ContainerFromIndex(tools.FindIndex(t => t.Kind == kind))!;

        var note = Item(ToolKind.Note);
        Assert.Contains(note.GetVisualDescendants().OfType<Material.Icons.Avalonia.MaterialIcon>(), i => i.IsEffectivelyVisible);
        Assert.DoesNotContain(note.GetVisualDescendants().OfType<TextBlock>(), t => t.IsEffectivelyVisible && t.Text == "Note");

        // Select is a mouse pointer, like in paint programs.
        var select = Item(ToolKind.Select);
        Assert.Contains(select.GetVisualDescendants().OfType<Material.Icons.Avalonia.MaterialIcon>(),
            i => i.IsEffectivelyVisible && i.Kind == Material.Icons.MaterialIconKind.CursorDefaultOutline);
        Assert.DoesNotContain(select.GetVisualDescendants().OfType<TextBlock>(), t => t.IsEffectivelyVisible && t.Text == "Select");

        var cls = Item(ToolKind.Class);
        Assert.Contains(cls.GetVisualDescendants().OfType<TextBlock>(), t => t.IsEffectivelyVisible && t.Text == "Class");
        Assert.DoesNotContain(cls.GetVisualDescendants().OfType<Material.Icons.Avalonia.MaterialIcon>(), i => i.IsEffectivelyVisible);
        Assert.DoesNotContain(cls.GetVisualDescendants().OfType<ConnectorIcon>(), i => i.IsEffectivelyVisible);

        // A connector tool is drawn as its connector, as the canvas draws it: composition's filled diamond.
        var composition = Item(ToolKind.Composition);
        var icon = Assert.Single(composition.GetVisualDescendants().OfType<ConnectorIcon>(), i => i.IsEffectivelyVisible);
        Assert.Equal(ConnectorStyle.Of(RelationshipKind.Composition), icon.Connector);
        Assert.Equal(ConnectorHead.FilledDiamond, icon.Connector!.SourceHead);
        Assert.True(icon.Bounds.Width > 0 && icon.Bounds.Height > 0);
        Assert.DoesNotContain(composition.GetVisualDescendants().OfType<TextBlock>(), t => t.IsEffectivelyVisible && t.Text == "Composition");

        // Picking the icon tool still works.
        f.Main.ActiveDiagram.ActiveTool = tools.Single(t => t.Kind == ToolKind.TextBox);
        Dispatcher.UIThread.RunJobs();
        Assert.Same(f.Main.ActiveDiagram.ActiveTool, toolbox.SelectedItem);
        window.Close();
    }

    [AvaloniaFact]
    public void EveryConnectorTool_ShowsItsTooltip_AnywhereOnItsButton()
    {
        // A connector icon only takes the pointer on the line it draws; the tooltip must not depend on hitting it.
        var folder = Directory.CreateTempSubdirectory().FullName;
        try
        {
            var project = new UmlProject { Name = "Tips" };
            foreach (var type in Enum.GetValues<DiagramType>())
            {
                project.Diagrams.Add(DiagramDocument.Create(type.ToString(), type));
            }

            var path = Path.Combine(folder, "Tips.umlwiz");
            Core.Serialization.ProjectSerializer.Save(project, path);
            var services = new FakeServices();
            var main = new MainWindowViewModel(services, services);
            main.OpenProjectFileAsync(path).GetAwaiter().GetResult();
            var window = new MainWindow { DataContext = main };
            window.Show();

            var checkedTools = 0;
            foreach (var type in Enum.GetValues<DiagramType>())
            {
                main.OpenDiagramCommand.Execute((DiagramItemViewModel)main.AllNodes().Single(n => n.Name == type.ToString()));
                Dispatcher.UIThread.RunJobs();
                window.CaptureRenderedFrame(); // hit testing uses the rendered scene
                var toolbox = window.GetVisualDescendants().OfType<DrawingToolbar>().Single(t => t.IsEffectivelyVisible)
                    .GetLogicalDescendants().OfType<ListBox>().Single();
                var tools = main.ActiveDiagram!.Tools.ToList();
                for (var i = 0; i < tools.Count; i++)
                {
                    if (!tools[i].IsConnectorTool)
                    {
                        continue;
                    }

                    Assert.False(string.IsNullOrWhiteSpace(tools[i].ToolTip), $"{type} {tools[i].Label}");
                    var item = toolbox.ContainerFromIndex(i)!;

                    // Just inside the button's corner: empty space, away from the icon's line.
                    var corner = item.TranslatePoint(new Point(2, 2), window)!.Value;
                    var hit = window.InputHitTest(corner) as Visual;
                    var tip = hit?.GetSelfAndVisualAncestors().OfType<Control>().Select(ToolTip.GetTip).FirstOrDefault(t => t is not null);
                    Assert.True(tools[i].ToolTip.Equals(tip), $"{type} {tools[i].Label}: {tip ?? "no tooltip"}");
                    checkedTools++;
                }
            }

            Assert.True(checkedTools >= 20);
            window.Close();
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [AvaloniaFact]
    public void TheToolboxDivider_CannotBeSelected()
    {
        using var f = new ExplorerFixture();
        var window = new MainWindow { DataContext = f.Main };
        window.Show();
        f.Main.OpenDiagramCommand.Execute((DiagramItemViewModel)f.Node("A"));
        Dispatcher.UIThread.RunJobs();

        var toolbox = window.GetLogicalDescendants().OfType<DrawingToolbar>().Single().GetLogicalDescendants().OfType<ListBox>().Single();
        var index = f.Main.ActiveDiagram!.Tools.ToList().FindIndex(t => t.IsSeparator);
        var divider = (ListBoxItem)toolbox.ContainerFromIndex(index)!;

        Assert.False(divider.IsEnabled);
        Assert.Contains(divider.GetVisualDescendants().OfType<Avalonia.Controls.Shapes.Rectangle>(), r => r.IsEffectivelyVisible);
        window.Close();
    }

    [AvaloniaFact]
    public void MoveTab_ReordersOpenDiagrams_AndKeepsItActive()
    {
        using var f = new ExplorerFixture();
        f.Main.OpenDiagramCommand.Execute((DiagramItemViewModel)f.Node("A"));
        f.Main.OpenDiagramCommand.Execute((DiagramItemViewModel)f.Node("B"));
        var a = f.Main.OpenDiagrams[0];

        f.Main.MoveTab(a, 5);

        Assert.Equal(["B", "A"], f.Main.OpenDiagrams.Select(d => d.Name));
        Assert.Same(a, f.Main.ActiveDiagram);
    }

    [AvaloniaFact]
    public void DraggingATab_AlongTheStrip_ReordersTheTabs()
    {
        using var f = new ExplorerFixture();
        var window = new MainWindow { DataContext = f.Main, Width = 1200, Height = 800 };
        window.Show();
        f.Main.OpenDiagramCommand.Execute((DiagramItemViewModel)f.Node("A"));
        f.Main.OpenDiagramCommand.Execute((DiagramItemViewModel)f.Node("B"));
        Dispatcher.UIThread.RunJobs();

        var tabs = window.FindControl<TabControl>("DocumentTabs")!;
        var first = (TabItem)tabs.ContainerFromIndex(0)!;
        var second = (TabItem)tabs.ContainerFromIndex(1)!;
        var start = first.TranslatePoint(new Point(20, first.Bounds.Height / 2), window)!.Value;
        var end = second.TranslatePoint(new Point(second.Bounds.Width - 5, second.Bounds.Height / 2), window)!.Value;

        window.MouseDown(start, MouseButton.Left);
        window.MouseMove(start + new Point(10, 0));
        window.MouseMove(end);
        window.MouseUp(end, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(["B", "A"], f.Main.OpenDiagrams.Select(d => d.Name));
        Assert.Equal("A", f.Main.ActiveDiagram?.Name);

        // A plain click selects a tab without moving it.
        var b = (TabItem)tabs.ContainerFromIndex(0)!;
        var click = b.TranslatePoint(new Point(20, b.Bounds.Height / 2), window)!.Value;
        window.MouseDown(click, MouseButton.Left);
        window.MouseUp(click, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(["B", "A"], f.Main.OpenDiagrams.Select(d => d.Name));
        Assert.Equal("B", f.Main.ActiveDiagram?.Name);
        window.Close();
    }

    [AvaloniaFact]
    public void DiagramMenu_RenameMoveDeleteExport_AreEnabledOnlyWithADiagramSelected()
    {
        using var f = new ExplorerFixture();
        var window = new MainWindow { DataContext = f.Main };
        window.Show();
        var diagramMenu = window.GetLogicalDescendants().OfType<MenuItem>().Single(m => (string?)m.Header == "_Diagram");
        diagramMenu.Open();
        Dispatcher.UIThread.RunJobs();
        var export = diagramMenu.Items.OfType<MenuItem>().Single(m => (string?)m.Header == "_Export");

        // A submenu's items are part of the window only while it is open.
        export.Open();
        Dispatcher.UIThread.RunJobs();
        var items = diagramMenu.Items.OfType<MenuItem>()
            .Where(m => (string?)m.Header is "_Rename…" or "_Move To…" or "_Delete…" or "_Properties…")
            .Concat(export.Items.OfType<MenuItem>().Where(m => (string?)m.Header is "PNG _Image…" or "_PlantUML…"))
            .ToList();
        Assert.Equal(6, items.Count);

        f.Main.SelectedNode = f.Node("Model");
        Dispatcher.UIThread.RunJobs();
        Assert.All(items, m => Assert.False(m.IsEffectivelyEnabled));

        f.Main.SelectedNode = f.Node("A");
        Dispatcher.UIThread.RunJobs();
        Assert.All(items, m => Assert.True(m.IsEffectivelyEnabled));

        f.Main.SelectedNode = null;
        Dispatcher.UIThread.RunJobs();
        Assert.All(items, m => Assert.False(m.IsEffectivelyEnabled));

        // The commands themselves still work on folders, for the explorer's context menu, which has no such restriction.
        f.Main.SelectedNode = f.Node("Model");
        Assert.True(f.Main.RenameItemCommand.CanExecute(null));
        var tree = window.FindControl<TreeView>("DiagramTree")!;
        Assert.All(
            tree.ContextMenu!.Items.OfType<MenuItem>(),
            m => Assert.False(m.IsSet(MenuItem.IsEnabledProperty)));
        window.Close();
    }

    [AvaloniaFact]
    public void DiagramMenu_HoldsTheDiagramCommands()
    {
        var services = new FakeServices();
        var main = new MainWindowViewModel(services, services);
        var window = new MainWindow { DataContext = main };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        MenuItem TopMenu(string header) =>
            window.GetLogicalDescendants().OfType<MenuItem>().Single(m => (string?)m.Header == header);
        var diagram = TopMenu("_Diagram");
        Assert.Equal(
            ["_New Diagram", "_Create with AI…", "_Rename…", "_Move To…", "_Delete…", "_Import", "_Export", "_Generate Code…", "Close _All Diagrams", "_Properties…"],
            diagram.Items.OfType<MenuItem>().Select(m => (string?)m.Header));

        // Imports and exports are grouped by what they do, then by format.
        IEnumerable<string?> Submenu(string header) =>
            diagram.Items.OfType<MenuItem>().Single(m => (string?)m.Header == header).Items.OfType<MenuItem>().Select(m => (string?)m.Header);
        Assert.Equal(["_OpenAPI…", "_PlantUML…"], Submenu("_Import"));
        Assert.Equal(["PNG _Image…", "_OpenAPI…", "_PlantUML…"], Submenu("_Export"));
        var newDiagram = diagram.Items.OfType<MenuItem>().First();
        Assert.Same(main.NewDiagramMenu, newDiagram.ItemsSource);
        Assert.Equal(
            [DiagramType.Class, DiagramType.Sequence, DiagramType.StateMachine, DiagramType.Activity, DiagramType.UseCase, DiagramType.Component,
             DiagramType.Deployment],
            main.NewDiagramMenu.Select(e => e.Type));
        window.Close();
    }

    [AvaloniaFact]
    public void FileMenu_HoldsTheProjectCommands_InGroups()
    {
        var services = new FakeServices();
        var window = new MainWindow { DataContext = new MainWindowViewModel(services, services) };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var menu = window.FindControl<Menu>("MainMenu")!;
        var file = menu.Items.OfType<MenuItem>().First();
        Assert.Equal(
            [
                "_New Project…", "_Open Project…", "Import So_urce Folder…", "Recent _Projects", "-",
                "_Save Project", "Save Project _As…", "-",
                "New _Folder…", "Project Propert_ies…", "-",
                "_Close Project", "-",
                "Pr_eferences…", "-",
                "E_xit",
            ],
            file.Items.Select(i => i is Separator ? "-" : (string?)((MenuItem)i!).Header));

        // There is no Project menu any more; without a project its commands are disabled.
        Assert.Equal(["_File", "_Edit", "_View", "F_ormat", "_Diagram", "_Help"], menu.Items.OfType<MenuItem>().Select(m => (string?)m.Header));
        Assert.All(
            file.Items.OfType<MenuItem>().Where(m => (string?)m.Header is "New _Folder…" or "Project Propert_ies…"),
            m => Assert.False(m.IsEnabled));
        window.Close();
    }
}
