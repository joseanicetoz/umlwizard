using UmlWizard.Core.Model;

namespace UmlWizard.Core.Rendering;

/// <summary>
/// Places the nodes of a state machine, activity, use case, component or deployment diagram (an import, or the Arrange
/// command) in layers that follow the edges, with <see cref="LayeredLayout"/>: each node comes after the nodes it has
/// edges from, so flows read from top to bottom (or from left to right). Nodes inside a container (a system boundary, a
/// deployment node) are laid out in it first, and the container is sized around them, so it contains them as the editor
/// sees containment.
/// </summary>
public static class GraphDiagramLayout
{
    private const double Margin = 40;
    private const double Padding = 20;

    /// <summary>Arranges the diagram's nodes.</summary>
    /// <param name="parentOf">The container each node is in; nodes that are not in one are left out.</param>
    /// <param name="leftToRight">Layers run from left to right (e.g. actors, then the system), not from top to bottom.</param>
    /// <param name="packParts">Groups of nodes that no edge connects are laid out separately and packed in rows, rather
    /// than side by side in the same layers (top to bottom layouts only).</param>
    public static void Arrange(
        DiagramDocument document, IReadOnlyDictionary<string, string> parentOf, bool leftToRight = false, bool packParts = false)
    {
        var model = (GraphDiagramModel)document.Model;
        using (var renderer = DiagramRenderer.Create(document.DiagramType))
        {
            // The renderer gives every node a layout, grown to fit its name.
            renderer.UpdateLayout(document);
        }

        // The container of each node, the diagram itself ("") for the nodes that are not in one.
        var containerOf = model.Nodes.ToDictionary(n => n.Id, n => parentOf.TryGetValue(n.Id, out var parent) && model.FindNode(parent) is not null ? parent : string.Empty);
        var children = model.Nodes.ToLookup(n => containerOf[n.Id]);
        var positions = new Dictionary<string, (double X, double Y)>();
        Place(document, model, children, containerOf, string.Empty, leftToRight, packParts && !leftToRight, positions);

        // Positions are relative to their container's content: make them absolute, outermost first.
        Offset(string.Empty, Margin, Margin);

        void Offset(string container, double x, double y)
        {
            foreach (var node in children[container])
            {
                var (relativeX, relativeY) = positions[node.Id];
                var layout = document.Layout.Find(node.Id)!;
                layout.X = x + relativeX;
                layout.Y = y + relativeY;
                if (GraphRules.IsContainer(node.Kind))
                {
                    var (left, top) = ContentOffset(node.Kind);
                    Offset(node.Id, layout.X + left, layout.Y + top);
                }
            }
        }
    }

