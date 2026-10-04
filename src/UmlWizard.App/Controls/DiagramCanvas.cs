using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Material.Icons;
using Material.Icons.Avalonia;
using SkiaSharp;
using UmlWizard.App.Resources;
using UmlWizard.App.ViewModels;
using UmlWizard.Core.Editing;
using UmlWizard.Core.Model;
using UmlWizard.Core.Rendering;

namespace UmlWizard.App.Controls;

/// <summary>
/// Interactive diagram surface. Rendering is delegated to the SkiaSharp <see cref="DiagramRenderer"/>;
/// this control translates pointer and keyboard input into <see cref="DiagramEditor"/> operations and hosts the
/// in-line text editor. Its <c>DataContext</c> is a <see cref="DiagramViewModel"/>.
/// </summary>
public sealed class DiagramCanvas : Control
{
    private const double MinZoom = DiagramViewModel.MinZoom;
    private const double MaxZoom = DiagramViewModel.MaxZoom;
    private const double ZoomStep = 1.15;
    private const float GridSize = 20f;

    private static readonly Cursor DefaultCursor = new(StandardCursorType.Arrow);
    private static readonly Cursor ResizeCursor = new(StandardCursorType.BottomRightCorner);
    private static readonly Cursor MoveCursor = new(StandardCursorType.SizeAll);
    private static readonly Cursor ConnectCursor = new(StandardCursorType.Cross);
    private static readonly Cursor PanCursor = new(StandardCursorType.Hand);

    private readonly TextBox _inlineEditor;
    private DiagramViewModel? _vm;
    private InlineEditSession? _session;

    private DragMode _dragMode;
    private string? _dragElementId;
    private string? _dragSnapshot;

    /// <summary>The diagram before the arrow keys started moving the selection; recorded as one undo step when they are released.</summary>
    private string? _nudgeSnapshot;
    private bool _dragMoved;
    private Point _dragStartScreen;
    private SKPoint _dragStartWorld;
    private double _originX;
    private double _originY;
    private double _originWidth;
    private double _originHeight;
    private SKPoint _connectorEnd;
    private string? _connectorTargetId;
    private Rect _inlineEditorRect;

