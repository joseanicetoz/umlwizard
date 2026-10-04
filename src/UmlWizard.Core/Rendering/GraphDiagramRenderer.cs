using SkiaSharp;
using UmlWizard.Core.Model;

namespace UmlWizard.Core.Rendering;

/// <summary>Draws state machine, activity, use case and component diagrams: nodes of several shapes and the edges between them.</summary>
internal sealed class GraphDiagramRenderer : DiagramRenderer
{
    private const float Pad = 8f;
    private const float EdgeTolerance = 6f;
    private const float MaxAutoWidth = 260f;
    private const float LoopSize = 36f;

    /// <summary>How far the top and side faces of a deployment node's 3-D box reach back.</summary>
    private const float BoxDepth = 10f;

    private protected override void UpdateDiagramLayout(DiagramDocument document)
    {
        var model = (GraphDiagramModel)document.Model;
        for (var i = 0; i < model.Nodes.Count; i++)
        {
            var node = model.Nodes[i];
            var (defaultWidth, defaultHeight) = GraphRules.DefaultSize(node.Kind);
            var layout = document.Layout.Find(node.Id) ?? document.Layout.Add(new ElementLayout
            {
                ElementId = node.Id,
                X = 40 + i % 5 * 180,
                Y = 40 + i / 5 * 140,
                Width = defaultWidth,
                Height = defaultHeight,
            });

            if (!GraphRules.IsResizable(node.Kind))
            {
                (layout.Width, layout.Height) = (defaultWidth, defaultHeight);
                continue;
            }

            var (minWidth, minHeight) = GraphRules.MinimumSize(node.Kind);
            var (textWidth, textHeight) = MeasureName(document, node, layout);
            layout.Width = Math.Max(layout.Width, Math.Max(minWidth, textWidth));
            layout.Height = Math.Max(layout.Height, Math.Max(minHeight, textHeight));
        }
    }

    private protected override void RenderDiagram(SKCanvas canvas, DiagramDocument document, RenderOptions options)
    {
        var model = (GraphDiagramModel)document.Model;

        // Containers (system boundaries, deployment nodes) lie under everything, outer ones under the ones nested in
        // them; edges lie under the other nodes.
        foreach (var node in ContainersOuterFirst(document, model))
        {
            DrawNode(canvas, document, node, options);
        }

        foreach (var edge in model.Edges)
        {
            if (TryGetEdgeGeometry(document, edge, out var geometry))
            {
                DrawEdge(canvas, document, edge, geometry, options.SelectedId == edge.Id);
            }
        }

        foreach (var node in model.Nodes.Where(n => !GraphRules.IsContainer(n.Kind)))
        {
            DrawNode(canvas, document, node, options);
        }

        // Labels last, so nodes never hide them.
        foreach (var edge in model.Edges)
        {
            if (TryGetEdgeGeometry(document, edge, out var geometry))
            {
                DrawEdgeLabel(canvas, document, edge, geometry);
            }
        }
    }

    private protected override HitInfo? HitTestDiagram(DiagramDocument document, SKPoint point, string? selectedId)
    {
        var model = (GraphDiagramModel)document.Model;
        if (selectedId is not null && model.FindNode(selectedId) is { } selected && GraphRules.IsResizable(selected.Kind))
        {
            var handle = HandleRect(NodeBounds(document, selected));
            handle.Inflate(3f, 3f);
            if (handle.Contains(point))
            {
                return new HitInfo(selectedId, HitPart.ResizeHandle);
            }
        }

        foreach (var node in Enumerable.Reverse(model.Nodes).Where(n => !GraphRules.IsContainer(n.Kind)))
        {
            var bounds = NodeBounds(document, node);
            if (bounds.Contains(point) || NameBelowRect(document, node, bounds) is { } name && name.Contains(point))
            {
                return new HitInfo(node.Id, HitPart.Node);
            }
        }

        foreach (var edge in Enumerable.Reverse(model.Edges))
        {
            if (TryGetEdgeGeometry(document, edge, out var geometry) &&
                (geometry.DistanceTo(point) <= EdgeTolerance || LabelRect(document, edge, geometry)?.Contains(point) == true))
            {
                return new HitInfo(edge.Id, HitPart.Edge);
            }
        }

        // Containers last, innermost first. Deployment nodes can be connected; system boundaries cannot.
        foreach (var node in ContainersOuterFirst(document, model).Reverse())
        {
            if (NodeBounds(document, node).Contains(point))
            {
                return new HitInfo(node.Id, node.Kind == GraphNodeKind.SystemBoundary ? HitPart.Boundary : HitPart.Node);
            }
        }

        return null;
    }