    /// <summary>
    /// Lays out the nodes directly in <paramref name="container"/> (the diagram for an empty id), relative to its
    /// content's top-left corner, and returns the size of the content.
    /// </summary>
    private static (double Width, double Height) Place(
        DiagramDocument document, GraphDiagramModel model, ILookup<string, GraphNode> children, Dictionary<string, string> containerOf,
        string container, bool leftToRight, bool packParts, Dictionary<string, (double X, double Y)> positions)
    {
        var nodes = children[container].ToList();
        if (nodes.Count == 0)
        {
            return (0, 0);
        }

        // Nested containers first: their size depends on what they hold.
        foreach (var node in nodes.Where(n => GraphRules.IsContainer(n.Kind)))
        {
            var (contentWidth, contentHeight) = Place(document, model, children, containerOf, node.Id, leftToRight, packParts, positions);
            var layout = document.Layout.Find(node.Id)!;
            var (left, top) = ContentOffset(node.Kind);
            layout.Width = Math.Max(layout.Width, contentWidth + left + Padding + (GraphRules.IsDeploymentTarget(node.Kind) ? 10 : 0));
            layout.Height = Math.Max(contentHeight + top + Padding, GraphRules.MinimumSize(node.Kind).Height);
            if (contentWidth == 0)
            {
                (layout.Width, layout.Height) = (Math.Max(layout.Width, 160), Math.Max(layout.Height, 80));
            }
        }

        var size = nodes.ToDictionary(n => n.Id, n => document.Layout.Find(n.Id) is { } l ? (l.Width, l.Height) : GraphRules.DefaultSize(n.Kind));

        // The edges between these nodes, including those between nodes nested in them: an edge that ends inside a
        // container here counts as ending at the container.
        var member = nodes.Select(n => n.Id).ToHashSet();
        string? Direct(string id)
        {
            var current = id;
            while (!member.Contains(current))
            {
                if (!containerOf.TryGetValue(current, out var parent) || parent.Length == 0)
                {
                    return null;
                }

                current = parent;
            }

            return current;
        }

        var edges = model.Edges
            .Select(e => (Source: Direct(e.SourceId), Target: Direct(e.TargetId)))
            .Where(e => e.Source is not null && e.Target is not null && e.Source != e.Target)
            .Select(e => (Source: e.Source!, Target: e.Target!))
            .Distinct()
            .ToList();

        // An edge from a node inside a container leaves the container where that node is, already laid out in it.
        var ports = new Dictionary<(string Node, string Other), double>();
        foreach (var edge in model.Edges)
        {
            var (source, target) = (Direct(edge.SourceId), Direct(edge.TargetId));
            if (source is null || target is null || source == target)
            {
                continue;
            }

            if (source != edge.SourceId)
            {
                ports.TryAdd((source, target), Port(source, edge.SourceId));
            }

            if (target != edge.TargetId)
            {
                ports.TryAdd((target, source), Port(target, edge.TargetId));
            }
        }

        // Where the centre of a node nested in a member of this level is along the member, from -0.5 to 0.5.
        double Port(string member, string inner)
        {
            var layout = document.Layout.Find(inner)!;
            var along = (leftToRight ? layout.Height : layout.Width) / 2;
            for (var current = inner; current != member; current = containerOf[current])
            {
                var offset = ContentOffset(model.FindNode(containerOf[current])!.Kind);
                along += leftToRight ? positions[current].Y + offset.Top : positions[current].X + offset.Left;
            }

            var outer = document.Layout.Find(member)!;
            return Math.Clamp(along / (leftToRight ? outer.Height : outer.Width) - 0.5, -0.5, 0.5);
        }

        // In a system, each use case has a row of its own, so that the lines from the actors outside reach every one
        // of them without crossing another.
        var rowEach = model.FindNode(container) is { Kind: GraphNodeKind.SystemBoundary };
        var ids = nodes.Select(n => n.Id).ToList();
        var parts = packParts ? LayeredLayout.ConnectedParts(ids, edges) : [ids];
        var placed = new List<Dictionary<string, (double X, double Y)>>();
        foreach (var part in parts)
        {
            var inPart = part.ToHashSet();
            var partEdges = edges.Where(e => inPart.Contains(e.Source)).ToList();
            var (partPositions, layers) = LayeredLayout.Place(part, size, partEdges, leftToRight, rowEach, ports: ports);
            FitForkJoinBars(document, model, layers, partEdges, partPositions, size, leftToRight);
            placed.Add(partPositions);
        }

        foreach (var (id, position) in placed.Count == 1 ? placed[0] : LayeredLayout.Pack(placed, size))
        {
            positions[id] = position;
        }

        var width = nodes.Max(n => positions[n.Id].X + size[n.Id].Item1);
        var height = nodes.Max(n => positions[n.Id].Y + size[n.Id].Item2);
        return (width, height);
    }

    /// <summary>Widens a fork or join bar to span the nodes it splits into or joins.</summary>
    private static void FitForkJoinBars(
        DiagramDocument document, GraphDiagramModel model, List<List<string>> layers, List<(string Source, string Target)> edges,
        Dictionary<string, (double X, double Y)> positions, Dictionary<string, (double Width, double Height)> size, bool leftToRight)
    {
        if (leftToRight)
        {
            return;
        }

        foreach (var bar in layers.SelectMany(l => l).Where(id => model.FindNode(id)?.Kind == GraphNodeKind.ForkJoin))
        {
            var others = edges.Where(e => e.Source == bar).Select(e => e.Target)
                .Concat(edges.Where(e => e.Target == bar).Select(e => e.Source))
                .Where(positions.ContainsKey)
                .Select(id => (Position: positions[id], Layout: document.Layout.Find(id)!))
                .ToList();
            if (others.Count < 2)
            {
                continue;
            }

            var left = others.Min(o => o.Position.X) - 20;
            var right = others.Max(o => o.Position.X + o.Layout.Width) + 20;
            var layout = document.Layout.Find(bar)!;
            if (right - left > layout.Width)
            {
                positions[bar] = (left, positions[bar].Y);
                layout.Width = right - left;
            }
        }
    }

    /// <summary>Where a container's content starts: below its heading, and inside its 3-D box for deployment nodes.</summary>
    private static (double Left, double Top) ContentOffset(GraphNodeKind kind) =>
        kind == GraphNodeKind.SystemBoundary ? (Padding, 36)
        : GraphRules.Keyword(kind) is null ? (Padding, 46)
        : (Padding, 62);
}
