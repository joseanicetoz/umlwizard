using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using UmlWizard.App.ViewModels;

namespace UmlWizard.App.Views;

public sealed partial class MainWindow : Window
{
    /// <summary>Drag-and-drop format of a project explorer node; the dragged node itself is kept in <see cref="_draggedNode"/>.</summary>
    private static readonly DataFormat<string> ProjectNodeFormat = DataFormat.CreateStringApplicationFormat("umlwizard-project-node");

    private bool _closeConfirmed;

    private (ProjectNodeViewModel Node, Point Origin)? _dragCandidate;

    private ProjectNodeViewModel? _draggedNode;

    private TreeViewItem? _dropHighlight;

    /// <summary>A tab being dragged: its diagram, where it was pressed, and the press offset from the tab's left edge.</summary>
    private (DiagramViewModel Diagram, Point Origin, double GrabOffset, bool Dragging)? _tabDrag;

    /// <summary>A dragged diagram placed at <c>Index</c> among the other diagrams of <c>Folder</c>; the insertion line is drawn above or below <c>Anchor</c>.</summary>
    private readonly record struct DiagramDrop(DiagramItemViewModel Item, FolderNodeViewModel? Folder, int Index, TreeViewItem? Anchor, bool After);

    /// <summary>The width of the Project Explorer column while it is hidden, restored when it is shown again.</summary>
    private GridLength _projectExplorerWidth;

    /// <summary>The width of the AI chat column while it is hidden, restored when it is shown again.</summary>
    private GridLength _aiChatWidth = new(380);

    public MainWindow()
    {
        InitializeComponent();

        // Hiding the Project Explorer (View → Project Explorer) also collapses its column, so the tabs take the space.
        ProjectExplorer.PropertyChanged += (_, e) =>
        {
            if (e.Property == IsVisibleProperty)
            {
                ApplyProjectExplorerVisibility();
            }
        };

        // The AI chat (View → AI Chat) takes a column on the right while it is shown.
        AiChatHost.PropertyChanged += (_, e) =>
        {
            if (e.Property == IsVisibleProperty)
            {
                ApplyAiChatVisibility();
            }
        };
        ApplyAiChatVisibility();

        // TreeViewItems handle the press to select themselves, so listen to handled events as well.
        DiagramTree.AddHandler(PointerPressedEvent, OnTreePointerPressed, RoutingStrategies.Bubble, handledEventsToo: true);
        DiagramTree.AddHandler(PointerMovedEvent, OnTreePointerMoved, RoutingStrategies.Bubble, handledEventsToo: true);
        DiagramTree.AddHandler(PointerReleasedEvent, OnTreePointerReleased, RoutingStrategies.Bubble, handledEventsToo: true);
        DiagramTree.AddHandler(DragDrop.DragOverEvent, OnTreeDragOver);
        DiagramTree.AddHandler(DragDrop.DragLeaveEvent, (_, _) =>
        {
            HighlightDropTarget(null);
            ShowDropLine(null);
        });
        DiagramTree.AddHandler(DragDrop.DropEvent, OnTreeDrop);

        // Enter opens the selected diagram; tree items handle keys themselves (e.g. expanding folders) first.
        DiagramTree.AddHandler(KeyDownEvent, OnDiagramTreeKeyDown, RoutingStrategies.Bubble, handledEventsToo: true);

        // A menu normally closes after a click by walking up from the clicked item. Commands such as opening a
        // recent project rebuild the Recent Projects list, which detaches the clicked item first, so the menu
        // stayed open; close it explicitly instead.
        MainMenu.AddHandler(MenuItem.ClickEvent, OnMainMenuItemClick, RoutingStrategies.Bubble, handledEventsToo: true);

        // Tabs are reordered by dragging them along the tab strip. TabItems handle the press to select themselves.
        DocumentTabs.AddHandler(PointerPressedEvent, OnTabPointerPressed, RoutingStrategies.Bubble, handledEventsToo: true);
        DocumentTabs.AddHandler(PointerMovedEvent, OnTabPointerMoved, RoutingStrategies.Bubble, handledEventsToo: true);
        DocumentTabs.AddHandler(PointerReleasedEvent, (_, _) => EndTabDrag(), RoutingStrategies.Bubble, handledEventsToo: true);
        DocumentTabs.AddHandler(PointerCaptureLostEvent, (_, _) => EndTabDrag(), RoutingStrategies.Bubble, handledEventsToo: true);

        // Another application may have changed the clipboard while this window was in the background.
        Activated += (_, _) => _ = ViewModel?.ActiveDiagram?.RefreshCanPasteAsync();
    }