    private protected override SKRect? GetDiagramEditRect(DiagramDocument document, string elementId, HitPart part, int index)
    {
        var model = (GraphDiagramModel)document.Model;
        if (model.FindNode(elementId) is { } node)
        {
            if (!GraphRules.HasName(node.Kind))
            {
                return null;
            }

            var bounds = NodeBounds(document, node);
            var font = FontOf(document, node.Id);
            if (NameBelowRect(document, node, bounds) is { } below)
            {
                return below.Width < 120f ? SKRect.Create(below.MidX - 60f, below.Top, 120f, below.Height) : below;
            }

            if (GraphRules.IsDeploymentTarget(node.Kind))
            {
                var front = FrontFace(bounds);
                var nameTop = front.Top + 4f + (GraphRules.Keyword(node.Kind) is null ? 0f : font.LineHeight);
                return SKRect.Create(front.Left + 4f, nameTop, front.Width - 8f, font.LineHeight);
            }

            var top = node.Kind == GraphNodeKind.SystemBoundary
                ? bounds.Top + 4f
                : bounds.MidY - font.LineHeight / 2f + (GraphRules.Keyword(node.Kind) is null ? 0f : font.LineHeight / 2f);
            return SKRect.Create(bounds.Left + 4f, top, bounds.Width - 8f, font.LineHeight);
        }

        if (model.FindEdge(elementId) is { } edge && TryGetEdgeGeometry(document, edge, out var geometry))
        {
            var mid = geometry.LabelAnchor;
            var font = FontOf(document, edge.Id);
            return SKRect.Create(mid.X - 80f, mid.Y - font.LineHeight - 3f, 160f, font.LineHeight);
        }

        return null;
    }

    private protected override SKRect GetDiagramContentBounds(DiagramDocument document)
    {
        var model = (GraphDiagramModel)document.Model;
        var bounds = SKRect.Empty;
        void Include(SKRect rect) => bounds = bounds.IsEmpty ? rect : SKRect.Union(bounds, rect);
        foreach (var node in model.Nodes)
        {
            var rect = NodeBounds(document, node);
            Include(rect);
            if (NameBelowRect(document, node, rect) is { } name)
            {
                Include(name);
            }
        }

        foreach (var edge in model.Edges)
        {
            if (TryGetEdgeGeometry(document, edge, out var geometry))
            {
                Include(geometry.Bounds);
                if (LabelRect(document, edge, geometry) is { } label)
                {
                    Include(label);
                }
            }
        }

        return bounds;
    }

    private protected override SKRect? GetDiagramElementBounds(DiagramDocument document, string elementId)
    {
        var model = (GraphDiagramModel)document.Model;
        if (model.FindNode(elementId) is { } node)
        {
            return NodeBounds(document, node);
        }

        return model.FindEdge(elementId) is { } edge && TryGetEdgeGeometry(document, edge, out var geometry)
            ? SKRect.Create(geometry.Middle.X, geometry.Middle.Y, 0, 0)
            : null;
    }

    #region Nodes

    private static SKRect NodeBounds(DiagramDocument document, GraphNode node)
    {
        var layout = document.Layout.Find(node.Id);
        var (width, height) = GraphRules.DefaultSize(node.Kind);
        return layout is null
            ? SKRect.Create(0, 0, (float)width, (float)height)
            : SKRect.Create((float)layout.X, (float)layout.Y, (float)layout.Width, (float)layout.Height);
    }

    private RenderResources.FontSet FontOf(DiagramDocument document, string id) => R.Font(document.Layout.FontFor(id));

    /// <summary>Actors and interfaces show their name under the node rather than inside it.</summary>
    private static bool NameIsBelow(GraphNodeKind kind) => kind is GraphNodeKind.Actor or GraphNodeKind.Interface;