    public DiagramCanvas()
    {
        Focusable = true;
        ClipToBounds = true;

        _inlineEditor = new TextBox { IsVisible = false, AcceptsReturn = false };
        _inlineEditor.Classes.Add("inline");
        // Tunnel, so Enter and Esc are seen before a multi-line editor turns Enter into a line break.
        _inlineEditor.AddHandler(KeyDownEvent, OnInlineEditorKeyDown, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        _inlineEditor.LostFocus += (_, _) => CommitInlineEdit();
        VisualChildren.Add(_inlineEditor);
        LogicalChildren.Add(_inlineEditor);

        // Redraw in the other palette when the application (or an ancestor) switches between light and dark.
        ActualThemeVariantChanged += (_, _) => InvalidateVisual();
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        _inlineEditor.Measure(_inlineEditorRect.Size);
        return default;
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        _inlineEditor.Arrange(_inlineEditorRect);
        return finalSize;
    }

    private enum DragMode
    {
        None,
        Move,
        Resize,
        Pan,
        Connector,
    }

    private ViewState View => _vm!.Document.Layout.View;

    protected override void OnDataContextChanged(EventArgs e)
    {
        CommitInlineEdit();
        Detach();
        _vm = DataContext as DiagramViewModel;
        if (_vm is not null)
        {
            _vm.Editor.Changed += OnDiagramChanged;
            _vm.Editor.SelectionChanged += OnDiagramChanged;
            _vm.ZoomRequested += OnZoomRequested;
            _vm.RedrawRequested += OnDiagramChanged;
        }

        base.OnDataContextChanged(e);
        InvalidateVisual();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        CommitInlineEdit();
        Detach();
        _vm = null;
        base.OnDetachedFromVisualTree(e);
    }

    private void Detach()
    {
        EndNudge();
        if (_vm is not null)
        {
            _vm.Editor.Changed -= OnDiagramChanged;
            _vm.Editor.SelectionChanged -= OnDiagramChanged;
            _vm.ZoomRequested -= OnZoomRequested;
            _vm.RedrawRequested -= OnDiagramChanged;
        }
    }

    private void OnDiagramChanged(object? sender, EventArgs e) => InvalidateVisual();

    #region Rendering

    /// <summary>The diagram colours for the theme the canvas is shown in.</summary>
    internal RenderTheme DiagramTheme => ActualThemeVariant == ThemeVariant.Dark ? RenderTheme.Dark : RenderTheme.Light;

    public override void Render(DrawingContext context)
    {
        var theme = DiagramTheme;

        // Also makes the whole surface hit-testable for pointer input.
        var background = theme.CanvasBackground;
        context.FillRectangle(new SolidColorBrush(new Color(background.Alpha, background.Red, background.Green, background.Blue)), new Rect(Bounds.Size));
        if (_vm is null || Bounds.Width <= 0 || Bounds.Height <= 0)
        {
            return;
        }

        var document = _vm.Document;
        var view = View;
        _vm.Renderer.Theme = theme;
        _vm.Renderer.UpdateLayout(document);

        using var recorder = new SKPictureRecorder();
        var canvas = recorder.BeginRecording(SKRect.Create((float)Bounds.Width, (float)Bounds.Height));
        canvas.Clear(theme.CanvasBackground);
        DrawGrid(canvas, (float)Bounds.Width, (float)Bounds.Height, view, theme.Grid);

        canvas.Translate((float)view.OffsetX, (float)view.OffsetY);
        canvas.Scale((float)view.Zoom);

        var connecting = _dragMode == DragMode.Connector;
        // Messages are horizontal; note attachments go anywhere.
        var connectorEnd = document.DiagramType == DiagramType.Sequence && _vm.ActiveTool.Kind != ToolKind.NoteAttachment
            ? new SKPoint(_connectorEnd.X, _dragStartWorld.Y)
            : _connectorEnd;
        _vm.Renderer.Render(canvas, document, new Core.Rendering.RenderOptions
        {
            SelectedId = _vm.Editor.SelectedId,
            HighlightId = connecting ? _connectorTargetId : null,
            ConnectorStart = connecting ? _dragStartWorld : null,
            ConnectorEnd = connecting ? connectorEnd : null,
            ShowTextBoxOutlines = true,
        });

        context.Custom(new SkiaPictureDrawOperation(new Rect(Bounds.Size), recorder.EndRecording()));
    }

    private static void DrawGrid(SKCanvas canvas, float width, float height, ViewState view, SKColor color)
    {
        var step = GridSize * (float)view.Zoom;
        while (step < 12f)
        {
            step *= 2f;
        }

        var startX = (float)(view.OffsetX % step);
        var startY = (float)(view.OffsetY % step);
        if (startX < 0)
        {
            startX += step;
        }

        if (startY < 0)
        {
            startY += step;
        }

        var points = new List<SKPoint>();
        for (var x = startX; x < width; x += step)
        {
            for (var y = startY; y < height; y += step)
            {
                points.Add(new SKPoint(x, y));
            }
        }

        using var paint = new SKPaint
        {
            Color = color,
            StrokeWidth = 1.6f,
            StrokeCap = SKStrokeCap.Round,
            IsAntialias = true,
        };
        canvas.DrawPoints(SKPointMode.Points, points.ToArray(), paint);
    }

    #endregion

    #region Coordinates and zoom

    private SKPoint ToWorld(Point screen) => new(
        (float)((screen.X - View.OffsetX) / View.Zoom),
        (float)((screen.Y - View.OffsetY) / View.Zoom));

    private Point ToScreen(float x, float y) => new(x * View.Zoom + View.OffsetX, y * View.Zoom + View.OffsetY);

    private void ZoomAt(Point screen, double zoom)
    {
        CommitInlineEdit();
        var world = ToWorld(screen);
        var view = View;
        view.Zoom = Math.Clamp(zoom, MinZoom, MaxZoom);
        view.OffsetX = screen.X - world.X * view.Zoom;
        view.OffsetY = screen.Y - world.Y * view.Zoom;
        _vm!.RefreshZoom();
        InvalidateVisual();
    }

    private void OnZoomRequested(object? sender, ZoomAction action)
    {
        if (_vm is null)
        {
            return;
        }

        var center = new Point(Bounds.Width / 2, Bounds.Height / 2);
        switch (action)
        {
            case ZoomAction.In: ZoomAt(center, View.Zoom * ZoomStep); break;
            case ZoomAction.Out: ZoomAt(center, View.Zoom / ZoomStep); break;
            case ZoomAction.Reset: ZoomAt(center, 1.0); break;
            case ZoomAction.Fit: ZoomToFit(); break;
        }
    }

    private void ZoomToFit()
    {
        _vm!.Renderer.UpdateLayout(_vm.Document);
        var bounds = _vm.Renderer.GetContentBounds(_vm.Document);
        if (bounds.IsEmpty || Bounds.Width <= 0 || Bounds.Height <= 0)
        {
            return;
        }

        bounds.Inflate(40, 40);
        var view = View;
        view.Zoom = Math.Clamp(Math.Min(Bounds.Width / bounds.Width, Bounds.Height / bounds.Height), MinZoom, 1.5);

        // Centred when it fits; a diagram too large even at the smallest zoom shows from its top-left corner, not from
        // its middle, which on a large spread-out diagram can be empty.
        var (width, height) = (bounds.Width * view.Zoom, bounds.Height * view.Zoom);
        view.OffsetX = (width <= Bounds.Width ? (Bounds.Width - width) / 2 : 0) - bounds.Left * view.Zoom;
        view.OffsetY = (height <= Bounds.Height ? (Bounds.Height - height) / 2 : 0) - bounds.Top * view.Zoom;
        _vm.RefreshZoom();
        InvalidateVisual();
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        if (_vm is null)
        {
            return;
        }

        if (Shortcuts.IsCommand(e.KeyModifiers))
        {
            ZoomAt(e.GetPosition(this), View.Zoom * (e.Delta.Y > 0 ? ZoomStep : 1 / ZoomStep));
        }
        else
        {
            CommitInlineEdit();
            var horizontal = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
            View.OffsetX += (horizontal ? e.Delta.Y : e.Delta.X) * 50;
            View.OffsetY += horizontal ? 0 : e.Delta.Y * 50;
            InvalidateVisual();
        }

        e.Handled = true;
    }

    #endregion

    #region Pointer input

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (_vm is null || IsInsideInlineEditor(e.Source))
        {
            return;
        }

        CommitInlineEdit();
        EndNudge();
        Focus();

        var position = e.GetPosition(this);
        var world = ToWorld(position);
        var properties = e.GetCurrentPoint(this).Properties;
        var editor = _vm.Editor;
        var hit = _vm.Renderer.HitTest(_vm.Document, world, editor.SelectedId);
        e.Handled = true;

        if (properties.IsRightButtonPressed)
        {
            editor.SelectedId = hit?.ElementId;
            ShowContextMenu(hit, world);
            return;
        }

        if (properties.IsMiddleButtonPressed)
        {
            StartPan(position, e);
            return;
        }

        if (!properties.IsLeftButtonPressed)
        {
            return;
        }

        var tool = _vm.ActiveTool;
        if (tool.IsNodeTool)
        {
            _vm.CreateNode(tool, world);

            // A new note or text box is typed into straight away.
            if (tool.Kind is ToolKind.TextBox or ToolKind.Note && editor.SelectedId is { } textId)
            {
                BeginInlineEdit(new HitInfo(textId, HitPart.Text));
            }

            return;
        }

        if (tool.IsConnectorTool)
        {
            if (IsConnectorEnd(hit))
            {
                _dragMode = DragMode.Connector;
                _dragElementId = hit.ElementId;
                _dragStartWorld = world;
                _connectorEnd = world;
                e.Pointer.Capture(this);
            }

            return;
        }

        if (e.ClickCount == 2 && hit is not null)
        {
            BeginInlineEdit(hit);
            return;
        }

        if (hit is null)
        {
            editor.SelectedId = null;
            StartPan(position, e);
            return;
        }

        editor.SelectedId = hit.ElementId;
        var layout = _vm.Document.Layout.Find(hit.ElementId);
        if (layout is null || hit.Part == HitPart.Relationship)
        {
            return;
        }

        _dragMode = hit.Part == HitPart.ResizeHandle ? DragMode.Resize : DragMode.Move;
        _dragElementId = hit.ElementId;
        _dragStartWorld = world;
        _originX = layout.X;
        _originY = layout.Y;
        _originWidth = layout.Width;
        _originHeight = layout.Height;
        _dragSnapshot = editor.CreateSnapshot();
        _dragMoved = false;
        e.Pointer.Capture(this);
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (_vm is null)
        {
            return;
        }

        var position = e.GetPosition(this);
        var world = ToWorld(position);
        var dx = world.X - _dragStartWorld.X;
        var dy = world.Y - _dragStartWorld.Y;

        switch (_dragMode)
        {
            case DragMode.Move:
                if (!_dragMoved && Math.Abs(dx) + Math.Abs(dy) < 3 / View.Zoom)
                {
                    return;
                }

                _vm.Editor.MoveElementTo(_dragElementId!, _originX + dx, _originY + dy);
                _dragMoved = true;
                Cursor = MoveCursor;
                InvalidateVisual();
                break;

            case DragMode.Resize:
                _vm.Editor.ResizeElement(_dragElementId!, _originWidth + dx, _originHeight + dy);
                _dragMoved = true;
                InvalidateVisual();
                break;

            case DragMode.Pan:
                View.OffsetX = _originX + (position.X - _dragStartScreen.X);
                View.OffsetY = _originY + (position.Y - _dragStartScreen.Y);
                InvalidateVisual();
                break;

            case DragMode.Connector:
                _connectorEnd = world;
                var target = _vm.Renderer.HitTest(_vm.Document, world);
                _connectorTargetId = IsConnectorTarget(target) ? target.ElementId : null;
                InvalidateVisual();
                break;

            default:
                UpdateHoverCursor(world);
                break;
        }
    }

