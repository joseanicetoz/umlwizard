using System.Text;
using System.Text.Json.Nodes;
using UmlWizard.Core.Model;
using UmlWizard.Core.Rendering;

namespace UmlWizard.Ai;

/// <summary>
/// The tools the model may call to change a state machine, activity, use case, component or deployment diagram: nodes
/// and edges of the kinds the diagram type has (<see cref="GraphRules"/>), checked by the same rules as in the editor.
/// Nodes are referred to by name, and nodes without one by their kind and number, e.g. <c>decision#1</c> (see
/// <see cref="ElementReferences"/>). Containment (use cases in a system, artifacts on a node) is where a node is drawn:
/// <c>inside</c> puts a node in a container, and <see cref="GraphPlacement"/> places it there.
/// </summary>
public sealed class GraphDiagramTools(DiagramDocument document) : DiagramTools(document)
{
    private readonly GraphDiagramModel _model = (GraphDiagramModel)document.Model;

    /// <summary>The container each added or moved node goes in; <c>null</c> for none.</summary>
    public Dictionary<string, string?> Inside { get; } = [];

    private DiagramType Type => Document.DiagramType;

    private bool HasContainers => GraphRules.NodeKinds(Type).Any(GraphRules.IsContainer);

    public override IReadOnlyList<AiTool> Tools => DefinitionsFor(Type);

    public override string Guidance
    {
        get
        {
            var text = new StringBuilder($"""
                You can change this {TypeName(Type)} with your tools. Use them when the user asks for a change. Refer to
                nodes by the references listed below the diagram: a node's name, or for nodes without a name their kind and
                number, e.g. {Example(Type)}. A tool that adds a node tells you its reference.
                """);
            text.Append(Type switch
            {
                DiagramType.StateMachine => "\nLabel transitions trigger [guard] / effect, each part optional, e.g. pay [valid] / capture().",
                DiagramType.Activity => "\nLabel the flows that leave a decision with their guard in square brackets, e.g. [in stock]; a decision itself has no text.",
                DiagramType.UseCase => "\nPut use cases inside the system boundary with inside; actors stay outside it.",
                DiagramType.Deployment => "\nDeploy artifacts and execution environments on a node or device with inside; nodes can be nested.",
                _ => string.Empty,
            });
            return text.ToString();
        }
    }

    public override void Place() => GraphPlacement.Place(Document, Added, Inside);

    /// <summary>The references of the nodes, in the order of the diagram.</summary>
    public ElementReferences References() =>
        new(_model.Nodes.Select(n => (n.Id, GraphRules.HasName(n.Kind) ? n.Name : null, SnakeCase(n.Kind.ToString()))));

    public override string Outline()
    {
        var references = References();
        var containers = DiagramArranger.ContainerOf(Document, _model);
        foreach (var (id, container) in Inside)
        {
            if (container is null)
            {
                containers.Remove(id);
            }
            else
            {
                containers[id] = container;
            }
        }

        var text = new StringBuilder("Nodes (refer to them by these references):\n");
        foreach (var node in _model.Nodes)
        {
            text.Append("- ").Append(references[node.Id]).Append(" (").Append(SnakeCase(node.Kind.ToString())).Append(')');
            if (containers.TryGetValue(node.Id, out var container) && _model.FindNode(container) is not null)
            {
                text.Append(", inside ").Append(references[container]);
            }

            text.Append('\n');
        }

        if (_model.Nodes.Count == 0)
        {
            text.Append("(none)\n");
        }

        text.Append("Edges:\n");
        foreach (var edge in _model.Edges.Where(e => _model.FindNode(e.SourceId) is not null && _model.FindNode(e.TargetId) is not null))
        {
            text.Append("- ").Append(references[edge.SourceId]).Append(" -> ").Append(references[edge.TargetId])
                .Append(" (").Append(SnakeCase(edge.Kind.ToString())).Append(')');
            if (!string.IsNullOrWhiteSpace(edge.Label))
            {
                text.Append(": ").Append(edge.Label.Trim());
            }

            text.Append('\n');
        }

        if (_model.Edges.Count == 0)
        {
            text.Append("(none)\n");
        }

        return text.ToString();
    }