    private SKRect? NameBelowRect(DiagramDocument document, GraphNode node, SKRect bounds)
    {
        if (!NameIsBelow(node.Kind) || string.IsNullOrEmpty(node.Name))
        {
            return null;
        }

        var font = FontOf(document, node.Id);
        var width = font.Text.MeasureText(node.Name) + 8f;
        return SKRect.Create(bounds.MidX - width / 2f, bounds.Bottom + 2f, width, font.LineHeight);
    }

    /// <summary>The size a named node needs for its name, used to grow nodes that are too small.</summary>
    private (double Width, double Height) MeasureName(DiagramDocument document, GraphNode node, ElementLayout layout)
    {
        if (!GraphRules.HasName(node.Kind) || NameIsBelow(node.Kind))
        {
            return (0, 0);
        }

        var font = FontOf(document, node.Id);
        var name = node.Name ?? string.Empty;
        var keyword = GraphRules.Keyword(node.Kind);

        // Containers show a heading (keyword and name) at the top; their height is set by the user.
        if (GraphRules.IsContainer(node.Kind))
        {
            var heading = Math.Max(font.BoldText.MeasureText(name), keyword is null ? 0f : font.SmallText.MeasureText(keyword));
            var depth = GraphRules.IsDeploymentTarget(node.Kind) ? BoxDepth : 0f;
            return (Math.Min(heading + 2 * Pad + depth, MaxAutoWidth), 0);
        }

        var extra = node.Kind switch
        {
            GraphNodeKind.UseCase => 1.4f, // an ellipse needs more room than a rectangle
            _ => 1f,
        };
        var iconWidth = node.Kind is GraphNodeKind.Component or GraphNodeKind.Artifact ? 24f : 0f;
        var width = Math.Min(font.BoldText.MeasureText(name) * extra + 2 * Pad + iconWidth, MaxAutoWidth);
        var lines = TextWrapper.Wrap(name, font.Text, (float)Math.Max(layout.Width, width) / extra - 2 * Pad).Count;
        var height = (lines + (keyword is null ? 0 : 1)) * font.LineHeight * (node.Kind == GraphNodeKind.UseCase ? 1.4f : 1f) + 2 * Pad;
        return (width, height);
    }

    /// <summary>The outline of a node, used to fill, stroke and select it.</summary>
    private static SKPath ShapePath(GraphNodeKind kind, SKRect bounds)
    {
        var path = new SKPath();
        switch (kind)
        {
            case GraphNodeKind.State:
                path.AddRoundRect(bounds, 10f, 10f);
                break;
            case GraphNodeKind.Action:
                path.AddRoundRect(bounds, 18f, 18f);
                break;
            case GraphNodeKind.InitialState or GraphNodeKind.InitialNode or GraphNodeKind.FinalState or GraphNodeKind.ActivityFinal or
                GraphNodeKind.Interface:
                path.AddOval(bounds);
                break;
            case GraphNodeKind.UseCase:
                path.AddOval(bounds);
                break;
            case GraphNodeKind.Node or GraphNodeKind.Device or GraphNodeKind.ExecutionEnvironment:
                path.MoveTo(bounds.Left, bounds.Top + BoxDepth);
                path.LineTo(bounds.Left + BoxDepth, bounds.Top);
                path.LineTo(bounds.Right, bounds.Top);
                path.LineTo(bounds.Right, bounds.Bottom - BoxDepth);
                path.LineTo(bounds.Right - BoxDepth, bounds.Bottom);
                path.LineTo(bounds.Left, bounds.Bottom);
                path.Close();
                break;
            case GraphNodeKind.Choice or GraphNodeKind.Decision:
                path.MoveTo(bounds.MidX, bounds.Top);
                path.LineTo(bounds.Right, bounds.MidY);
                path.LineTo(bounds.MidX, bounds.Bottom);
                path.LineTo(bounds.Left, bounds.MidY);
                path.Close();
                break;
            default:
                path.AddRect(bounds);
                break;
        }

        return path;
    }