    /// <summary>
    /// Where a connector drag can start or end: a class or lifeline for relationships and messages; a note or any UML
    /// element (including relationship and message lines) for note attachments.
    /// </summary>
    private bool IsConnectorEnd([System.Diagnostics.CodeAnalysis.NotNullWhen(true)] HitInfo? hit)
    {
        if (hit is null)
        {
            return false;
        }

        if (_vm!.ActiveTool.Kind != ToolKind.NoteAttachment)
        {
            return hit.IsNode;
        }

        var model = _vm.Document.Model;
        return hit.Part != HitPart.ResizeHandle &&
               (model.FindNote(hit.ElementId) is not null || model.FindUmlElementById(hit.ElementId) is not null);
    }

    private bool IsConnectorTarget([System.Diagnostics.CodeAnalysis.NotNullWhen(true)] HitInfo? hit)
    {
        if (!IsConnectorEnd(hit))
        {
            return false;
        }

        var tool = _vm!.ActiveTool;

        // Graph edges follow the diagram's rules; a transition may lead back to its own state.
        if (tool is { Kind: ToolKind.GraphEdge, EdgeKind: { } edgeKind })
        {
            return _vm.Editor.CheckEdge(edgeKind, _dragElementId!, hit.ElementId) is null;
        }

        return hit.ElementId != _dragElementId &&
               (tool.Kind != ToolKind.NoteAttachment || _vm.Editor.CanAttachNote(_dragElementId!, hit.ElementId));
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (_vm is not null && _dragMode == DragMode.Connector)
        {
            var target = _vm.Renderer.HitTest(_vm.Document, ToWorld(e.GetPosition(this)));
            if (IsConnectorTarget(target))
            {
                _vm.CreateConnector(_vm.ActiveTool, _dragElementId!, target.ElementId, _dragStartWorld.Y);
            }
        }

        EndDrag();
        e.Pointer.Capture(null);
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        EndDrag();
    }

