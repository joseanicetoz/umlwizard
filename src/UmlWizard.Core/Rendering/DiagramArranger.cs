using SkiaSharp;
using UmlWizard.Core.Model;

namespace UmlWizard.Core.Rendering;

/// <summary>
/// Rearranges a whole diagram so that it reads well: the Arrange command. Only positions change (and the size of the
/// containers of node diagrams, which are fitted around what they hold); the model does not.
/// <list type="bullet">
/// <item>Class diagrams are laid out in layers: base classes and interfaces above the classes that inherit from or
/// realize them, wholes above their parts, and the source of other relationships above their target. Groups of classes
/// that no relationship connects are laid out separately and packed in rows.</item>
/// <item>State machine, activity, component and deployment diagrams are laid out in layers from top to bottom, and use
/// case diagrams from left to right, with <see cref="GraphDiagramLayout"/>; nodes stay in the container they are in.</item>
/// <item>Sequence diagrams keep the order of their messages, which is what they mean: the lifelines are ordered by the
/// first message they take part in and spaced so every message label fits, and the messages are evenly spaced.</item>
/// <item>Notes and text boxes that were above the whole diagram (such as a title) stay above it; the others go in a column
/// to its right, each note level with the element it is attached to.</item>
/// </list>
/// </summary>
public static class DiagramArranger
{
    private const double Margin = 40;
    private const double LifelineGap = 50;
    private const double MessageGap = 40;
    private const double TextGap = 16;
    private const double ColumnGap = 60;

    /// <summary>Whether the diagram has UML elements to arrange.</summary>
    public static bool CanArrange(DiagramDocument document) => document.Model switch
    {
        ClassDiagramModel model => model.Classes.Count > 0,
        SequenceDiagramModel model => model.Lifelines.Count > 0,
        GraphDiagramModel model => model.Nodes.Count > 0,
        _ => false,
    };

    public static void Arrange(DiagramDocument document)
    {
        if (!CanArrange(document))
        {
            return;
        }

        using var renderer = DiagramRenderer.Create(document.DiagramType);
        renderer.UpdateLayout(document);

        // Notes and text boxes that are above all the UML elements, such as a title, stay above them.
        var before = renderer.GetUmlContentBounds(document);
        var texts = document.Model.TextElements()
            .Select(e => (Element: e, Bounds: renderer.GetElementBounds(document, e.Id) ?? SKRect.Empty))
            .ToList();
        var headings = texts.Where(t => !before.IsEmpty && t.Bounds.Bottom <= before.Top)
            .OrderBy(t => t.Bounds.Top).ThenBy(t => t.Bounds.Left).Select(t => t.Element).ToList();
        var others = texts.Select(t => t.Element).Except(headings).OrderBy(e => document.Layout.Find(e.Id)?.Y ?? 0).ToList();

        switch (document.Model)
        {
            case ClassDiagramModel model:
                ArrangeClasses(document, model, renderer);
                break;
            case SequenceDiagramModel model:
                ArrangeSequence(document, model, (SequenceDiagramRenderer)renderer);
                break;
            case GraphDiagramModel model:
                // Fork and join bars are widened to span their flows; start again from the default width.
                foreach (var bar in model.Nodes.Where(n => n.Kind == GraphNodeKind.ForkJoin))
                {
                    if (document.Layout.Find(bar.Id) is { } layout)
                    {
                        (layout.Width, layout.Height) = GraphRules.DefaultSize(GraphNodeKind.ForkJoin);
                    }
                }

                GraphDiagramLayout.Arrange(document, ContainerOf(document, model), leftToRight: document.DiagramType == DiagramType.UseCase, packParts: true);
                break;
        }

        // Whole pixels: tidy coordinates, and arranging again changes nothing.
        foreach (var layout in document.Layout.Elements)
        {
            (layout.X, layout.Y) = (Math.Round(layout.X), Math.Round(layout.Y));
        }

        renderer.UpdateLayout(document);
        PlaceTextElements(document, renderer, headings, others);
    }