    private void DrawNode(SKCanvas canvas, DiagramDocument document, GraphNode node, RenderOptions options)
    {
        var bounds = NodeBounds(document, node);
        var selected = options.SelectedId == node.Id;
        var highlighted = options.HighlightId == node.Id;
        var style = StyleOf(node, document.Layout.Find(node.Id)?.Style);
        var fill = R.FillOf(style, R.Theme.GraphNodeFill(node.Kind));
        var border = R.BorderOf(style);

        // Names inside the shape are drawn on its fill; names below it (actors, interfaces) on the canvas.
        var font = NameIsBelow(node.Kind) ? FontOf(document, node.Id) : R.Font(document.Layout.FontFor(node.Id), ShapeColors.CustomFill(style));
        using var shape = ShapePath(node.Kind, bounds);

        switch (node.Kind)
        {
            case GraphNodeKind.State or GraphNodeKind.Action or GraphNodeKind.UseCase or GraphNodeKind.Component or GraphNodeKind.Artifact:
            {
                using var shadow = new SKPath(shape);
                shadow.Offset(2f, 3f);
                canvas.DrawPath(shadow, R.Shadow);
                canvas.DrawPath(shape, fill);
                canvas.DrawPath(shape, border);
                if (GraphRules.Keyword(node.Kind) is { } keyword)
                {
                    if (node.Kind == GraphNodeKind.Artifact)
                    {
                        DrawArtifactIcon(canvas, bounds);
                    }
                    else
                    {
                        DrawComponentIcon(canvas, bounds);
                    }

                    font.DrawCenteredText(canvas, keyword, bounds.MidX, bounds.MidY - font.LineHeight, font.SmallText);
                    DrawName(canvas, node.Name, SKRect.Create(bounds.Left, bounds.MidY, bounds.Width, font.LineHeight), font, font.BoldText, 1f);
                }
                else
                {
                    DrawName(canvas, node.Name, bounds, font, font.Text, node.Kind == GraphNodeKind.UseCase ? 1.4f : 1f);
                }

                break;
            }

            case GraphNodeKind.SystemBoundary:
                canvas.DrawRect(bounds, fill);
                canvas.DrawRect(bounds, border);
                font.DrawCenteredText(canvas, node.Name ?? string.Empty, bounds.MidX, bounds.Top + 4f, font.BoldText);
                break;

            case GraphNodeKind.Node or GraphNodeKind.Device or GraphNodeKind.ExecutionEnvironment:
            {
                // A 3-D box: shaded top and right faces behind the front face, which holds the heading and contents.
                // A custom fill is shaded towards the border colour, as the theme's shade is.
                using var shadow = new SKPath(shape);
                shadow.Offset(2f, 3f);
                canvas.DrawPath(shadow, R.Shadow);
                var shade = ShapeColors.CustomFill(style) is { } custom ? ShapeColors.Blend(custom, R.Theme.Border, 0.25f) : R.Theme.DeploymentNodeShade;
                canvas.DrawPath(shape, R.FillOf(null, shade));
                var front = FrontFace(bounds);
                canvas.DrawRect(front, fill);
                canvas.DrawPath(shape, border);
                canvas.DrawRect(front, R.InnerLineOf(style));
                canvas.DrawLine(front.Right, front.Top, bounds.Right, bounds.Top, R.InnerLineOf(style));

                var top = front.Top + 4f;
                if (GraphRules.Keyword(node.Kind) is { } keyword)
                {
                    font.DrawCenteredText(canvas, keyword, front.MidX, top, font.SmallText);
                    top += font.LineHeight;
                }

                font.DrawCenteredText(canvas, node.Name ?? string.Empty, front.MidX, top, font.BoldText);
                break;
            }

            case GraphNodeKind.InitialState or GraphNodeKind.InitialNode:
                canvas.DrawPath(shape, fill);
                break;

            case GraphNodeKind.FinalState or GraphNodeKind.ActivityFinal:
            {
                // The ring takes the fill and border; the dot inside is drawn in the border colour.
                canvas.DrawPath(shape, fill);
                canvas.DrawPath(shape, border);
                var inner = bounds;
                inner.Inflate(-5f, -5f);
                canvas.DrawOval(inner, R.FillOf(null, ShapeColors.Parse(style?.Border) ?? R.Theme.Line));
                break;
            }

            case GraphNodeKind.Choice or GraphNodeKind.Decision or GraphNodeKind.Interface:
                canvas.DrawPath(shape, fill);
                canvas.DrawPath(shape, border);
                break;

            case GraphNodeKind.ForkJoin:
                canvas.DrawRect(bounds, fill);
                break;

            case GraphNodeKind.Actor:
                DrawActor(canvas, bounds, fill, border);
                break;
        }

        if (NameBelowRect(document, node, bounds) is { } below)
        {
            font.DrawCenteredText(canvas, node.Name!, below.MidX, below.Top, font.Text);
        }

        if (selected || highlighted)
        {
            var outline = selected ? R.SelectionBorder : R.HighlightBorder;
            if (node.Kind is GraphNodeKind.Actor or GraphNodeKind.ForkJoin)
            {
                var frame = bounds;
                frame.Inflate(3f, 3f);
                canvas.DrawRect(frame, outline);
            }
            else
            {
                canvas.DrawPath(shape, outline);
            }
        }

        if (selected && GraphRules.IsResizable(node.Kind))
        {
            canvas.DrawRect(HandleRect(bounds), R.HandleFill);
        }
    }