    private MainWindowViewModel? ViewModel => DataContext as MainWindowViewModel;

    /// <summary>
    /// Collapses the Project Explorer's column when the panel is hidden, and gives back the width it had (as resized
    /// with the splitter) when it is shown.
    /// </summary>
    private void ApplyProjectExplorerVisibility()
    {
        var column = Workspace.ColumnDefinitions[0];
        if (!ProjectExplorer.IsVisible && column.Width.Value > 0)
        {
            _projectExplorerWidth = column.Width;
            column.Width = new GridLength(0);
        }
        else if (ProjectExplorer.IsVisible && column.Width.Value == 0)
        {
            column.Width = _projectExplorerWidth.Value > 0 ? _projectExplorerWidth : new GridLength(270);
        }
    }

    /// <summary>Gives the AI chat its column while it is shown (the width it had, as resized), and collapses it when it is not.</summary>
    private void ApplyAiChatVisibility()
    {
        var column = Workspace.ColumnDefinitions[2];
        if (!AiChatHost.IsVisible && column.Width.Value > 0)
        {
            _aiChatWidth = column.Width;
            column.Width = new GridLength(0);
        }
        else if (AiChatHost.IsVisible && column.Width.Value == 0)
        {
            column.Width = _aiChatWidth;
        }
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (ViewModel is { } vm)
        {
            vm.ExitRequested += (_, _) => Close();
        }
    }

    protected override async void OnClosing(WindowClosingEventArgs e)
    {
        base.OnClosing(e);
        if (ViewModel is not { } vm)
        {
            return;
        }

        if (!_closeConfirmed && vm.OpenDiagrams.Any(d => d.IsDirty))
        {
            // Cancel first, then close again once the user has decided.
            e.Cancel = true;
            if (await vm.ConfirmSaveChangesAsync())
            {
                _closeConfirmed = true;
                Close();
            }

            return;
        }

        // The project stays open until the application exits; remember its tabs and expanded folders for next time.
        vm.SaveUserState();
    }