    private void StartPan(Point position, PointerPressedEventArgs e)
    {
        _dragMode = DragMode.Pan;
        _dragStartScreen = position;
        _originX = View.OffsetX;
        _originY = View.OffsetY;
        Cursor = PanCursor;
        e.Pointer.Capture(this);
    }

    /// <summary>Finishes a drag; a move or resize becomes one undo step.</summary>
    private void EndDrag()
    {
        if (_vm is not null && _dragMode is DragMode.Move or DragMode.Resize && _dragMoved && _dragSnapshot is not null)
        {
            _vm.Editor.PushUndo(_dragSnapshot);
            _vm.Editor.NotifyChanged();
        }

        _dragMode = DragMode.None;
        _dragElementId = null;
        _dragSnapshot = null;
        _dragMoved = false;
        _connectorTargetId = null;
        Cursor = DefaultCursor;
        InvalidateVisual();
    }

    private void UpdateHoverCursor(SKPoint world)
    {
        if (_vm!.ActiveTool.IsConnectorTool)
        {
            Cursor = ConnectCursor;
            return;
        }

        if (_vm.ActiveTool.IsNodeTool)
        {
            Cursor = ConnectCursor;
            return;
        }

        var hit = _vm.Renderer.HitTest(_vm.Document, world, _vm.Editor.SelectedId);
        Cursor = hit?.Part == HitPart.ResizeHandle ? ResizeCursor : DefaultCursor;
    }