    /// <summary>Draws a name wrapped to the node width and centred in it.</summary>
    private static void DrawName(SKCanvas canvas, string? name, SKRect bounds, RenderResources.FontSet font, SKPaint paint, float widthFactor)
    {
        if (string.IsNullOrEmpty(name))
        {
            return;
        }

        var lines = TextWrapper.Wrap(name, paint, bounds.Width / widthFactor - 2 * Pad);
        var top = bounds.MidY - lines.Count * font.LineHeight / 2f;
        canvas.Save();
        canvas.ClipRect(bounds);
        foreach (var line in lines)
        {
            font.DrawCenteredText(canvas, line, bounds.MidX, top, paint);
            top += font.LineHeight;
        }

        canvas.Restore();
    }

    /// <summary>The front face of a deployment node's 3-D box, below the top face and left of the side face.</summary>
    private static SKRect FrontFace(SKRect bounds) =>
        new(bounds.Left, bounds.Top + BoxDepth, bounds.Right - BoxDepth, bounds.Bottom);

    /// <summary>
    /// Containers back to front: those nested in other containers are drawn above them, and containers at the same
    /// depth in their stacking (model) order.
    /// </summary>
    private static IEnumerable<GraphNode> ContainersOuterFirst(DiagramDocument document, GraphDiagramModel model)
    {
        var containers = model.Nodes.Where(n => GraphRules.IsContainer(n.Kind)).Select(n => (Node: n, Bounds: NodeBounds(document, n))).ToList();
        return containers
            .OrderBy(c => containers.Count(o => o.Node != c.Node && o.Bounds.Contains(c.Bounds)))
            .Select(c => c.Node);
    }

    /// <summary>The UML artifact icon: a page with a folded corner, in the top-right corner.</summary>
    private void DrawArtifactIcon(SKCanvas canvas, SKRect bounds)
    {
        var page = SKRect.Create(bounds.Right - 20f, bounds.Top + 6f, 12f, 16f);
        const float fold = 4f;
        using var path = new SKPath();
        path.MoveTo(page.Left, page.Top);
        path.LineTo(page.Right - fold, page.Top);
        path.LineTo(page.Right, page.Top + fold);
        path.LineTo(page.Right, page.Bottom);
        path.LineTo(page.Left, page.Bottom);
        path.Close();
        canvas.DrawPath(path, R.Fill);
        canvas.DrawPath(path, R.Border);
        canvas.DrawLine(page.Right - fold, page.Top, page.Right - fold, page.Top + fold, R.Border);
        canvas.DrawLine(page.Right - fold, page.Top + fold, page.Right, page.Top + fold, R.Border);
    }

    /// <summary>The UML component icon: a small rectangle with two tabs on its left side, in the top-right corner.</summary>
    private void DrawComponentIcon(SKCanvas canvas, SKRect bounds)
    {
        var body = SKRect.Create(bounds.Right - 22f, bounds.Top + 7f, 14f, 16f);
        canvas.DrawRect(body, R.Fill);
        canvas.DrawRect(body, R.Border);
        foreach (var y in new[] { body.Top + 3f, body.Top + 10f })
        {
            var tab = SKRect.Create(body.Left - 4f, y, 8f, 4f);
            canvas.DrawRect(tab, R.Fill);
            canvas.DrawRect(tab, R.Border);
        }
    }