    /// <summary>The tools for a diagram type, with the node and edge kinds it has.</summary>
    public static IReadOnlyList<AiTool> DefinitionsFor(DiagramType type)
    {
        var nodeKinds = Kinds(GraphRules.NodeKinds(type));
        var edgeKinds = Kinds(GraphRules.EdgeKinds(type));
        var containers = GraphRules.NodeKinds(type).Any(GraphRules.IsContainer);
        var inside = containers
            ? """, "inside": {"type": "string", "description": "The container to put it in (system boundary, node, device or execution environment); empty for none."}"""
            : string.Empty;
        var defaultEdge = GraphRules.EdgeKinds(type).Count == 1 ? $" Default: {SnakeCase(GraphRules.EdgeKinds(type)[0].ToString())}." : string.Empty;
        List<AiTool> tools =
        [
            Tool("add_node", "Adds a node. Initial and final nodes, decisions, choices and fork/join bars have no name.", $$"""
                {"type": "object", "properties": {
                  "kind": {"type": "string", "enum": {{nodeKinds}}},
                  "name": {"type": "string", "description": "The name, for the kinds that have one."}{{inside}}},
                 "required": ["kind"]}
                """),
            Tool("rename_node", "Renames a node.", """
                {"type": "object", "properties": {"node": {"type": "string"}, "new_name": {"type": "string"}}, "required": ["node", "new_name"]}
                """),
            Tool("delete_node", "Deletes a node with its edges and the notes' links to it. What a container holds stays.", """
                {"type": "object", "properties": {"node": {"type": "string"}}, "required": ["node"]}
                """),
        ];
        if (containers)
        {
            tools.Add(Tool("move_node", "Moves a node into a container, or out of the one it is in.", """
                {"type": "object", "properties": {"node": {"type": "string"}, "inside": {"type": "string", "description": "The container; empty to take it out."}}, "required": ["node"]}
                """));
        }

        tools.Add(Tool("add_edge", "Adds an edge from source to target.", $$"""
            {"type": "object", "properties": {
              "source": {"type": "string"}, "target": {"type": "string"},
              "kind": {"type": "string", "enum": {{edgeKinds}}, "description": "The kind of edge.{{defaultEdge}}"},
              "label": {"type": "string"} },
             "required": ["source", "target"]}
            """));
        tools.Add(Tool("set_edge_label", "Changes the label of the edge from source to target.", $$"""
            {"type": "object", "properties": {
              "source": {"type": "string"}, "target": {"type": "string"}, "label": {"type": "string", "description": "Empty to remove it."},
              "kind": {"type": "string", "enum": {{edgeKinds}}} },
             "required": ["source", "target"]}
            """));
        tools.Add(Tool("remove_edge", "Removes the edges from source to target (of a kind, if given).", $$"""
            {"type": "object", "properties": {"source": {"type": "string"}, "target": {"type": "string"}, "kind": {"type": "string", "enum": {{edgeKinds}}} }, "required": ["source", "target"]}
            """));
        tools.Add(NoteTool("node"));
        return tools;
    }

    protected override string? Execute(string name, JsonObject args) => name switch
    {
        "add_node" => AddNode(args),
        "rename_node" => RenameNode(args),
        "delete_node" => DeleteNode(args),
        "move_node" when HasContainers => MoveNode(args),
        "add_edge" => AddEdge(args),
        "set_edge_label" => SetEdgeLabel(args),
        "remove_edge" => RemoveEdge(args),
        "add_note" => AddNote(args),
        _ => null,
    };

