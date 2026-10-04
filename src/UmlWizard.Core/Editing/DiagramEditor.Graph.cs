using UmlWizard.Core.Model;

namespace UmlWizard.Core.Editing;

/// <summary>Editing of state machine, activity, use case and component diagrams.</summary>
public sealed partial class DiagramEditor
{
    private GraphDiagramModel GraphModel =>
        Model as GraphDiagramModel ?? throw new InvalidOperationException("The document is not a node-and-edge diagram.");

    /// <summary>Adds a node centred on (<paramref name="x"/>, <paramref name="y"/>) as one undoable change.</summary>
    public GraphNode AddNode(GraphNodeKind kind, double x, double y)
    {
        if (!GraphRules.NodeKinds(Document.DiagramType).Contains(kind))
        {
            throw new ArgumentException($"A {kind} does not belong in a {Document.DiagramType} diagram.", nameof(kind));
        }

        var node = new GraphNode
        {
            Kind = kind,
            Name = GraphRules.HasName(kind)
                ? UniqueName(GraphRules.DefaultName(kind), GraphModel.Nodes.Select(n => n.Name ?? string.Empty), alwaysNumber: kind != GraphNodeKind.SystemBoundary)
                : null,
        };
        var (width, height) = GraphRules.DefaultSize(kind);
        Execute(() =>
        {
            GraphModel.Nodes.Add(node);
            Layout.Add(new ElementLayout { ElementId = node.Id, X = x - width / 2, Y = y - height / 2, Width = width, Height = height });
        });
        SelectedId = node.Id;
        return node;
    }

    /// <summary>Why an edge of <paramref name="kind"/> cannot connect the nodes, or <c>null</c> when it can.</summary>
    public string? CheckEdge(GraphEdgeKind kind, string sourceId, string targetId) =>
        Model is GraphDiagramModel model && model.FindNode(sourceId) is { } source && model.FindNode(targetId) is { } target
            ? GraphRules.CheckEdge(Document.DiagramType, kind, source, target)
            : "Connect two nodes of the diagram.";

    /// <summary>Connects two nodes as one undoable change; <c>null</c> when <see cref="CheckEdge"/> rejects it.</summary>
    public GraphEdge? AddEdge(GraphEdgeKind kind, string sourceId, string targetId)
    {
        if (CheckEdge(kind, sourceId, targetId) is not null)
        {
            return null;
        }

        var edge = new GraphEdge { Kind = kind, SourceId = sourceId, TargetId = targetId };
        Execute(() => GraphModel.Edges.Add(edge));
        SelectedId = edge.Id;
        return edge;
    }

    /// <summary>
    /// Moves a node; a container (system boundary, deployment node, device or execution environment) takes the nodes
    /// that lie completely inside it along, including nested containers and their contents. Used while dragging, so no
    /// undo step is recorded here.
    /// </summary>
    private void MoveGraphNode(GraphNode node, ElementLayout layout, double x, double y)
    {
        var (dx, dy) = (x - layout.X, y - layout.Y);
        if (GraphRules.IsContainer(node.Kind))
        {
            foreach (var inner in GraphModel.Nodes.Where(n => n.Id != node.Id && n.Kind != GraphNodeKind.SystemBoundary))
            {
                if (Layout.Find(inner.Id) is { } innerLayout &&
                    innerLayout.X >= layout.X && innerLayout.Y >= layout.Y &&
                    innerLayout.X + innerLayout.Width <= layout.X + layout.Width &&
                    innerLayout.Y + innerLayout.Height <= layout.Y + layout.Height)
                {
                    innerLayout.X += dx;
                    innerLayout.Y += dy;
                }
            }
        }

        layout.X = x;
        layout.Y = y;
    }

    private static void ResizeGraphNode(GraphNode node, ElementLayout layout, double width, double? height)
    {
        if (!GraphRules.IsResizable(node.Kind))
        {
            return;
        }

        var (minWidth, minHeight) = GraphRules.MinimumSize(node.Kind);
        layout.Width = Math.Max(minWidth, width);
        layout.Height = Math.Max(minHeight, height ?? layout.Height);
    }
}