    /// <summary>A stick figure: <paramref name="fill"/> is its head's, <paramref name="border"/> draws its lines.</summary>
    private static void DrawActor(SKCanvas canvas, SKRect bounds, SKPaint fill, SKPaint border)
    {
        var head = bounds.Width * 0.32f;
        var cx = bounds.MidX;
        var headCenter = new SKPoint(cx, bounds.Top + head);
        canvas.DrawCircle(headCenter, head, fill);
        canvas.DrawCircle(headCenter, head, border);
        var neck = bounds.Top + 2 * head;
        var hip = bounds.Top + bounds.Height * 0.66f;
        canvas.DrawLine(cx, neck, cx, hip, border);
        canvas.DrawLine(bounds.Left, neck + (hip - neck) * 0.3f, bounds.Right, neck + (hip - neck) * 0.3f, border);
        canvas.DrawLine(cx, hip, bounds.Left + 2f, bounds.Bottom, border);
        canvas.DrawLine(cx, hip, bounds.Right - 2f, bounds.Bottom, border);
    }

    /// <summary>Where the line from the centre of <paramref name="bounds"/> towards <paramref name="toward"/> leaves the node's shape.</summary>
    private static SKPoint ClipToShape(GraphNodeKind kind, SKRect bounds, SKPoint toward)
    {
        var center = Geometry.Center(bounds);
        var dx = toward.X - center.X;
        var dy = toward.Y - center.Y;
        if (MathF.Abs(dx) < 1e-4f && MathF.Abs(dy) < 1e-4f)
        {
            return center;
        }

        var (a, b) = (bounds.Width / 2f, bounds.Height / 2f);
        float? t = kind switch
        {
            GraphNodeKind.InitialState or GraphNodeKind.InitialNode or GraphNodeKind.FinalState or GraphNodeKind.ActivityFinal or
                GraphNodeKind.Interface or GraphNodeKind.UseCase => 1f / MathF.Sqrt(dx * dx / (a * a) + dy * dy / (b * b)),
            GraphNodeKind.Choice or GraphNodeKind.Decision => 1f / (MathF.Abs(dx) / a + MathF.Abs(dy) / b),
            _ => null,
        };
        return t is { } scale ? new SKPoint(center.X + dx * MathF.Min(1f, scale), center.Y + dy * MathF.Min(1f, scale)) : Geometry.ClipToRect(bounds, toward);
    }

    #endregion

    #region Edges

    /// <summary>
    /// The drawn path of an edge: a straight line between the node outlines, or a loop at the top-right corner of a
    /// node for a transition back to the same state.
    /// </summary>
    private sealed record EdgeGeometry(SKPoint Start, SKPoint End, SKPoint? Control1, SKPoint? Control2)
    {
        public bool IsLoop => Control1 is not null;

        public SKPoint Middle => IsLoop ? PointAt(0.5f) : new((Start.X + End.X) / 2f, (Start.Y + End.Y) / 2f);

        /// <summary>Where the label goes: above the middle of a line, to the right of a loop.</summary>
        public SKPoint LabelAnchor => IsLoop ? new SKPoint(Control2!.Value.X - 6f, Start.Y - LoopSize * 0.3f) : Middle;

        /// <summary>The direction the arrow head points in.</summary>
        public SKPoint Direction => IsLoop
            ? new SKPoint(End.X - Control2!.Value.X, End.Y - Control2.Value.Y)
            : new SKPoint(End.X - Start.X, End.Y - Start.Y);

        public SKRect Bounds => IsLoop
            ? new SKRect(Start.X, Control1!.Value.Y, Control2!.Value.X, End.Y)
            : new SKRect(MathF.Min(Start.X, End.X), MathF.Min(Start.Y, End.Y), MathF.Max(Start.X, End.X), MathF.Max(Start.Y, End.Y));

        public SKPoint PointAt(float t)
        {
            if (!IsLoop)
            {
                return new SKPoint(Start.X + (End.X - Start.X) * t, Start.Y + (End.Y - Start.Y) * t);
            }

            var (c1, c2) = (Control1!.Value, Control2!.Value);
            var u = 1 - t;
            return new SKPoint(
                u * u * u * Start.X + 3 * u * u * t * c1.X + 3 * u * t * t * c2.X + t * t * t * End.X,
                u * u * u * Start.Y + 3 * u * u * t * c1.Y + 3 * u * t * t * c2.Y + t * t * t * End.Y);
        }