    private string AddNode(JsonObject args)
    {
        var kind = EnumValue(Required(args, "kind"), GraphRules.NodeKinds(Type), "kind");
        var container = Optional(args, "inside") is { } inside ? Container(inside, kind) : null;
        string? name = null;
        if (GraphRules.HasName(kind))
        {
            name = Optional(args, "name") ?? NumberedName(kind);
            if (_model.Nodes.Any(n => n.Kind == kind && n.Name == name))
            {
                throw new ToolException($"There is already a {Word(kind)} named {name}.");
            }
        }

        var node = new GraphNode { Kind = kind, Name = name };
        _model.Nodes.Add(node);
        var (width, height) = GraphRules.DefaultSize(kind);
        Document.Layout.Add(new ElementLayout { ElementId = node.Id, Width = width, Height = height });
        Added.Add(node.Id);
        Inside[node.Id] = container?.Id;
        var reference = References()[node.Id];
        return $"Added {Word(kind)} {reference}" + (container is null ? "." : $", inside {References()[container.Id]}.");
    }

    private string RenameNode(JsonObject args)
    {
        var node = Node(Required(args, "node"));
        var name = Required(args, "new_name");
        if (!GraphRules.HasName(node.Kind))
        {
            throw new ToolException($"A {Word(node.Kind)} has no name.");
        }

        if (_model.Nodes.Any(n => n != node && n.Kind == node.Kind && n.Name == name))
        {
            throw new ToolException($"There is already a {Word(node.Kind)} named {name}.");
        }

        var old = node.Name;
        node.Name = name;
        return $"Renamed {old} to {name}.";
    }

    private string DeleteNode(JsonObject args)
    {
        var node = Node(Required(args, "node"));
        var reference = References()[node.Id];
        foreach (var id in _model.RemoveElement(node.Id))
        {
            Document.Layout.Remove(id);
        }

        Added.Remove(node.Id);
        Inside.Remove(node.Id);
        foreach (var held in Inside.Where(p => p.Value == node.Id).Select(p => p.Key).ToList())
        {
            Inside[held] = null;
        }

        return $"Deleted {reference}. The references of the other nodes may have changed; see the list.";
    }

    private string MoveNode(JsonObject args)
    {
        var node = Node(Required(args, "node"));
        if (GraphRules.IsContainer(node.Kind) && node.Kind != GraphNodeKind.ExecutionEnvironment)
        {
            throw new ToolException("Only what is deployed (artifacts, components, execution environments) or use cases and actors can be moved.");
        }

        var container = Optional(args, "inside") is { } inside ? Container(inside, node.Kind) : null;
        if (container == node)
        {
            throw new ToolException("A node cannot be inside itself.");
        }

        Inside[node.Id] = container?.Id;
        if (!Added.Contains(node.Id))
        {
            Added.Add(node.Id);
        }

        var references = References();
        return container is null ? $"Moved {references[node.Id]} out of its container." : $"Moved {references[node.Id]} into {references[container.Id]}.";
    }

    private string AddEdge(JsonObject args)
    {
        var source = Node(Required(args, "source"));
        var target = Node(Required(args, "target"));
        var kind = EdgeKind(args, required: GraphRules.EdgeKinds(Type).Count > 1) ?? GraphRules.EdgeKinds(Type)[0];
        if (GraphRules.CheckEdge(Type, kind, source, target) is { } problem)
        {
            throw new ToolException(problem);
        }

        var label = Optional(args, "label");
        if (_model.Edges.Any(e => e.SourceId == source.Id && e.TargetId == target.Id && e.Kind == kind && (e.Label ?? string.Empty) == (label ?? string.Empty)))
        {
            throw new ToolException("That edge is already there.");
        }

        _model.Edges.Add(new GraphEdge { Kind = kind, SourceId = source.Id, TargetId = target.Id, Label = label });
        var references = References();
        return $"Added a {Word(kind)} from {references[source.Id]} to {references[target.Id]}.";
    }