    #endregion

    #region Context menu and keyboard

    private void ShowContextMenu(HitInfo? hit, SKPoint world)
    {
        var vm = _vm!;
        var menu = new ContextMenu();
        if (hit is not null)
        {
            menu.Items.Add(CreateMenuItem(Strings.Context_Cut, Shortcuts.Cut, MaterialIconKind.ContentCut, () => vm.CutCommand.Execute(null)));
            menu.Items.Add(CreateMenuItem(Strings.Context_Copy, Shortcuts.Copy, MaterialIconKind.ContentCopy, () => vm.CopyCommand.Execute(null)));
            menu.Items.Add(new Separator());
            menu.Items.Add(CreateMenuItem(Strings.Context_Edit, new KeyGesture(Key.Enter), MaterialIconKind.PencilOutline, () => vm.EditCommand.Execute(null)));
            menu.Items.Add(CreateMenuItem(Strings.Context_Font, null, MaterialIconKind.FormatFont, () => vm.EditFontCommand.Execute(null)));
            if (vm.CanFormatShape)
            {
                menu.Items.Add(CreateMenuItem(Strings.Context_Shape, null, MaterialIconKind.FormatColorFill, () => vm.FormatShapeCommand.Execute(null)));
            }
            else if (vm.CanFormatLine)
            {
                menu.Items.Add(CreateMenuItem(Strings.Context_Line, null, MaterialIconKind.FormatLineWeight, () => vm.FormatLineCommand.Execute(null)));
            }
            if (vm.Editor.HasStackingOrder(hit.ElementId))
            {
                menu.Items.Add(new Separator());
                AddCommand(Strings.Context_BringToFront, Shortcuts.BringToFront, MaterialIconKind.ArrangeBringToFront, vm.BringToFrontCommand);
                AddCommand(Strings.Context_BringForward, Shortcuts.BringForward, MaterialIconKind.ArrangeBringForward, vm.BringForwardCommand);
                AddCommand(Strings.Context_SendBackward, Shortcuts.SendBackward, MaterialIconKind.ArrangeSendBackward, vm.SendBackwardCommand);
                AddCommand(Strings.Context_SendToBack, Shortcuts.SendToBack, MaterialIconKind.ArrangeSendToBack, vm.SendToBackCommand);
            }
        }
        else
        {
            var paste = CreateMenuItem(Strings.Context_Paste, Shortcuts.Paste, MaterialIconKind.ContentPaste, () => _ = vm.PasteAtAsync(world));
            paste.IsEnabled = vm.CanPaste;
            menu.Items.Add(paste);
            menu.Items.Add(new Separator());
            menu.Items.Add(CreateMenuItem(Strings.Context_DiagramFont, null, MaterialIconKind.FormatText, () => vm.EditDiagramFontCommand.Execute(null)));
            var arrange = CreateMenuItem(Strings.Context_Arrange, null, MaterialIconKind.SitemapOutline, () => vm.ArrangeCommand.Execute(null));
            arrange.IsEnabled = vm.ArrangeCommand.CanExecute(null);
            menu.Items.Add(arrange);
        }

        LastContextMenu = menu;
        menu.Open(this);

        void AddCommand(string header, KeyGesture gesture, MaterialIconKind icon, System.Windows.Input.ICommand command)
        {
            var item = CreateMenuItem(header, gesture, icon, () => command.Execute(null));
            item.IsEnabled = command.CanExecute(null);
            menu.Items.Add(item);
        }
    }

