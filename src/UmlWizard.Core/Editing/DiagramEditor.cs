using UmlWizard.Core.Model;
using UmlWizard.Core.Notation;
using UmlWizard.Core.Rendering;
using UmlWizard.Core.Serialization;

namespace UmlWizard.Core.Editing;

/// <summary>
/// All editing operations on a diagram document, independent of any UI framework.
/// Undo/redo is snapshot based: every change records the serialized document state before it.
/// </summary>
public sealed partial class DiagramEditor
{
    private const int MaxUndoSteps = 200;

    private readonly List<string> _undo = [];
    private readonly List<string> _redo = [];
    private string? _selectedId;

    public DiagramEditor(DiagramDocument document) => Document = document;

    /// <summary>Raised after the model or layout changed (including undo/redo and save state).</summary>
    public event EventHandler? Changed;

    public event EventHandler? SelectionChanged;

    public DiagramDocument Document { get; }

    public DiagramModel Model => Document.Model;

    public DiagramLayout Layout => Document.Layout;

    public bool IsDirty { get; private set; }

    public bool CanUndo => _undo.Count > 0;

    public bool CanRedo => _redo.Count > 0;

    public string? SelectedId
    {
        get => _selectedId;
        set
        {
            if (_selectedId == value)
            {
                return;
            }

            _selectedId = value;
            SelectionChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public ModelElement? SelectedElement => SelectedId is null ? null : Model.FindElement(SelectedId);

    private ClassDiagramModel ClassModel =>
        Model as ClassDiagramModel ?? throw new InvalidOperationException("The document is not a class diagram.");

    private SequenceDiagramModel SequenceModel =>
        Model as SequenceDiagramModel ?? throw new InvalidOperationException("The document is not a sequence diagram.");

    #region Change tracking and undo

    public string CreateSnapshot() => DiagramSerializer.Serialize(Document);

    /// <summary>Records <paramref name="snapshot"/> (taken before a change) as an undo step.</summary>
    public void PushUndo(string snapshot)
    {
        _undo.Add(snapshot);
        if (_undo.Count > MaxUndoSteps)
        {
            _undo.RemoveAt(0);
        }

        _redo.Clear();
    }

    /// <summary>Runs <paramref name="mutation"/> as one undoable change.</summary>
    public void Execute(Action mutation)
    {
        var snapshot = CreateSnapshot();
        mutation();
        PushUndo(snapshot);
        NotifyChanged();
    }

    /// <summary>Counts the changes, so that work done meanwhile can tell whether the diagram was edited.</summary>
    private int _changes;

    public void NotifyChanged()
    {
        _changes++;
        IsDirty = true;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void MarkSaved()
    {
        IsDirty = false;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public bool Undo() => Step(_undo, _redo);

    public bool Redo() => Step(_redo, _undo);

    private bool Step(List<string> from, List<string> to)
    {
        if (from.Count == 0)
        {
            return false;
        }

        to.Add(CreateSnapshot());
        var snapshot = from[^1];
        from.RemoveAt(from.Count - 1);

        var restored = DiagramSerializer.Deserialize(snapshot);
        Document.Name = restored.Name;
        Document.Description = restored.Description;
        Document.Author = restored.Author;
        Document.TaggedValues = restored.TaggedValues;
        Document.Model = restored.Model;
        Document.Layout.Elements = restored.Layout.Elements;
        Document.Layout.Font = restored.Layout.Font;

        if (SelectedId is not null && Model.FindElement(SelectedId) is null)
        {
            SelectedId = null;
        }

        NotifyChanged();
        return true;
    }

    #endregion

    #region Class diagram

    public UmlClass AddClass(ClassifierKind kind, double x, double y)
    {
        var baseName = kind switch
        {
            ClassifierKind.Interface => "IInterface",
            ClassifierKind.AbstractClass => "AbstractClass",
            ClassifierKind.Enumeration => "Enumeration",
            _ => "Class",
        };
        var cls = new UmlClass
        {
            Kind = kind,
            Name = UniqueName(baseName, ClassModel.Classes.Select(c => c.Name), alwaysNumber: true),
        };
        Execute(() =>
        {
            ClassModel.Classes.Add(cls);
            Layout.Add(new ElementLayout { ElementId = cls.Id, X = x, Y = y, Width = 160 });
        });
        SelectedId = cls.Id;
        return cls;
    }

    public UmlRelationship? AddRelationship(RelationshipKind kind, string sourceId, string targetId)
    {
        if (sourceId == targetId || ClassModel.FindClass(sourceId) is null || ClassModel.FindClass(targetId) is null)
        {
            return null;
        }

        var relationship = new UmlRelationship { Kind = kind, SourceId = sourceId, TargetId = targetId };
        Execute(() => ClassModel.Relationships.Add(relationship));
        SelectedId = relationship.Id;
        return relationship;
    }

    #endregion

    #region Sequence diagram

    #region Notes and text boxes

    /// <summary>Adds a text box of the default size with its top-left corner at (<paramref name="x"/>, <paramref name="y"/>).</summary>
    public TextBoxElement AddTextBox(double x, double y) => AddTextElement(new TextBoxElement(), x, y);

    /// <summary>Adds a UML note of the default size with its top-left corner at (<paramref name="x"/>, <paramref name="y"/>).</summary>
    public NoteElement AddNote(double x, double y) => AddTextElement(new NoteElement(), x, y);

    /// <summary>
    /// True when a note and a UML element can be attached: one of the two is a note, the other a class, relationship,
    /// lifeline or message, and they are not attached yet. The order of the two ids does not matter.
    /// </summary>
    public bool CanAttachNote(string firstId, string secondId) => ResolveAttachment(firstId, secondId) is not null;

    /// <summary>Attaches a note to a UML element (in either order) as one undoable change.</summary>
    /// <returns>The new attachment, or <c>null</c> when <see cref="CanAttachNote"/> is false.</returns>
    public NoteAttachment? AttachNote(string firstId, string secondId)
    {
        if (ResolveAttachment(firstId, secondId) is not var (noteId, targetId))
        {
            return null;
        }

        var attachment = new NoteAttachment { NoteId = noteId, TargetId = targetId };
        Execute(() => Model.NoteAttachments.Add(attachment));
        SelectedId = attachment.Id;
        return attachment;
    }

    private (string NoteId, string TargetId)? ResolveAttachment(string firstId, string secondId)
    {
        var (noteId, targetId) = Model.FindNote(firstId) is not null ? (firstId, secondId) : (secondId, firstId);
        return Model.FindNote(noteId) is not null && Model.FindUmlElementById(targetId) is not null &&
               !Model.NoteAttachments.Any(a => a.NoteId == noteId && a.TargetId == targetId)
            ? (noteId, targetId)
            : null;
    }

    private T AddTextElement<T>(T element, double x, double y) where T : TextElement
    {
        var (width, height) = element.DefaultSize;
        Execute(() =>
        {
            Model.Add(element);
            Layout.Add(new ElementLayout { ElementId = element.Id, X = x, Y = y, Width = width, Height = height });
        });
        SelectedId = element.Id;
        return element;
    }

    #endregion

    public Lifeline AddLifeline(LifelineKind kind, double x)
    {
        var lifeline = new Lifeline
        {
            Kind = kind,
            Name = UniqueName(kind == LifelineKind.Actor ? "actor" : "object", SequenceModel.Lifelines.Select(l => l.Name), alwaysNumber: true),
        };
        Execute(() =>
        {
            SequenceModel.Lifelines.Add(lifeline);
            Layout.Add(new ElementLayout
            {
                ElementId = lifeline.Id,
                X = x,
                Y = SequenceDiagramRenderer.DefaultTop,
                Width = 120,
                Height = kind == LifelineKind.Actor ? SequenceDiagramRenderer.ActorHeadHeight : SequenceDiagramRenderer.ObjectHeadHeight,
            });
        });
        SelectedId = lifeline.Id;
        return lifeline;
    }

    public SequenceMessage? AddMessage(MessageKind kind, string sourceId, string targetId, double y)
    {
        var source = SequenceModel.FindLifeline(sourceId);
        var target = SequenceModel.FindLifeline(targetId);
        if (source is null || target is null)
        {
            return null;
        }

        var message = new SequenceMessage
        {
            Kind = kind,
            SourceId = sourceId,
            TargetId = targetId,
            Text = kind switch
            {
                MessageKind.Reply => "result",
                MessageKind.Create => "new()",
                MessageKind.Asynchronous => "notify()",
                _ => "call()",
            },
        };
        Execute(() =>
        {
            SequenceModel.Messages.Add(message);
            Layout.Add(new ElementLayout
            {
                ElementId = message.Id,
                Y = Math.Max(y, SequenceDiagramRenderer.MinimumMessageY(Layout, source, target)),
            });
        });
        SelectedId = message.Id;
        return message;
    }

    #endregion

    #region Common element operations

    /// <summary>
    /// Moves an element to a new position without recording undo (used while dragging; the caller records
    /// the snapshot taken at the start of the drag). Lifelines move horizontally, messages vertically.
    /// </summary>
    public void MoveElementTo(string id, double x, double y)
    {
        var layout = Layout.Find(id);
        if (layout is null)
        {
            return;
        }

        switch (Model.FindElement(id))
        {
            case GraphNode node:
                MoveGraphNode(node, layout, x, y);
                break;
            case UmlClass or TextElement:
                layout.X = x;
                layout.Y = y;
                break;
            case Lifeline:
                layout.X = x;
                break;
            case SequenceMessage message:
                var model = SequenceModel;
                layout.Y = Math.Max(y, SequenceDiagramRenderer.MinimumMessageY(
                    Layout, model.FindLifeline(message.SourceId), model.FindLifeline(message.TargetId)));
                break;
        }
    }

    /// <summary>
    /// Replaces the model and layout with those of <paramref name="source"/>, a changed copy of this diagram (e.g. the AI's
    /// changes), as one undoable change. The selection is kept while its element still exists.
    /// </summary>
    public void ReplaceContent(DiagramDocument source)
    {
        var copy = DiagramSerializer.Clone(source);
        Execute(() =>
        {
            Document.Model = copy.Model;
            Document.Layout.Elements = copy.Layout.Elements;
        });
        if (SelectedId is not null && Model.FindElement(SelectedId) is null)
        {
            SelectedId = null;
        }
    }

    /// <summary>Whether <see cref="Arrange"/> has anything to arrange.</summary>
    public bool CanArrange => DiagramArranger.CanArrange(Document);

    /// <summary>
    /// Rearranges the whole diagram so that it reads well, as one undoable change (see <see cref="DiagramArranger"/>).
    /// False when the diagram has nothing to arrange.
    /// </summary>
    public bool Arrange()
    {
        if (!CanArrange)
        {
            return false;
        }

        Execute(() => DiagramArranger.Arrange(Document));
        return true;
    }

    /// <summary>
    /// Rearranges the whole diagram as <see cref="Arrange"/> does, but works the new layout out on a copy on another
    /// thread, so the caller can show progress meanwhile; it is applied as one undoable change. False when the diagram
    /// has nothing to arrange, or was changed meanwhile, which arranging would undo. Cancelling discards the new layout
    /// with an <see cref="OperationCanceledException"/>.
    /// </summary>
    public async Task<bool> ArrangeAsync(CancellationToken cancellationToken = default)
    {
        if (!CanArrange)
        {
            return false;
        }

        var before = CreateSnapshot();
        var changes = _changes;
        var copy = DiagramSerializer.Deserialize(before);
        await Task.Run(() => DiagramArranger.Arrange(copy), cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();

        // Edited meanwhile: drawing the diagram also fits sizes to content, which is not an edit and does not count.
        if (_changes != changes)
        {
            return false;
        }

        // Arranging changes positions and sizes only, so the copy's layout is all there is to take over.
        Document.Layout.Elements = copy.Layout.Elements;
        PushUndo(before);
        NotifyChanged();
        return true;
    }

    /// <summary>
    /// Moves an element by an offset without recording undo, as <see cref="MoveElementTo"/> does (e.g. when nudged with
    /// the arrow keys). False when the element cannot be moved, such as a relationship.
    /// </summary>
    public bool MoveElementBy(string id, double dx, double dy)
    {
        if (Layout.Find(id) is not { } layout ||
            Model.FindElement(id) is not (GraphNode or UmlClass or TextElement or Lifeline or SequenceMessage))
        {
            return false;
        }

        MoveElementTo(id, layout.X + dx, layout.Y + dy);
        return true;
    }

    /// <summary>
    /// Sets the font of an element, or the diagram font when <paramref name="elementId"/> is <c>null</c>, as one
    /// undoable change. A <c>null</c> <paramref name="font"/> makes the element use the diagram font again
    /// (or the diagram use the default font).
    /// </summary>
    public void SetFont(string? elementId, DiagramFont? font)
    {
        if (elementId is null)
        {
            Execute(() => Layout.Font = font?.Clone());
            return;
        }

        if (Model.FindElement(elementId) is null)
        {
            return;
        }

        Execute(() =>
        {
            // Relationships have no layout entry until they get a font.
            var layout = Layout.Find(elementId) ?? Layout.Add(new ElementLayout { ElementId = elementId });
            layout.Font = font?.Clone();
        });
    }

    /// <summary>True for elements drawn as a shape with a fill and a border: not for relationships, messages and edges.</summary>
    public static bool HasShape(ModelElement? element) => element is UmlClass or Lifeline or GraphNode or TextElement;

    /// <summary>Sets the fill and border of an element's shape; <c>null</c> (or an empty style) restores the defaults.</summary>
    public void SetShapeStyle(string elementId, ShapeStyle? style)
    {
        if (!HasShape(Model.FindElement(elementId)))
        {
            return;
        }

        Execute(() =>
        {
            var layout = Layout.Find(elementId) ?? Layout.Add(new ElementLayout { ElementId = elementId });
            layout.Style = style is null || style.IsEmpty ? null : style.Clone();
        });
    }

    /// <summary>True for connectors drawn as a line: relationships, messages and edges (not note attachments).</summary>
    public static bool HasLine(ModelElement? element) => element is UmlRelationship or SequenceMessage or GraphEdge;

    /// <summary>Sets the colour and thickness of a connector; <c>null</c> (or an empty style) restores the defaults.</summary>
    public void SetLineStyle(string elementId, LineStyle? style)
    {
        if (!HasLine(Model.FindElement(elementId)))
        {
            return;
        }

        Execute(() =>
        {
            // Relationships and edges have no layout entry until they get a font or a line style.
            var layout = Layout.Find(elementId) ?? Layout.Add(new ElementLayout { ElementId = elementId });
            layout.Line = style is null || style.IsEmpty ? null : style.Clone();
        });
    }

    /// <summary>Sets the preferred width; renderers never shrink an element below its content.</summary>
    /// <summary>
    /// Sets the preferred size without recording undo (used while dragging the resize handle). Classes only take the
    /// width, as their height follows their members; text boxes take both.
    /// </summary>
    public void ResizeElement(string id, double width, double? height = null)
    {
        if (Layout.Find(id) is not { } layout)
        {
            return;
        }

        if (Model.FindTextElement(id) is not null)
        {
            layout.Width = Math.Max(TextElement.MinWidth, width);
            layout.Height = Math.Max(TextElement.MinHeight, height ?? layout.Height);
            return;
        }

        if (Model is GraphDiagramModel graph && graph.FindNode(id) is { } node)
        {
            ResizeGraphNode(node, layout, width, height);
            return;
        }

        layout.Width = Math.Max(40, width);
    }

    #region Z-order

    /// <summary>
    /// The nodes stacked together with <paramref name="id"/>, back to front, and how to put them in a new order;
    /// <c>null</c> for elements without a stacking order (relationships, messages, edges and note attachments).
    /// Classes, lifelines and graph nodes are drawn in the order of their model list. In node-and-edge diagrams,
    /// containers (system boundaries, deployment nodes) are stacked among themselves, under the other nodes. Notes
    /// and text boxes are stacked together, above the UML elements.
    /// </summary>
    private ZOrderStack? StackOf(string id) => Model switch
    {
        _ when Model.FindTextElement(id) is not null => ZOrderStack.OfTextElements(Model),
        ClassDiagramModel model when model.FindClass(id) is not null => ZOrderStack.Of(model.Classes, _ => true),
        SequenceDiagramModel model when model.FindLifeline(id) is not null => ZOrderStack.Of(model.Lifelines, _ => true),
        GraphDiagramModel model when model.FindNode(id) is { } node =>
            ZOrderStack.Of(model.Nodes, n => GraphRules.IsContainer(n.Kind) == GraphRules.IsContainer(node.Kind)),
        _ => null,
    };

    /// <summary>True when the element is a node with a stacking order: a class, lifeline or graph node, note or text box.</summary>
    public bool HasStackingOrder(string id) => StackOf(id) is not null;

    /// <summary>
    /// True when the stacking order of the node <paramref name="id"/> can change that way: it is not already the
    /// front-most (for <see cref="ZOrderChange.BringToFront"/> and <see cref="ZOrderChange.BringForward"/>) or the
    /// back-most node of its stack (for the other two).
    /// </summary>
    public bool CanChangeZOrder(string id, ZOrderChange change)
    {
        if (StackOf(id) is not { } stack)
        {
            return false;
        }

        var index = stack.Ids.IndexOf(id);
        return change is ZOrderChange.BringToFront or ZOrderChange.BringForward
            ? index < stack.Ids.Count - 1
            : index > 0;
    }

    /// <summary>
    /// Changes the stacking order of a node as one undoable change; the last node of a stack is in front. Bring
    /// forward and send backward pass the nearest node that overlaps this one, so the change is visible; without
    /// overlaps they move one step.
    /// </summary>
    /// <returns>False when the order could not change.</returns>
    public bool ChangeZOrder(string id, ZOrderChange change)
    {
        if (!CanChangeZOrder(id, change))
        {
            return false;
        }

        var stack = StackOf(id)!;
        var ids = stack.Ids;
        var index = ids.IndexOf(id);
        var target = change switch
        {
            ZOrderChange.BringToFront => ids.Count - 1,
            ZOrderChange.SendToBack => 0,
            ZOrderChange.BringForward => NearestOverlap(Enumerable.Range(index + 1, ids.Count - index - 1)) ?? index + 1,
            _ => NearestOverlap(Enumerable.Range(0, index).Reverse()) ?? index - 1,
        };

        var order = ids.ToList();
        order.RemoveAt(index);
        order.Insert(target, id);
        Execute(() => stack.Reorder(order));
        return true;

        int? NearestOverlap(IEnumerable<int> indexes)
        {
            if (Layout.Find(id) is not { } bounds)
            {
                return null;
            }

            foreach (var i in indexes)
            {
                if (Layout.Find(ids[i]) is { } other && Overlaps(bounds, other))
                {
                    return i;
                }
            }

            return null;
        }

        static bool Overlaps(ElementLayout a, ElementLayout b) =>
            a.X < b.X + b.Width && b.X < a.X + a.Width && a.Y < b.Y + b.Height && b.Y < a.Y + a.Height;
    }

    /// <summary>The ids of nodes stacked together, back to front, and a way to rearrange them in their model list.</summary>
    private sealed record ZOrderStack(List<string> Ids, Action<IReadOnlyList<string>> Reorder)
    {
        /// <summary>
        /// The elements of <paramref name="list"/> that match <paramref name="inStack"/>; reordering puts them back into
        /// the positions they occupied, so the other elements keep their places.
        /// </summary>
        public static ZOrderStack Of<T>(List<T> list, Func<T, bool> inStack) where T : ModelElement
        {
            var slots = Enumerable.Range(0, list.Count).Where(i => inStack(list[i])).ToList();
            return new ZOrderStack(
                slots.Select(i => list[i].Id).ToList(),
                order =>
                {
                    var byId = slots.Select(i => list[i]).ToDictionary(e => e.Id);
                    for (var k = 0; k < slots.Count; k++)
                    {
                        list[slots[k]] = byId[order[k]];
                    }
                });
        }

        /// <summary>Notes and text boxes, drawn in <see cref="TextElement.ZIndex"/> order; reordering renumbers them.</summary>
        public static ZOrderStack OfTextElements(DiagramModel model) => new(
            model.TextElements().Select(e => e.Id).ToList(),
            order =>
            {
                for (var k = 0; k < order.Count; k++)
                {
                    model.FindTextElement(order[k])!.ZIndex = k;
                }
            });
    }

    #endregion

    public IReadOnlyList<string> Delete(string id)
    {
        if (Model.FindElement(id) is null)
        {
            return [];
        }

        IReadOnlyList<string> removed = [];
        Execute(() =>
        {
            removed = Model.RemoveElement(id);
            foreach (var removedId in removed)
            {
                Layout.Remove(removedId);
            }
        });

        if (SelectedId is not null && removed.Contains(SelectedId))
        {
            SelectedId = null;
        }

        return removed;
    }

    /// <summary>Serializes an element for the clipboard; returns <c>null</c> when it does not exist.</summary>
    public string? Copy(string id)
    {
        var payload = new ClipboardPayload { DiagramType = Document.DiagramType, Layout = Layout.Find(id) };
        switch (Model.FindElement(id))
        {
            case UmlClass cls: payload.Class = cls; break;
            case UmlRelationship relationship: payload.Relationship = relationship; break;
            case Lifeline lifeline: payload.Lifeline = lifeline; break;
            case SequenceMessage message: payload.Message = message; break;
            case TextBoxElement textBox: payload.TextBox = textBox; break;
            case NoteElement note: payload.Note = note; break;
            case GraphNode node: payload.Node = node; break;
            case GraphEdge edge: payload.Edge = edge; break;
            default: return null;
        }

        return payload.ToJson();
    }

    public string? Cut(string id)
    {
        var text = Copy(id);
        if (text is not null)
        {
            Delete(id);
        }

        return text;
    }

    /// <summary>True when the clipboard holds an element for this diagram type, or a note or text box (which fit any diagram).</summary>
    public bool CanPaste(string? text) =>
        ClipboardPayload.TryParse(text, out var payload) && (payload.DiagramType == Document.DiagramType || payload.TextElement is not null);

    /// <summary>
    /// Pastes an element copied with <see cref="Copy"/>. The pasted element gets a new id; it is placed at
    /// (<paramref name="x"/>, <paramref name="y"/>) when given, otherwise slightly offset from the original.
    /// Connectors are pasted only when both of their ends exist in this diagram.
    /// </summary>
    /// <returns>The id of the pasted element, or <c>null</c> when nothing could be pasted.</returns>
    public string? Paste(string? text, double? x = null, double? y = null)
    {
        if (!CanPaste(text) || !ClipboardPayload.TryParse(text, out var payload))
        {
            return null;
        }

        var layout = payload.Layout?.Clone() ?? new ElementLayout();
        var newId = ModelElement.NewId();
        layout.ElementId = newId;
        Action? mutation = null;

        if (payload.Class is { } cls && Model is ClassDiagramModel classModel)
        {
            cls.Id = newId;
            cls.Name = UniqueName(cls.Name, classModel.Classes.Select(c => c.Name), alwaysNumber: false);
            layout.X = x ?? layout.X + 24;
            layout.Y = y ?? layout.Y + 24;
            mutation = () =>
            {
                classModel.Classes.Add(cls);
                Layout.Add(layout);
            };
        }
        else if (payload.Relationship is { } relationship && Model is ClassDiagramModel relationshipModel &&
                 relationshipModel.FindClass(relationship.SourceId) is not null &&
                 relationshipModel.FindClass(relationship.TargetId) is not null)
        {
            relationship.Id = newId;
            mutation = () =>
            {
                relationshipModel.Relationships.Add(relationship);
                if (layout.Font is not null || layout.Line is not null)
                {
                    Layout.Add(layout);
                }
            };
        }
        else if (payload.Lifeline is { } lifeline && Model is SequenceDiagramModel lifelineModel)
        {
            lifeline.Id = newId;
            lifeline.Name = UniqueName(lifeline.Name, lifelineModel.Lifelines.Select(l => l.Name), alwaysNumber: false);
            layout.X = x ?? layout.X + 24;
            mutation = () =>
            {
                lifelineModel.Lifelines.Add(lifeline);
                Layout.Add(layout);
            };
        }
        else if (payload.Node is { } node && Model is GraphDiagramModel nodeModel)
        {
            node.Id = newId;
            if (node.Name is not null)
            {
                node.Name = UniqueName(node.Name, nodeModel.Nodes.Select(n => n.Name ?? string.Empty), alwaysNumber: false);
            }

            layout.X = x ?? layout.X + 24;
            layout.Y = y ?? layout.Y + 24;
            mutation = () =>
            {
                nodeModel.Nodes.Add(node);
                Layout.Add(layout);
            };
        }
        else if (payload.Edge is { } edge && Model is GraphDiagramModel edgeModel &&
                 CheckEdge(edge.Kind, edge.SourceId, edge.TargetId) is null)
        {
            edge.Id = newId;
            mutation = () =>
            {
                edgeModel.Edges.Add(edge);
                if (layout.Font is not null || layout.Line is not null)
                {
                    Layout.Add(layout);
                }
            };
        }
        else if (payload.TextElement is { } textElement)
        {
            textElement.Id = newId;
            layout.X = x ?? layout.X + 24;
            layout.Y = y ?? layout.Y + 24;
            mutation = () =>
            {
                Model.Add(textElement);
                Layout.Add(layout);
            };
        }
        else if (payload.Message is { } message && Model is SequenceDiagramModel messageModel &&
                 messageModel.FindLifeline(message.SourceId) is { } source &&
                 messageModel.FindLifeline(message.TargetId) is { } target)
        {
            message.Id = newId;
            layout.Y = Math.Max(y ?? layout.Y + 30, SequenceDiagramRenderer.MinimumMessageY(Layout, source, target));
            mutation = () =>
            {
                messageModel.Messages.Add(message);
                Layout.Add(layout);
            };
        }

        if (mutation is null)
        {
            return null;
        }

        Execute(mutation);
        SelectedId = newId;
        return newId;
    }

    #endregion

    #region In-line editing

    /// <summary>
    /// Starts an in-line edit for the element part that was hit. Hitting the empty part of an attribute or
    /// operation compartment adds a new member that is removed again if the edit ends empty.
    /// </summary>
    public InlineEditSession? BeginInlineEdit(HitInfo hit)
    {
        var snapshot = CreateSnapshot();
        InlineEditSession Session(HitPart part, string text, int index = -1, bool isNew = false) =>
            new(this, hit.ElementId, part, index, text, snapshot, isNew);

        switch (Model.FindElement(hit.ElementId))
        {
            case UmlClass cls:
                switch (hit.Part)
                {
                    case HitPart.ClassHeader:
                        return Session(HitPart.ClassHeader, cls.Name);
                    case HitPart.ClassAttribute when hit.Index >= 0 && hit.Index < cls.Attributes.Count:
                        return Session(HitPart.ClassAttribute, FormatAttribute(cls, cls.Attributes[hit.Index]), hit.Index);
                    case HitPart.ClassAttributesArea:
                        cls.Attributes.Add(new UmlAttribute { Name = string.Empty });
                        return Session(HitPart.ClassAttribute, string.Empty, cls.Attributes.Count - 1, isNew: true);
                    case HitPart.ClassOperation when hit.Index >= 0 && hit.Index < cls.Operations.Count:
                        return Session(HitPart.ClassOperation, UmlNotation.FormatForEditing(cls.Operations[hit.Index]), hit.Index);
                    case HitPart.ClassOperationsArea:
                        cls.Operations.Add(new UmlOperation { Name = string.Empty });
                        return Session(HitPart.ClassOperation, string.Empty, cls.Operations.Count - 1, isNew: true);
                }

                return null;
            case UmlRelationship relationship:
                return Session(HitPart.Relationship, relationship.Label ?? string.Empty);
            case Lifeline lifeline:
                return Session(HitPart.LifelineHead, UmlNotation.FormatLifeline(lifeline));
            case SequenceMessage message:
                return Session(HitPart.Message, message.Text);
            case TextElement textElement:
                return Session(HitPart.Text, textElement.Text);
            case GraphNode node when GraphRules.HasName(node.Kind):
                return Session(node.Kind == GraphNodeKind.SystemBoundary ? HitPart.Boundary : HitPart.Node, node.Name ?? string.Empty);
            case GraphEdge edge:
                return Session(HitPart.Edge, edge.Label ?? string.Empty);
            default:
                return null;
        }
    }

    internal void CompleteInlineEdit(InlineEditSession session, string text)
    {
        text = text.Trim();
        var changed = false;

        switch (Model.FindElement(session.ElementId))
        {
            case UmlClass cls when session.Part == HitPart.ClassHeader:
                if (text.Length > 0 && text != cls.Name)
                {
                    cls.Name = text;
                    changed = true;
                }

                break;

            case UmlClass cls when session.Part == HitPart.ClassAttribute && session.Index < cls.Attributes.Count:
                var existingAttribute = cls.Attributes[session.Index];
                var attribute = UmlNotation.ParseAttribute(
                    text, session.IsNew ? UmlVisibility.Private : existingAttribute.Visibility);
                if (attribute is null)
                {
                    cls.Attributes.RemoveAt(session.Index);
                    changed = !session.IsNew;
                }
                else
                {
                    changed = session.IsNew || FormatAttribute(cls, attribute) != FormatAttribute(cls, existingAttribute);
                    attribute.TaggedValues = existingAttribute.TaggedValues;
                    cls.Attributes[session.Index] = attribute;
                }

                break;

            case UmlClass cls when session.Part == HitPart.ClassOperation && session.Index < cls.Operations.Count:
                var existingOperation = cls.Operations[session.Index];
                var operation = UmlNotation.ParseOperation(
                    text, session.IsNew ? UmlVisibility.Public : existingOperation.Visibility);
                if (operation is null)
                {
                    cls.Operations.RemoveAt(session.Index);
                    changed = !session.IsNew;
                }
                else
                {
                    changed = session.IsNew ||
                              UmlNotation.FormatForEditing(operation) != UmlNotation.FormatForEditing(existingOperation);
                    // The text holds the signature only: keep what else the operation has.
                    operation.HttpMethod = existingOperation.HttpMethod;
                    operation.Path = existingOperation.Path;
                    operation.TaggedValues = existingOperation.TaggedValues;
                    cls.Operations[session.Index] = operation;
                }

                break;

            case UmlRelationship relationship:
                var label = text.Length == 0 ? null : text;
                changed = label != relationship.Label;
                relationship.Label = label;
                break;

            case Lifeline lifeline:
                var (name, type) = UmlNotation.ParseLifeline(text);
                if (name.Length > 0 || type is not null)
                {
                    changed = name != lifeline.Name || type != lifeline.Type;
                    lifeline.Name = name;
                    lifeline.Type = type;
                }

                break;

            case SequenceMessage message:
                changed = text != message.Text;
                message.Text = text;
                break;

            case TextElement textElement:
                changed = text != textElement.Text;
                textElement.Text = text;
                break;

            case GraphNode node when text.Length > 0:
                changed = text != node.Name;
                node.Name = text;
                break;

            case GraphEdge edge:
                var edgeLabel = text.Length == 0 ? null : text;
                changed = edgeLabel != edge.Label;
                edge.Label = edgeLabel;
                break;
        }

        if (changed)
        {
            PushUndo(session.Snapshot);
            NotifyChanged();
        }
        else
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    internal void CancelInlineEdit(InlineEditSession session)
    {
        if (session.IsNew && Model.FindElement(session.ElementId) is UmlClass cls)
        {
            if (session.Part == HitPart.ClassAttribute && session.Index < cls.Attributes.Count)
            {
                cls.Attributes.RemoveAt(session.Index);
            }
            else if (session.Part == HitPart.ClassOperation && session.Index < cls.Operations.Count)
            {
                cls.Operations.RemoveAt(session.Index);
            }
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    private static string FormatAttribute(UmlClass cls, UmlAttribute attribute) =>
        UmlNotation.FormatForEditing(attribute, includeVisibility: cls.Kind != ClassifierKind.Enumeration);

    #endregion

    private static string UniqueName(string baseName, IEnumerable<string> existing, bool alwaysNumber)
    {
        var names = new HashSet<string>(existing, StringComparer.Ordinal);
        if (!alwaysNumber && !names.Contains(baseName))
        {
            return baseName;
        }

        for (var i = alwaysNumber ? 1 : 2; ; i++)
        {
            var candidate = baseName + i;
            if (!names.Contains(candidate))
            {
                return candidate;
            }
        }
    }
}