    private static void ArrangeClasses(DiagramDocument document, ClassDiagramModel model, DiagramRenderer renderer)
    {
        // Each class is placed at its drawn size, which grows to fit its members.
        var size = model.Classes.ToDictionary(
            c => c.Id,
            c => renderer.GetElementBounds(document, c.Id) is { } b ? ((double)b.Width, (double)b.Height) : (160.0, 80.0));

        // Generalizations and whole-part relationships make the layers: the general above the specific, the whole above
        // its parts. Other relationships only do for the classes those leave out, which hang below their source; between
        // two classes already placed, they are drawn but do not move them (siblings that know each other stay side by
        // side). Generalizations first, so that when relationships go round in a circle, it is another one that gives way.
        var relationships = model.Relationships
            .Where(r => r.SourceId != r.TargetId && size.ContainsKey(r.SourceId) && size.ContainsKey(r.TargetId))
            .OrderBy(r => r.Kind switch
            {
                RelationshipKind.Inheritance or RelationshipKind.Realization => 0,
                RelationshipKind.Composition or RelationshipKind.Aggregation => 1,
                _ => 2,
            })
            .Select(r => (
                Edge: r.Kind is RelationshipKind.Inheritance or RelationshipKind.Realization ? (r.TargetId, r.SourceId) : (r.SourceId, r.TargetId),
                Structural: r.Kind is RelationshipKind.Inheritance or RelationshipKind.Realization or RelationshipKind.Composition or RelationshipKind.Aggregation))
            .ToList();
        var structured = relationships.Where(r => r.Structural).SelectMany(r => new[] { r.Edge.Item1, r.Edge.Item2 }).ToHashSet();
        var layering = relationships
            .Where(r => r.Structural || !structured.Contains(r.Edge.Item1) || !structured.Contains(r.Edge.Item2))
            .Select(r => r.Edge).Distinct().ToList();
        var drawnOnly = relationships.Select(r => r.Edge).Distinct().Except(layering).ToList();

        // Classes side by side leave room for the label and multiplicities of the relationship between them.
        var gaps = new Dictionary<(string, string), double>();
        foreach (var relationship in model.Relationships)
        {
            var length = ((ClassDiagramRenderer)renderer).TextLength(document, relationship) + 20;
            gaps[(relationship.SourceId, relationship.TargetId)] = Math.Max(gaps.GetValueOrDefault((relationship.SourceId, relationship.TargetId)), length);
        }

        var ids = model.Classes.Select(c => c.Id).ToList();
        var placed = LayeredLayout.ConnectedParts(ids, [.. layering, .. drawnOnly])
            .Select(part =>
            {
                var inPart = part.ToHashSet();
                return LayeredLayout.Place(
                    part, size, [.. layering.Where(e => inPart.Contains(e.Item1))], leftToRight: false,
                    drawnOnly: [.. drawnOnly.Where(e => inPart.Contains(e.Item1))], gaps: gaps).Positions;
            })
            .ToList();

        foreach (var (id, (x, y)) in placed.Count == 1 ? placed[0] : LayeredLayout.Pack(placed, size))
        {
            var layout = document.Layout.Find(id)!;
            layout.X = Margin + x;
            layout.Y = Margin + y;
        }
    }

    private static void ArrangeSequence(DiagramDocument document, SequenceDiagramModel model, SequenceDiagramRenderer renderer)
    {
        var layouts = model.Lifelines.ToDictionary(l => l.Id, l => document.Layout.Find(l.Id)!);
        var messages = model.Messages
            .Select(m => (Message: m, Layout: document.Layout.Find(m.Id)))
            .Where(m => m.Layout is not null && layouts.ContainsKey(m.Message.SourceId) && layouts.ContainsKey(m.Message.TargetId))
            .OrderBy(m => m.Layout!.Y)
            .ToList();
        var labels = renderer.MessageLabelSizes(document);

        // Lifelines in the order they first take part, the sender of a message before its receiver; lifelines without
        // messages keep their order after them.
        var first = new Dictionary<string, int>();
        for (var i = 0; i < messages.Count; i++)
        {
            first.TryAdd(messages[i].Message.SourceId, 2 * i);
            first.TryAdd(messages[i].Message.TargetId, 2 * i + 1);
        }

        var lifelines = model.Lifelines
            .OrderBy(l => first.TryGetValue(l.Id, out var rank) ? rank : int.MaxValue)
            .ThenBy(l => layouts[l.Id].X)
            .ToList();
        var index = lifelines.Select((l, i) => (l.Id, i)).ToDictionary(p => p.Id, p => p.i);

        // The distance between the centres of neighbouring lifelines: their heads apart, then widened for the labels of
        // the messages between them, the shortest messages first.
        var gaps = new double[Math.Max(0, lifelines.Count - 1)];
        for (var i = 0; i < gaps.Length; i++)
        {
            gaps[i] = layouts[lifelines[i].Id].Width / 2 + layouts[lifelines[i + 1].Id].Width / 2 + LifelineGap;
        }

        foreach (var (message, _) in messages.OrderBy(m => Math.Abs(index[m.Message.SourceId] - index[m.Message.TargetId])))
        {
            var (from, to) = (index[message.SourceId], index[message.TargetId]);
            var width = labels.TryGetValue(message.Id, out var label) ? label.Width : 0;
            if (from == to)
            {
                // A message to itself: the loop and its label go to the right.
                if (from < gaps.Length)
                {
                    gaps[from] = Math.Max(gaps[from], SequenceDiagramRenderer.SelfLoopWidth + width + 20);
                }

                continue;
            }

            var (left, right) = (Math.Min(from, to), Math.Max(from, to));
            var span = Enumerable.Range(left, right - left).Sum(i => gaps[i]);
            var needed = width + 20;
            for (var i = left; i < right && span < needed; i++)
            {
                gaps[i] += (needed - span) / (right - left);
            }
        }

        var center = Margin + layouts[lifelines[0].Id].Width / 2;
        for (var i = 0; i < lifelines.Count; i++)
        {
            var layout = layouts[lifelines[i].Id];
            layout.X = center - layout.Width / 2;
            layout.Y = SequenceDiagramRenderer.DefaultTop;
            center += i < gaps.Length ? gaps[i] : 0;
        }

        // The messages evenly spaced below the heads, in the same order; a message to itself takes the height of its loop.
        var y = layouts.Values.Max(l => l.Y + l.Height) + 30;
        foreach (var (message, layout) in messages)
        {
            var height = labels.TryGetValue(message.Id, out var label) ? label.Height : 0;
            y = Math.Max(y, SequenceDiagramRenderer.MinimumMessageY(document.Layout, model.FindLifeline(message.SourceId), model.FindLifeline(message.TargetId)));
            layout!.Y = y;
            y += Math.Max(MessageGap, height + 20) + (message.SourceId == message.TargetId ? SequenceDiagramRenderer.SelfLoopHeight : 0);
        }
    }