    /// <summary>The most recently opened context menu (for UI tests).</summary>
    internal ContextMenu? LastContextMenu { get; private set; }

    private static MenuItem CreateMenuItem(string header, KeyGesture? gesture, MaterialIconKind icon, Action action)
    {
        var item = new MenuItem
        {
            Header = header,
            InputGesture = gesture,
            Icon = new MaterialIcon { Kind = icon },
        };
        item.Click += (_, _) => action();
        return item;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (_vm is null || e.Handled || _session is not null)
        {
            return;
        }

        // "control" is the platform command key: ⌘ on macOS, Ctrl elsewhere.
        var control = Shortcuts.IsCommand(e.KeyModifiers);
        var shift = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
        if (!IsArrowKey(e.Key))
        {
            EndNudge();
        }

        switch (e.Key)
        {
            case Key.Left or Key.Right or Key.Up or Key.Down when !control && _dragMode == DragMode.None && NudgeSelection(e.Key, shift):
                break;
            case Key.Delete:
            case Key.Back when OperatingSystem.IsMacOS():
                _vm.DeleteCommand.Execute(null);
                break;
            case Key.F2:
                BeginInlineEditOfSelection();
                break;
            case Key.Enter:
                _vm.EditCommand.Execute(null);
                break;
            case Key.Escape:
                _vm.ResetTool();
                _vm.Editor.SelectedId = null;
                break;
            case Key.X when control:
                _vm.CutCommand.Execute(null);
                break;
            case Key.C when control:
                _vm.CopyCommand.Execute(null);
                break;
            case Key.V when control:
                _vm.PasteCommand.Execute(null);
                break;
            case Key.Z when control && shift:
            case Key.Y when control:
                _vm.RedoCommand.Execute(null);
                break;
            case Key.Z when control:
                _vm.UndoCommand.Execute(null);
                break;
            case Key.OemPlus or Key.Add when control:
                _vm.ZoomInCommand.Execute(null);
                break;
            case Key.OemMinus or Key.Subtract when control:
                _vm.ZoomOutCommand.Execute(null);
                break;
            case Key.D0 or Key.NumPad0 when control:
                _vm.ZoomResetCommand.Execute(null);
                break;
            case Key.OemCloseBrackets when control && shift:
                _vm.BringToFrontCommand.Execute(null);
                break;
            case Key.OemCloseBrackets when control:
                _vm.BringForwardCommand.Execute(null);
                break;
            case Key.OemOpenBrackets when control && shift:
                _vm.SendToBackCommand.Execute(null);
                break;
            case Key.OemOpenBrackets when control:
                _vm.SendBackwardCommand.Execute(null);
                break;
            default:
                return;
        }

        e.Handled = true;
    }