        public float DistanceTo(SKPoint point)
        {
            if (!IsLoop)
            {
                return Geometry.DistanceToSegment(point, Start, End);
            }

            var best = float.MaxValue;
            var previous = Start;
            for (var i = 1; i <= 24; i++)
            {
                var next = PointAt(i / 24f);
                best = MathF.Min(best, Geometry.DistanceToSegment(point, previous, next));
                previous = next;
            }

            return best;
        }
    }

    private bool TryGetEdgeGeometry(DiagramDocument document, GraphEdge edge, out EdgeGeometry geometry)
    {
        var model = (GraphDiagramModel)document.Model;
        geometry = null!;
        if (model.FindNode(edge.SourceId) is not { } source || model.FindNode(edge.TargetId) is not { } target)
        {
            return false;
        }

        var from = NodeBounds(document, source);
        if (source.Id == target.Id)
        {
            var start = new SKPoint(from.Right - LoopSize * 0.6f, from.Top);
            var end = new SKPoint(from.Right, from.Top + LoopSize * 0.6f);
            geometry = new EdgeGeometry(start, end, new SKPoint(start.X, from.Top - LoopSize), new SKPoint(from.Right + LoopSize, end.Y));
            return true;
        }

        // Edges end outside the name under an actor or interface, so arrow heads are not hidden by it.
        from = WithNameBelow(document, source, from);
        var to = WithNameBelow(document, target, NodeBounds(document, target));
        geometry = new EdgeGeometry(
            ClipToShape(source.Kind, from, Geometry.Center(to)),
            ClipToShape(target.Kind, to, Geometry.Center(from)),
            null,
            null);
        return true;
    }

    /// <summary>The node's bounds extended down over its name, keeping the node's width so side-on lines still meet it.</summary>
    private SKRect WithNameBelow(DiagramDocument document, GraphNode node, SKRect bounds) =>
        NameBelowRect(document, node, bounds) is { } name ? new SKRect(bounds.Left, bounds.Top, bounds.Right, name.Bottom) : bounds;

    /// <summary>The text shown on an edge: its label, after the «include» or «extend» keyword.</summary>
    private static string? EdgeText(GraphEdge edge)
    {
        var keyword = ConnectorStyle.Of(edge.Kind).Keyword;
        return keyword is null ? edge.Label : string.IsNullOrEmpty(edge.Label) ? keyword : $"{keyword} {edge.Label}";
    }

    private SKRect? LabelRect(DiagramDocument document, GraphEdge edge, EdgeGeometry geometry)
    {
        if (EdgeText(edge) is not { Length: > 0 } text)
        {
            return null;
        }

        var font = FontOf(document, edge.Id);
        var width = font.Text.MeasureText(text) + 12f;
        var anchor = geometry.LabelAnchor;
        return geometry.IsLoop
            ? SKRect.Create(anchor.X, anchor.Y - font.LineHeight / 2f, width, font.LineHeight)
            : SKRect.Create(anchor.X - width / 2f, anchor.Y - font.LineHeight - 3f, width, font.LineHeight);
    }

    private void DrawEdge(SKCanvas canvas, DiagramDocument document, GraphEdge edge, EdgeGeometry geometry, bool selected)
    {
        var connector = ConnectorStyle.Of(edge.Kind);
        var (solid, dash) = R.LinesOf(document.Layout.Find(edge.Id)?.Line, selected);
        var stroke = connector.Dashed ? dash : solid;

        if (geometry.IsLoop)
        {
            using var path = new SKPath();
            path.MoveTo(geometry.Start);
            path.CubicTo(geometry.Control1!.Value, geometry.Control2!.Value, geometry.End);
            canvas.DrawPath(path, stroke);
        }
        else
        {
            canvas.DrawLine(geometry.Start, geometry.End, stroke);
        }

        DrawHead(canvas, connector.TargetHead, geometry.End, geometry.Direction, solid);
    }

    private void DrawEdgeLabel(SKCanvas canvas, DiagramDocument document, GraphEdge edge, EdgeGeometry geometry)
    {
        if (LabelRect(document, edge, geometry) is { } rect)
        {
            var font = FontOf(document, edge.Id);
            canvas.DrawRect(rect, R.LabelBackground);
            font.DrawCenteredText(canvas, EdgeText(edge)!, rect.MidX, rect.Top, font.Text);
        }
    }

    #endregion
}