    /// <summary>
    /// The container each node is in, as the editor sees it: the smallest system boundary, deployment node, device or
    /// execution environment that holds it completely.
    /// </summary>
    public static Dictionary<string, string> ContainerOf(DiagramDocument document, GraphDiagramModel model)
    {
        var bounds = model.Nodes
            .Where(n => document.Layout.Find(n.Id) is not null)
            .ToDictionary(n => n.Id, n => document.Layout.Find(n.Id)!);
        double Area(ElementLayout l) => l.Width * l.Height;
        bool Holds(ElementLayout outer, ElementLayout inner) =>
            inner.X >= outer.X && inner.Y >= outer.Y && inner.X + inner.Width <= outer.X + outer.Width && inner.Y + inner.Height <= outer.Y + outer.Height;

        var containers = model.Nodes.Where(n => GraphRules.IsContainer(n.Kind) && bounds.ContainsKey(n.Id)).ToList();
        var result = new Dictionary<string, string>();
        foreach (var node in model.Nodes.Where(n => n.Kind != GraphNodeKind.SystemBoundary && bounds.ContainsKey(n.Id)))
        {
            // Strictly larger, so that two containers never hold each other.
            var inner = bounds[node.Id];
            var container = containers
                .Where(c => c.Id != node.Id && Area(bounds[c.Id]) > Area(inner) && Holds(bounds[c.Id], inner))
                .MinBy(c => Area(bounds[c.Id]));
            if (container is not null)
            {
                result[node.Id] = container.Id;
            }
        }

        return result;
    }

    /// <summary>
    /// Stacks the <paramref name="headings"/> above the arranged diagram, moving it down to make room, and the
    /// <paramref name="others"/> in a column to its right: a note level with the element it is attached to, if it can
    /// be, and the rest below them in their order from top to bottom.
    /// </summary>
    private static void PlaceTextElements(DiagramDocument document, DiagramRenderer renderer, List<TextElement> headings, List<TextElement> others)
    {
        var y = Margin;
        foreach (var heading in headings)
        {
            var layout = document.Layout.Find(heading.Id)!;
            (layout.X, layout.Y) = (Margin, y);
            y += layout.Height + TextGap;
        }

        var bounds = renderer.GetUmlContentBounds(document);
        if (headings.Count > 0 && bounds.Top < y + TextGap)
        {
            var dy = y + TextGap - bounds.Top;
            var texts = document.Model.TextElements().Select(e => e.Id).ToHashSet();
            foreach (var layout in document.Layout.Elements.Where(l => !texts.Contains(l.ElementId)))
            {
                layout.Y += dy;
            }

            bounds = renderer.GetUmlContentBounds(document);
        }

        var targetOf = document.Model.NoteAttachments
            .GroupBy(a => a.NoteId)
            .ToDictionary(g => g.Key, g => g.Select(a => renderer.GetElementBounds(document, a.TargetId)).OfType<SKRect>().Select(r => (double)r.Top).DefaultIfEmpty(double.MaxValue).Min());
        var column = others
            .Select((e, i) => (Element: e, Desired: targetOf.TryGetValue(e.Id, out var top) ? top : double.MaxValue, Order: i))
            .OrderBy(e => e.Desired).ThenBy(e => e.Order)
            .ToList();

        var next = Math.Round(bounds.Top);
        foreach (var (element, desired, _) in column)
        {
            var layout = document.Layout.Find(element.Id)!;
            layout.X = Math.Round(bounds.Right + ColumnGap);
            layout.Y = desired == double.MaxValue ? next : Math.Max(next, Math.Round(desired));
            next = layout.Y + layout.Height + TextGap;
        }
    }
}