    protected override void OnKeyUp(KeyEventArgs e)
    {
        base.OnKeyUp(e);
        if (IsArrowKey(e.Key))
        {
            EndNudge();
        }
    }

    protected override void OnLostFocus(Avalonia.Interactivity.RoutedEventArgs e)
    {
        base.OnLostFocus(e);
        EndNudge();
    }

    private static bool IsArrowKey(Key key) => key is Key.Left or Key.Right or Key.Up or Key.Down;

    /// <summary>
    /// Moves the selected element one unit in the direction of the arrow key, or one grid step with Shift. False when
    /// nothing movable is selected. Held-down keys repeat, and the whole move is one undo step (see <see cref="EndNudge"/>).
    /// </summary>
    private bool NudgeSelection(Key key, bool gridStep)
    {
        if (_vm!.Editor.SelectedId is not { } id)
        {
            return false;
        }

        double step = gridStep ? GridSize : 1;
        var (dx, dy) = key switch
        {
            Key.Left => (-step, 0.0),
            Key.Right => (step, 0.0),
            Key.Up => (0.0, -step),
            _ => (0.0, step),
        };
        var snapshot = _nudgeSnapshot ?? _vm.Editor.CreateSnapshot();
        if (!_vm.Editor.MoveElementBy(id, dx, dy))
        {
            return false;
        }

        _nudgeSnapshot = snapshot;
        InvalidateVisual();
        return true;
    }

    /// <summary>Records the keyboard move in progress as one undo step, unless it did not change anything.</summary>
    private void EndNudge()
    {
        if (_nudgeSnapshot is not { } snapshot || _vm is null)
        {
            _nudgeSnapshot = null;
            return;
        }

        _nudgeSnapshot = null;
        if (snapshot != _vm.Editor.CreateSnapshot())
        {
            _vm.Editor.PushUndo(snapshot);
            _vm.Editor.NotifyChanged();
        }
    }

    #endregion

    #region In-line editing

    private void BeginInlineEditOfSelection()
    {
        var part = _vm!.Editor.SelectedElement switch
        {
            UmlClass => HitPart.ClassHeader,
            UmlRelationship => HitPart.Relationship,
            Lifeline => HitPart.LifelineHead,
            SequenceMessage => HitPart.Message,
            TextElement => HitPart.Text,
            GraphNode { Kind: GraphNodeKind.SystemBoundary } => HitPart.Boundary,
            GraphNode node when GraphRules.HasName(node.Kind) => HitPart.Node,
            GraphEdge => HitPart.Edge,
            _ => (HitPart?)null,
        };
        if (part is { } p)
        {
            BeginInlineEdit(new HitInfo(_vm.Editor.SelectedId!, p));
        }
    }

