using Avalonia;
using Avalonia.Styling;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Material.Icons;
using SkiaSharp;
using UmlWizard.App.Resources;
using UmlWizard.App.Services;
using UmlWizard.App.ViewModels.Dialogs;
using UmlWizard.Core.Editing;
using UmlWizard.Core.Model;
using UmlWizard.Core.Notation;
using UmlWizard.Core.Rendering;

namespace UmlWizard.App.ViewModels;

/// <summary>An open diagram: its editor, renderer, active tool and edit commands.</summary>
public sealed partial class DiagramViewModel : ObservableObject, IDisposable
{
    private readonly IDialogService _dialogs;
    private readonly IClipboardService _clipboard;
    private readonly Func<DiagramViewModel, Task> _close;

    public DiagramViewModel(
        DiagramDocument document,
        IDialogService dialogs,
        IClipboardService clipboard,
        Func<DiagramViewModel, Task> close)
    {
        _dialogs = dialogs;
        _clipboard = clipboard;
        _close = close;
        Editor = new DiagramEditor(document);
        Renderer = DiagramRenderer.Create(document.DiagramType);
        Tools = ToolItem.For(document.DiagramType);
        ActiveTool = Tools[0];
        ZoomText = FormatZoom(document.Layout.View.Zoom);

        Editor.Changed += (_, _) =>
        {
            OnPropertyChanged(nameof(Title));
            OnPropertyChanged(nameof(IsDirty));
            OnPropertyChanged(nameof(StatusText));
            OnSelectedElementChanged();
            OnHistoryChanged();
        };
        Editor.SelectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(StatusText));
            OnSelectedElementChanged();
        };
    }

    /// <summary>Raised when a zoom command needs the canvas (which knows the viewport size).</summary>
    public event EventHandler<ZoomAction>? ZoomRequested;

    /// <summary>The AI chat about this diagram (View → AI Chat); set by the main window.</summary>
    public AiChatViewModel? Chat { get; set; }

    /// <summary>Raised when the diagram must be drawn again although it did not change, e.g. when <see cref="Defaults"/> do.</summary>
    public event EventHandler? RedrawRequested;

    /// <summary>The user's preferred look of the shapes of this diagram type (Preferences → Diagram); <c>null</c> keeps the theme's.</summary>
    public DiagramStyle? Defaults
    {
        get => Renderer.Defaults;
        set
        {
            Renderer.Defaults = value;
            RedrawRequested?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>Id of the diagram in the project; the editor works on a copy until the diagram is saved.</summary>
    public string Id => Document.Id;

    public DiagramEditor Editor { get; }

    public DiagramRenderer Renderer { get; }

    public DiagramDocument Document => Editor.Document;

    public DiagramType DiagramType => Document.DiagramType;

    public string Name => Document.Name;

    public bool IsDirty => Editor.IsDirty;

    public string Title => IsDirty ? $"{Name} *" : Name;

    public MaterialIconKind TypeIcon => DiagramItemViewModel.IconFor(DiagramType);

    public IReadOnlyList<ToolItem> Tools { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    public partial ToolItem ActiveTool { get; set; }

    [ObservableProperty]
    public partial string ZoomText { get; set; }

    public string StatusText
    {
        get
        {
            if (ActiveTool.IsNodeTool)
            {
                return Loc.Format(Strings.Status_PlaceTool, ActiveTool.Label);
            }

            if (ActiveTool.IsConnectorTool)
            {
                return Loc.Format(Strings.Status_ConnectorTool, ActiveTool.ToolTip);
            }

            return Editor.SelectedElement is { } element
                ? Loc.Format(Strings.Status_Selected, Describe(element))
                : Loc.Format(Strings.Status_Idle, Shortcuts.CommandKeyName);
        }
    }

    public void ResetTool() => ActiveTool = Tools[0];

    /// <summary>The smallest zoom, 2%: far enough out to see the whole of a diagram of hundreds of classes.</summary>
    public const double MinZoom = 0.02;

    public const double MaxZoom = 4.0;

    public void RefreshZoom()
    {
        ZoomText = FormatZoom(Document.Layout.View.Zoom);
        ZoomInCommand.NotifyCanExecuteChanged();
        ZoomOutCommand.NotifyCanExecuteChanged();
        ZoomResetCommand.NotifyCanExecuteChanged();
    }

    public void Rename(string name)
    {
        Editor.Execute(() => Document.Name = name);
        OnPropertyChanged(nameof(Name));
        OnPropertyChanged(nameof(Title));
    }

    /// <summary>
    /// Applies name, description, author and tagged values that were already saved to the project file, as an
    /// undoable step. An editor without other unsaved changes stays clean, since the project already holds these values.
    /// </summary>
    public void ApplyProperties(string name, string? description, string? author, IEnumerable<TaggedValue> taggedValues)
    {
        var wasDirty = Editor.IsDirty;
        Editor.Execute(() =>
        {
            Document.Name = name;
            Document.Description = description;
            Document.Author = author;
            Document.TaggedValues = taggedValues.Copy();
        });
        if (!wasDirty)
        {
            Editor.MarkSaved();
        }

        OnPropertyChanged(nameof(Name));
        OnPropertyChanged(nameof(Title));
    }

    public void CreateNode(ToolItem item, SKPoint world)
    {
        switch (item.Kind)
        {
            case ToolKind.GraphNode when item.NodeKind is { } nodeKind: Editor.AddNode(nodeKind, world.X, world.Y); break;
            case ToolKind.Class: Editor.AddClass(ClassifierKind.Class, world.X, world.Y); break;
            case ToolKind.AbstractClass: Editor.AddClass(ClassifierKind.AbstractClass, world.X, world.Y); break;
            case ToolKind.Interface: Editor.AddClass(ClassifierKind.Interface, world.X, world.Y); break;
            case ToolKind.Enumeration: Editor.AddClass(ClassifierKind.Enumeration, world.X, world.Y); break;
            // New lifelines are 120 wide; centre them on the click.
            case ToolKind.Lifeline: Editor.AddLifeline(LifelineKind.Object, world.X - 60); break;
            case ToolKind.Actor: Editor.AddLifeline(LifelineKind.Actor, world.X - 60); break;
            case ToolKind.TextBox: Editor.AddTextBox(world.X, world.Y); break;
            case ToolKind.Note: Editor.AddNote(world.X, world.Y); break;
        }

        ResetTool();
    }

    public void CreateConnector(ToolItem item, string sourceId, string targetId, float y)
    {
        var tool = item.Kind;
        if (tool == ToolKind.NoteAttachment)
        {
            Editor.AttachNote(sourceId, targetId);
            ResetTool();
            return;
        }

        if (tool == ToolKind.GraphEdge && item.EdgeKind is { } edgeKind)
        {
            Editor.AddEdge(edgeKind, sourceId, targetId);
            ResetTool();
            return;
        }

        RelationshipKind? relationshipKind = tool switch
        {
            ToolKind.Association => RelationshipKind.Association,
            ToolKind.DirectedAssociation => RelationshipKind.DirectedAssociation,
            ToolKind.Inheritance => RelationshipKind.Inheritance,
            ToolKind.Realization => RelationshipKind.Realization,
            ToolKind.Aggregation => RelationshipKind.Aggregation,
            ToolKind.Composition => RelationshipKind.Composition,
            ToolKind.Dependency => RelationshipKind.Dependency,
            _ => null,
        };
        MessageKind? messageKind = tool switch
        {
            ToolKind.SyncMessage => MessageKind.Synchronous,
            ToolKind.AsyncMessage => MessageKind.Asynchronous,
            ToolKind.ReplyMessage => MessageKind.Reply,
            ToolKind.CreateMessage => MessageKind.Create,
            _ => null,
        };

        if (relationshipKind is { } rk)
        {
            Editor.AddRelationship(rk, sourceId, targetId);
        }
        else if (messageKind is { } mk)
        {
            Editor.AddMessage(mk, sourceId, targetId, y);
        }

        ResetTool();
    }

    /// <summary>True when the clipboard holds an element that can be pasted into this diagram; see <see cref="RefreshCanPasteAsync"/>.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(PasteCommand))]
    public partial bool CanPaste { get; set; }

    /// <summary>True when an element is selected (cut, copy, delete, edit and font apply to it).</summary>
    public bool HasSelection => Editor.SelectedElement is not null;

    public bool CanUndo => Editor.CanUndo;

    public bool CanRedo => Editor.CanRedo;

    public bool CanZoomIn => Document.Layout.View.Zoom < MaxZoom;

    public bool CanZoomOut => Document.Layout.View.Zoom > MinZoom;

    public bool CanZoomReset => Document.Layout.View.Zoom != 1;

    [RelayCommand(CanExecute = nameof(CanUndo))]
    private void Undo() => Editor.Undo();

    [RelayCommand(CanExecute = nameof(CanRedo))]
    private void Redo() => Editor.Redo();

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private async Task Cut()
    {
        if (Editor.SelectedId is { } id && Editor.Cut(id) is { } text)
        {
            await _clipboard.SetTextAsync(text);
            CanPaste = true;
        }
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private async Task Copy()
    {
        if (Editor.SelectedId is { } id && Editor.Copy(id) is { } text)
        {
            await _clipboard.SetTextAsync(text);
            CanPaste = true;
        }
    }

    [RelayCommand(CanExecute = nameof(CanPaste))]
    private Task Paste() => PasteAtAsync(null);

    /// <summary>Pastes the clipboard element, at <paramref name="world"/> when given.</summary>
    public async Task PasteAtAsync(SKPoint? world)
    {
        var text = await _clipboard.GetTextAsync();
        Editor.Paste(text, world?.X, world?.Y);
    }

    /// <summary>Re-reads the clipboard, which other diagrams and applications may have changed, to update <see cref="CanPaste"/>.</summary>
    public async Task RefreshCanPasteAsync()
    {
        try
        {
            CanPaste = Editor.CanPaste(await _clipboard.GetTextAsync());
        }
        catch (Exception)
        {
            // The system clipboard can be briefly locked by another application; keep the current state.
        }
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void Delete()
    {
        if (Editor.SelectedId is { } id)
        {
            Editor.Delete(id);
        }
    }

    private bool CanBringToFront => CanChangeZOrder(ZOrderChange.BringToFront);

    private bool CanBringForward => CanChangeZOrder(ZOrderChange.BringForward);

    private bool CanSendBackward => CanChangeZOrder(ZOrderChange.SendBackward);

    private bool CanSendToBack => CanChangeZOrder(ZOrderChange.SendToBack);

    private bool CanChangeZOrder(ZOrderChange change) => Editor.SelectedId is { } id && Editor.CanChangeZOrder(id, change);

    [RelayCommand(CanExecute = nameof(CanBringToFront))]
    private void BringToFront() => ChangeZOrder(ZOrderChange.BringToFront);

    [RelayCommand(CanExecute = nameof(CanBringForward))]
    private void BringForward() => ChangeZOrder(ZOrderChange.BringForward);

    [RelayCommand(CanExecute = nameof(CanSendBackward))]
    private void SendBackward() => ChangeZOrder(ZOrderChange.SendBackward);

    [RelayCommand(CanExecute = nameof(CanSendToBack))]
    private void SendToBack() => ChangeZOrder(ZOrderChange.SendToBack);

    private void ChangeZOrder(ZOrderChange change)
    {
        if (Editor.SelectedId is { } id)
        {
            Editor.ChangeZOrder(id, change);
        }
    }

    private bool CanArrange => Editor.CanArrange;

    /// <summary>
    /// Runs a long task while the window shows the text, how far it is and Cancel, which cancels the token; set by the
    /// main window. Without one, the task just runs.
    /// </summary>
    public Func<string, Func<CancellationToken, Task>, Task> RunBusy { get; set; } = (_, work) => work(CancellationToken.None);

    /// <summary>
    /// Rearranges the whole diagram; undoable. Large diagrams take a while, so the layout is worked out on another thread
    /// while the window shows that it is arranging; Cancel leaves the diagram as it was. Every element moves, so the view
    /// then fits the arranged diagram, rather than staying on a place that may now be empty.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanArrange))]
    private async Task Arrange()
    {
        var arranged = false;
        try
        {
            await RunBusy(Loc.Format(Strings.Message_Arranging, Name), async token => arranged = await Editor.ArrangeAsync(token));
        }
        catch (OperationCanceledException)
        {
        }

        if (arranged)
        {
            ZoomFit();
        }
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private async Task Edit()
    {
        if (Editor.SelectedId is { } id)
        {
            await EditElementAsync(id);
        }
    }

    /// <summary>True when an element is selected whose font can be edited.</summary>
    public bool CanEditFont => Editor.SelectedElement is not (null or NoteAttachment);

    /// <summary>Font of the selected element.</summary>
    [RelayCommand(CanExecute = nameof(CanEditFont))]
    private Task EditFont() => Editor.SelectedId is { } id ? EditFontAsync(id) : Task.CompletedTask;

    [RelayCommand]
    private Task EditDiagramFont() => EditFontAsync(null);

    /// <summary>Opens the font dialog of an element, or of the whole diagram when <paramref name="id"/> is <c>null</c>.</summary>
    public async Task EditFontAsync(string? id)
    {
        FontEditorViewModel dialog;
        if (id is null)
        {
            dialog = new FontEditorViewModel(Strings.Dialog_Font_DiagramTitle, Document.Layout.Font, null, Strings.Dialog_Font_UseDefaultFont);
        }
        else if (Document.Model.FindElement(id) is { } element)
        {
            dialog = new FontEditorViewModel(
                Loc.Format(Strings.Dialog_Font_ElementTitle, Describe(element)), Document.Layout.Find(id)?.Font, Document.Layout.Font,
                Strings.Dialog_Font_UseDiagramFont);
        }
        else
        {
            return;
        }

        if (await _dialogs.ShowDialogAsync(dialog))
        {
            Editor.SetFont(id, dialog.ToFont());
        }
    }

    /// <summary>True when the selected element is drawn as a shape, whose fill and border can be changed.</summary>
    public bool CanFormatShape => DiagramEditor.HasShape(Editor.SelectedElement);

    /// <summary>Fill and border of the selected element (Format → Shape).</summary>
    [RelayCommand(CanExecute = nameof(CanFormatShape))]
    private Task FormatShape() => Editor.SelectedId is { } id ? FormatShapeAsync(id) : Task.CompletedTask;

    /// <summary>Opens the Format Shape dialog of an element and applies the result as one undoable change.</summary>
    public async Task FormatShapeAsync(string id)
    {
        if (Document.Model.FindElement(id) is not { } element || !DiagramEditor.HasShape(element))
        {
            return;
        }

        // "Default" is what the element has without a style of its own: the preferences for its diagram type, else the theme's.
        var theme = CurrentTheme;
        var preferred = Renderer.Defaults?.Apply(element, null);
        var dialog = new ShapeEditorViewModel(
            Loc.Format(Strings.Dialog_Shape_Title, Describe(element)), Document.Layout.Find(id)?.Style,
            ShapeColors.Parse(preferred?.Fill) ?? theme.DefaultFill(element),
            theme.DefaultBorder(element) is null ? null : ShapeColors.Parse(preferred?.Border) ?? theme.DefaultBorder(element),
            theme, preferred?.BorderWidth)
        {
            Dialogs = _dialogs,
        };
        if (await _dialogs.ShowDialogAsync(dialog))
        {
            Editor.SetShapeStyle(id, dialog.ToStyle());
        }
    }

    /// <summary>True when the selected element is a connector, whose line colour and thickness can be changed.</summary>
    public bool CanFormatLine => DiagramEditor.HasLine(Editor.SelectedElement);

    /// <summary>Colour and thickness of the selected connector (Format → Line).</summary>
    [RelayCommand(CanExecute = nameof(CanFormatLine))]
    private Task FormatLine() => Editor.SelectedId is { } id ? FormatLineAsync(id) : Task.CompletedTask;

    /// <summary>Opens the Format Line dialog of a connector and applies the result as one undoable change.</summary>
    public async Task FormatLineAsync(string id)
    {
        if (Document.Model.FindElement(id) is not { } element || !DiagramEditor.HasLine(element))
        {
            return;
        }

        var dialog = ShapeEditorViewModel.ForLine(
            Loc.Format(Strings.Dialog_Line_Title, Describe(element)), Document.Layout.Find(id)?.Line, CurrentTheme, _dialogs);
        if (await _dialogs.ShowDialogAsync(dialog))
        {
            Editor.SetLineStyle(id, dialog.ToLineStyle());
        }
    }

    /// <summary>The theme the diagram is displayed in; the format dialogs show its defaults.</summary>
    private static RenderTheme CurrentTheme =>
        Application.Current?.ActualThemeVariant == ThemeVariant.Dark ? RenderTheme.Dark : RenderTheme.Light;

    /// <summary>Opens the property dialog of an element and applies the result as one undoable change.</summary>
    public async Task EditElementAsync(string id)
    {
        switch (Document.Model.FindElement(id))
        {
            case UmlClass cls:
            {
                var dialog = new ClassEditorViewModel(cls);
                if (await _dialogs.ShowDialogAsync(dialog))
                {
                    Editor.Execute(() => dialog.ApplyTo(cls));
                }

                break;
            }

            case UmlRelationship relationship:
            {
                var model = (ClassDiagramModel)Document.Model;
                var dialog = new RelationshipEditorViewModel(
                    relationship,
                    model.FindClass(relationship.SourceId)?.Name ?? "?",
                    model.FindClass(relationship.TargetId)?.Name ?? "?");
                if (await _dialogs.ShowDialogAsync(dialog))
                {
                    Editor.Execute(() => dialog.ApplyTo(relationship));
                }

                break;
            }

            case Lifeline lifeline:
            {
                var dialog = new LifelineEditorViewModel(lifeline);
                if (await _dialogs.ShowDialogAsync(dialog))
                {
                    Editor.Execute(() => dialog.ApplyTo(lifeline));
                }

                break;
            }

            case SequenceMessage message:
            {
                var options = ((SequenceDiagramModel)Document.Model).Lifelines
                    .Select(l => new LifelineOption(l.Id, UmlNotation.FormatLifeline(l)))
                    .ToList();
                var dialog = new MessageEditorViewModel(message, options);
                if (await _dialogs.ShowDialogAsync(dialog))
                {
                    Editor.Execute(() => dialog.ApplyTo(message));
                }

                break;
            }

            case GraphNode node when GraphRules.HasName(node.Kind):
            {
                var dialog = new TextInputViewModel(
                    Loc.Format(Strings.Dialog_RenameElement_Title, Describe(node)), Strings.Label_Name, node.Name ?? string.Empty);
                if (await _dialogs.ShowDialogAsync(dialog))
                {
                    Editor.Execute(() => node.Name = dialog.Text.Trim());
                }

                break;
            }

            case GraphEdge edge:
            {
                var prompt = edge.Kind switch
                {
                    GraphEdgeKind.Transition => Strings.Dialog_EditEdge_TransitionLabel,
                    GraphEdgeKind.ControlFlow => Strings.Dialog_EditEdge_GuardLabel,
                    _ => Strings.Label_Label,
                };
                var dialog = new TextInputViewModel(
                    Loc.Format(Strings.Dialog_EditEdge_Title, Loc.Of(edge.Kind)), prompt, edge.Label ?? string.Empty, allowEmpty: true);
                if (await _dialogs.ShowDialogAsync(dialog))
                {
                    Editor.Execute(() => edge.Label = string.IsNullOrWhiteSpace(dialog.Text) ? null : dialog.Text.Trim());
                }

                break;
            }

            case TextElement textBox:
            {
                var layout = Document.Layout.Find(id);
                var dialog = new TextBoxEditorViewModel(textBox, layout?.Font, Document.Layout.Font);
                if (await _dialogs.ShowDialogAsync(dialog))
                {
                    // Text, alignment and font change together, as one undo step.
                    Editor.Execute(() =>
                    {
                        dialog.ApplyTo(textBox);
                        if (Document.Layout.Find(id) is { } target)
                        {
                            target.Font = dialog.Font.ToFont();
                        }
                    });
                }

                break;
            }
        }
    }

    [RelayCommand(CanExecute = nameof(CanZoomIn))]
    private void ZoomIn() => ZoomRequested?.Invoke(this, ZoomAction.In);

    [RelayCommand(CanExecute = nameof(CanZoomOut))]
    private void ZoomOut() => ZoomRequested?.Invoke(this, ZoomAction.Out);

    [RelayCommand(CanExecute = nameof(CanZoomReset))]
    private void ZoomReset() => ZoomRequested?.Invoke(this, ZoomAction.Reset);

    [RelayCommand]
    private void ZoomFit() => ZoomRequested?.Invoke(this, ZoomAction.Fit);

    [RelayCommand]
    private Task Close() => _close(this);

    /// <summary>The selection changed, or the selected element may have been deleted (undo, cut, delete).</summary>
    private void OnSelectedElementChanged()
    {
        OnPropertyChanged(nameof(HasSelection));
        OnPropertyChanged(nameof(CanEditFont));
        OnPropertyChanged(nameof(CanFormatShape));
        OnPropertyChanged(nameof(CanFormatLine));
        CutCommand.NotifyCanExecuteChanged();
        CopyCommand.NotifyCanExecuteChanged();
        DeleteCommand.NotifyCanExecuteChanged();
        EditCommand.NotifyCanExecuteChanged();
        EditFontCommand.NotifyCanExecuteChanged();
        FormatShapeCommand.NotifyCanExecuteChanged();
        FormatLineCommand.NotifyCanExecuteChanged();
        BringToFrontCommand.NotifyCanExecuteChanged();
        BringForwardCommand.NotifyCanExecuteChanged();
        SendBackwardCommand.NotifyCanExecuteChanged();
        SendToBackCommand.NotifyCanExecuteChanged();
    }

    private void OnHistoryChanged()
    {
        OnPropertyChanged(nameof(CanUndo));
        OnPropertyChanged(nameof(CanRedo));
        UndoCommand.NotifyCanExecuteChanged();
        RedoCommand.NotifyCanExecuteChanged();
        ArrangeCommand.NotifyCanExecuteChanged();
    }

    public void Dispose() => Renderer.Dispose();

    private static string FormatZoom(double zoom) => $"{Math.Round(zoom * 100)}%";

    private string Describe(ModelElement element) => element switch
    {
        UmlClass cls => Loc.Format(Strings.Describe_Named, Loc.Of(cls.Kind), cls.Name),
        UmlRelationship r => Loc.Format(Strings.Describe_Connection, Loc.Of(r.Kind), NameOf(r.SourceId), NameOf(r.TargetId)),
        Lifeline l => Loc.Format(Strings.Describe_Lifeline, UmlNotation.FormatLifeline(l)),
        SequenceMessage m => Loc.Format(Strings.Describe_Message, Loc.Of(m.Kind), m.Text),
        TextBoxElement t => Loc.Format(Strings.Describe_TextBox, FirstLine(t.Text)),
        NoteElement n => Loc.Format(Strings.Describe_Note, FirstLine(n.Text)),
        NoteAttachment => Strings.Describe_NoteAttachment,
        GraphNode n => string.IsNullOrEmpty(n.Name) ? Loc.Of(n.Kind) : Loc.Format(Strings.Describe_Named, Loc.Of(n.Kind), n.Name),
        GraphEdge e => Loc.Format(Strings.Describe_Connection, Loc.Of(e.Kind), NameOf(e.SourceId), NameOf(e.TargetId)),
        _ => element.Id,
    };

    private static string FirstLine(string text)
    {
        var line = text.Split('\n')[0].Trim();
        return line.Length > 30 ? line[..30] + "…" : line;
    }

    private string NameOf(string id) => Document.Model.FindElement(id) switch
    {
        UmlClass cls => cls.Name,
        Lifeline l => l.Name,
        GraphNode n => n.Name ?? Loc.Of(n.Kind),
        _ => "?",
    };
}