    /// <summary>Enter opens the diagram selected in the explorer, as a click does.</summary>
    private void OnDiagramTreeKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && ViewModel?.SelectedDiagramItem is { } item)
        {
            ViewModel.OpenDiagramCommand.Execute(item);
            e.Handled = true;
        }
    }

    /// <summary>
    /// A click on a diagram in the explorer opens it, or activates its tab: a left press on it that did not turn into a
    /// drag (dragging moves or reorders it instead). This does not use the Tapped gesture, which the press cancels
    /// when selecting a closed diagram removes the active tab's canvas.
    /// </summary>
    private void OnTreePointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_dragCandidate is { Node: DiagramItemViewModel item } && e.InitialPressMouseButton == MouseButton.Left)
        {
            ViewModel?.OpenDiagramCommand.Execute(item);
        }

        _dragCandidate = null;
    }

    private void OnTabPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        _tabDrag = null;

        // Presses on a tab's close button are not drags.
        if (!e.GetCurrentPoint(DocumentTabs).Properties.IsLeftButtonPressed ||
            e.Source is not Visual source || source.FindAncestorOfType<Button>(includeSelf: true) is not null ||
            source.FindAncestorOfType<TabItem>(includeSelf: true) is not { DataContext: DiagramViewModel diagram } tab)
        {
            return;
        }

        var position = e.GetPosition(DocumentTabs);
        _tabDrag = (diagram, position, e.GetPosition(tab).X, false);
    }

    private void OnTabPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_tabDrag is not { } drag || ViewModel is not { } vm)
        {
            return;
        }

        var position = e.GetPosition(DocumentTabs);
        if (!drag.Dragging)
        {
            if (Math.Abs(position.X - drag.Origin.X) < 6)
            {
                return;
            }

            _tabDrag = drag = drag with { Dragging = true };
            e.Pointer.Capture(DocumentTabs);
            DocumentTabs.Cursor = new Cursor(StandardCursorType.DragMove);
        }

        vm.MoveTab(drag.Diagram, TabDropIndex(drag.Diagram, position.X - drag.GrabOffset));
    }

    /// <summary>
    /// The index the dragged tab belongs at when its left edge is at <paramref name="left"/>: the number of other tabs
    /// whose centre lies left of the dragged tab's centre, measured as if the dragged tab were not in the strip. This
    /// depends only on the pointer, so tabs of different widths do not flip back and forth.
    /// </summary>
    private int TabDropIndex(DiagramViewModel dragged, double left)
    {
        var tabs = Enumerable.Range(0, DocumentTabs.ItemCount)
            .Select(i => DocumentTabs.ContainerFromIndex(i))
            .OfType<TabItem>()
            .ToList();
        var draggedTab = tabs.FirstOrDefault(t => t.DataContext == dragged);
        if (draggedTab is null || tabs.Count == 0)
        {
            return 0;
        }

        var x = tabs[0].TranslatePoint(default, DocumentTabs)?.X ?? 0;
        var draggedCentre = left + draggedTab.Bounds.Width / 2;
        var index = 0;
        foreach (var tab in tabs.Where(t => t != draggedTab))
        {
            if (x + tab.Bounds.Width / 2 < draggedCentre)
            {
                index++;
            }

            x += tab.Bounds.Width;
        }

        return index;
    }

    private void EndTabDrag()
    {
        if (_tabDrag is { Dragging: true })
        {
            DocumentTabs.Cursor = null;
        }

        _tabDrag = null;
    }

    private void OnMainMenuItemClick(object? sender, RoutedEventArgs e)
    {
        if (e.Source is MenuItem { HasSubMenu: false, StaysOpenOnClick: false })
        {
            MainMenu.Close();
        }
    }

    private void OnTreePointerPressed(object? sender, PointerPressedEventArgs e)
    {
        // The project root cannot be dragged; it is not a folder of the project.
        _dragCandidate = e.GetCurrentPoint(DiagramTree).Properties.IsLeftButtonPressed && NodeAt(e.Source) is { } node and not ProjectRootNodeViewModel
            ? (node, e.GetPosition(DiagramTree))
            : null;
    }

    private async void OnTreePointerMoved(object? sender, PointerEventArgs e)
    {
        if (_dragCandidate is not { } candidate)
        {
            return;
        }

        if (!e.GetCurrentPoint(DiagramTree).Properties.IsLeftButtonPressed)
        {
            _dragCandidate = null;
            return;
        }

        var delta = e.GetPosition(DiagramTree) - candidate.Origin;
        if (Math.Abs(delta.X) < 4 && Math.Abs(delta.Y) < 4)
        {
            return;
        }

        _dragCandidate = null;
        _draggedNode = candidate.Node;
        try
        {
            var data = new DataTransfer();
            data.Add(DataTransferItem.Create(ProjectNodeFormat, candidate.Node.Id));
            await DragDrop.DoDragDropAsync(e, data, DragDropEffects.Move);
        }
        finally
        {
            _draggedNode = null;
            HighlightDropTarget(null);
            ShowDropLine(null);
        }
    }

    private void OnTreeDragOver(object? sender, DragEventArgs e)
    {
        if (DiagramInsertion(e) is { } insertion)
        {
            var canInsert = ViewModel!.CanMoveDiagram(insertion.Item, insertion.Folder?.Id, insertion.Index);
            e.DragEffects = canInsert ? DragDropEffects.Move : DragDropEffects.None;
            HighlightDropTarget(null);
            ShowDropLine(canInsert ? insertion : null);
        }
        else
        {
            var target = DropTarget(e);
            e.DragEffects = target is { } folder && ViewModel!.CanMoveNode(_draggedNode!, folder.Id)
                ? DragDropEffects.Move
                : DragDropEffects.None;
            HighlightDropTarget(e.DragEffects == DragDropEffects.Move ? target!.Value.Node : null);
            ShowDropLine(null);
        }

        e.Handled = true;
    }

    private async void OnTreeDrop(object? sender, DragEventArgs e)
    {
        var insertion = DiagramInsertion(e);
        var target = insertion is null ? DropTarget(e) : null;
        HighlightDropTarget(null);
        ShowDropLine(null);
        if (insertion is { } i)
        {
            e.Handled = true;
            await ViewModel!.MoveDiagramAsync(i.Item, i.Folder?.Id, i.Index);
        }
        else if (target is { } t && _draggedNode is { } node)
        {
            e.Handled = true;
            await ViewModel!.MoveNodeAsync(node, t.Id);
        }
    }

    /// <summary>
    /// Where a dragged diagram goes when it is not dropped onto a folder: above or below the diagram under the pointer
    /// (by which half the pointer is in), or last at the top level over the project root or empty space. <c>Index</c> counts the other
    /// diagrams of <c>Folder</c>; the line is drawn at <c>Anchor</c>. <c>null</c> when the pointer is over a folder,
    /// or a folder is dragged: those go <em>into</em> a folder (see <see cref="DropTarget"/>).
    /// </summary>
    private DiagramDrop? DiagramInsertion(DragEventArgs e)
    {
        if (_draggedNode is not DiagramItemViewModel dragged || ViewModel is not { } vm ||
            !e.DataTransfer.Formats.Contains(ProjectNodeFormat))
        {
            return null;
        }

        var container = (e.Source as Visual)?.FindAncestorOfType<TreeViewItem>(includeSelf: true);
        switch (container?.DataContext)
        {
            case DiagramItemViewModel over:
            {
                var after = e.GetPosition(container).Y > container.Bounds.Height / 2;
                var index = over == dragged
                    ? vm.DiagramsIn(over.Parent).ToList().IndexOf(dragged)
                    : vm.DiagramsIn(over.Parent).Where(d => d != dragged).ToList().IndexOf(over) + (after ? 1 : 0);
                return new DiagramDrop(dragged, over.Parent, index, container, after);
            }
            case null or ProjectRootNodeViewModel:
            {
                var topLevel = vm.DiagramsIn(null).Where(d => d != dragged).ToList();
                var last = topLevel.Count == 0 ? null : DiagramTree.TreeContainerFromItem(topLevel[^1]) as TreeViewItem;
                return new DiagramDrop(dragged, null, topLevel.Count, last, true);
            }
            default:
                return null;
        }
    }

    /// <summary>Draws the insertion line above or below the insertion's anchor, from its content to the tree's right edge; hides it for <c>null</c>.</summary>
    private void ShowDropLine(DiagramDrop? insertion)
    {
        var anchor = insertion?.Anchor;
        var content = anchor?.FindDescendantOfType<StackPanel>();
        if (anchor is null || content?.TranslatePoint(default, DropLineLayer) is not { } left ||
            anchor.TranslatePoint(new Point(0, insertion!.Value.After ? anchor.Bounds.Height : 0), DropLineLayer) is not { } edge)
        {
            DropLine.IsVisible = false;
            return;
        }

        Canvas.SetLeft(DropLine, left.X);
        Canvas.SetTop(DropLine, edge.Y - DropLine.Height / 2);
        DropLine.Width = Math.Max(0, DropLineLayer.Bounds.Width - left.X - 4);
        DropLine.IsVisible = true;
    }

    /// <summary>
    /// The folder a drop at the pointer goes into: the folder under the pointer, the folder of the diagram under
    /// the pointer, or the top level (<c>Id</c> <c>null</c>, <c>Node</c> the project root) over the project root or
    /// empty space. Diagrams dragged anywhere but onto a folder are placed by <see cref="DiagramInsertion"/> instead.
    /// </summary>
    private (string? Id, ProjectNodeViewModel? Node)? DropTarget(DragEventArgs e)
    {
        if (_draggedNode is null || ViewModel is null || !e.DataTransfer.Formats.Contains(ProjectNodeFormat))
        {
            return null;
        }

        var folder = NodeAt(e.Source) switch
        {
            FolderNodeViewModel f => f,
            { } node => node.Parent,
            null => null,
        };
        return folder is null ? (null, ViewModel.ProjectRoot) : (folder.Id, folder);
    }

    private void HighlightDropTarget(ProjectNodeViewModel? node)
    {
        var item = node is null ? null : DiagramTree.TreeContainerFromItem(node) as TreeViewItem;
        if (item == _dropHighlight)
        {
            return;
        }

        _dropHighlight?.Classes.Remove("dropTarget");
        _dropHighlight = item;
        _dropHighlight?.Classes.Add("dropTarget");
    }

    /// <summary>The explorer node of the tree item that contains <paramref name="source"/>.</summary>
    private static ProjectNodeViewModel? NodeAt(object? source) =>
        (source as Visual)?.FindAncestorOfType<TreeViewItem>(includeSelf: true)?.DataContext as ProjectNodeViewModel;
}