    private void BeginInlineEdit(HitInfo hit)
    {
        if (_vm is null || hit.Part == HitPart.ResizeHandle)
        {
            return;
        }

        var session = _vm.Editor.BeginInlineEdit(hit);
        if (session is null)
        {
            return;
        }

        _session = session;
        _vm.Editor.SelectedId = session.ElementId;
        _vm.Renderer.UpdateLayout(_vm.Document);

        var rect = _vm.Renderer.GetEditRect(_vm.Document, session.ElementId, session.Part, session.Index);
        if (rect is not { } r)
        {
            CancelInlineEdit();
            return;
        }

        var topLeft = ToScreen(r.Left, r.Top);
        var zoom = View.Zoom;
        _inlineEditorRect = new Rect(topLeft.X - 2, topLeft.Y - 3, Math.Max(r.Width * zoom + 4, 80), r.Height * zoom + 6);
        _inlineEditor.Width = _inlineEditorRect.Width;
        _inlineEditor.Height = _inlineEditorRect.Height;
        ApplyFont(_inlineEditor, _vm.Document.Layout.FontFor(session.ElementId), zoom);

        // Notes and text boxes take several lines and keep their alignment while being edited.
        var textBox = _vm.Document.Model.FindTextElement(session.ElementId);
        _inlineEditor.AcceptsReturn = textBox is not null;
        _inlineEditor.TextWrapping = textBox is not null ? TextWrapping.Wrap : TextWrapping.NoWrap;
        _inlineEditor.TextAlignment = textBox?.HorizontalAlignment switch
        {
            HorizontalTextAlignment.Center => TextAlignment.Center,
            HorizontalTextAlignment.Right => TextAlignment.Right,
            _ => TextAlignment.Left,
        };
        _inlineEditor.VerticalContentAlignment = textBox is not null ? Avalonia.Layout.VerticalAlignment.Top : Avalonia.Layout.VerticalAlignment.Center;
        InvalidateMeasure();
        InvalidateArrange();
        _inlineEditor.Text = session.InitialText;
        _inlineEditor.IsVisible = true;
        InvalidateVisual();

        Dispatcher.UIThread.Post(() =>
        {
            if (_session == session)
            {
                _inlineEditor.Focus();
                _inlineEditor.SelectAll();
            }
        }, DispatcherPriority.Loaded);
    }

    /// <param name="continueEntry">
    /// When a new class member was added, immediately start entering the next one (Enter key).
    /// </param>
    private void CommitInlineEdit(bool continueEntry = false)
    {
        if (_session is not { } session)
        {
            return;
        }

        _session = null;
        var text = _inlineEditor.Text ?? string.Empty;
        HideInlineEditor();
        session.Commit(text);
        InvalidateVisual();

        if (continueEntry && session.IsNew && text.Trim().Length > 0)
        {
            var area = session.Part == HitPart.ClassAttribute ? HitPart.ClassAttributesArea : HitPart.ClassOperationsArea;
            BeginInlineEdit(new HitInfo(session.ElementId, area));
        }
    }

    private void CancelInlineEdit()
    {
        if (_session is not { } session)
        {
            return;
        }

        _session = null;
        HideInlineEditor();
        session.Cancel();
        InvalidateVisual();
    }

    /// <summary>Makes the in-line editor match the font the element is drawn with.</summary>
    private static void ApplyFont(TextBox editor, DiagramFont? font, double zoom)
    {
        var size = Math.Clamp(font?.Size ?? DiagramFont.DefaultSize, DiagramFont.MinSize, DiagramFont.MaxSize);
        editor.FontSize = size * zoom;
        editor.FontFamily = new FontFamily(string.IsNullOrWhiteSpace(font?.Family) ? RenderTheme.DefaultFontFamily : font.Family);
        editor.FontWeight = font?.Bold == true ? FontWeight.Bold : FontWeight.Normal;
        editor.FontStyle = font?.Italic == true ? FontStyle.Italic : FontStyle.Normal;
    }

    private void HideInlineEditor()
    {
        var hadFocus = _inlineEditor.IsFocused;
        _inlineEditor.IsVisible = false;
        if (hadFocus)
        {
            Focus();
        }
    }

    private void OnInlineEditorKeyDown(object? sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            // In a text box Enter starts a new line; Ctrl+Enter (⌘+Enter on macOS) finishes.
            case Key.Enter when _inlineEditor.AcceptsReturn && !Shortcuts.IsCommand(e.KeyModifiers):
                break;
            case Key.Enter:
                CommitInlineEdit(continueEntry: true);
                e.Handled = true;
                break;
            case Key.Escape:
                CancelInlineEdit();
                e.Handled = true;
                break;
        }
    }

    private bool IsInsideInlineEditor(object? source) =>
        source is Visual visual && (visual == _inlineEditor || _inlineEditor.IsVisualAncestorOf(visual));

    #endregion
}