    private string SetEdgeLabel(JsonObject args)
    {
        var edges = Edges(args);
        if (edges.Count > 1)
        {
            throw new ToolException("There is more than one such edge: remove them and add the ones you want.");
        }

        edges[0].Label = Optional(args, "label");
        return edges[0].Label is null ? "Removed the label." : $"The label is now {edges[0].Label}.";
    }

    private string RemoveEdge(JsonObject args)
    {
        var edges = Edges(args);
        foreach (var id in edges.SelectMany(e => _model.RemoveElement(e.Id)))
        {
            Document.Layout.Remove(id);
        }

        return $"Removed {edges.Count} edge(s).";
    }

    private string AddNote(JsonObject args)
    {
        var target = Optional(args, "attach_to") is { } name ? Node(name) : null;
        return AddNote(Required(args, "text"), target, target is null ? null : References()[target.Id]);
    }

    /// <summary>The edges between source and target, either way, of a kind if given; at least one.</summary>
    private List<GraphEdge> Edges(JsonObject args)
    {
        var source = Node(Required(args, "source"));
        var target = Node(Required(args, "target"));
        var kind = EdgeKind(args, required: false);
        var edges = _model.Edges
            .Where(e => ((e.SourceId == source.Id && e.TargetId == target.Id) || (e.SourceId == target.Id && e.TargetId == source.Id))
                        && (kind is null || e.Kind == kind))
            .ToList();
        return edges.Count > 0 ? edges : throw new ToolException("There is no such edge.");
    }

    private GraphEdgeKind? EdgeKind(JsonObject args, bool required) =>
        Optional(args, "kind") is { } kind ? EnumValue(kind, GraphRules.EdgeKinds(Type), "edge kind")
        : required ? throw new ToolException($"The argument kind is missing; use {string.Join(", ", GraphRules.EdgeKinds(Type).Select(k => SnakeCase(k.ToString())))}.")
        : null;

    private GraphNode Node(string reference) =>
        References().Find(reference, out var problem) is { } id ? _model.FindNode(id)! : throw new ToolException(problem!);

    /// <summary>The container a node of <paramref name="kind"/> can go in.</summary>
    private GraphNode Container(string reference, GraphNodeKind kind)
    {
        var container = Node(reference);
        if (!GraphRules.IsContainer(container.Kind))
        {
            throw new ToolException($"{reference} is a {Word(container.Kind)}, which holds nothing.");
        }

        if (kind == GraphNodeKind.SystemBoundary || (container.Kind == GraphNodeKind.SystemBoundary) != (kind is GraphNodeKind.UseCase or GraphNodeKind.Actor))
        {
            throw new ToolException($"A {Word(kind)} cannot go inside a {Word(container.Kind)}.");
        }

        return container;
    }

    private string NumberedName(GraphNodeKind kind)
    {
        var name = GraphRules.DefaultName(kind);
        var n = 1;
        while (_model.Nodes.Any(x => x.Name == $"{name}{n}"))
        {
            n++;
        }

        return $"{name}{n}";
    }

    private static string Kinds<T>(IEnumerable<T> kinds) => "[" + string.Join(", ", kinds.Select(k => $"\"{SnakeCase(k!.ToString()!)}\"")) + "]";

    private static string Word<T>(T kind) => SnakeCase(kind!.ToString()!).Replace('_', ' ');

    private static string TypeName(DiagramType type) => type switch
    {
        DiagramType.StateMachine => "state machine diagram",
        DiagramType.Activity => "activity diagram",
        DiagramType.UseCase => "use case diagram",
        DiagramType.Component => "component diagram",
        _ => "deployment diagram",
    };

    private static string Example(DiagramType type) => type switch
    {
        DiagramType.StateMachine => "initial_state#1 or choice#2",
        DiagramType.Activity => "initial_node#1 or decision#2",
        _ => "Customer",
    };
}
