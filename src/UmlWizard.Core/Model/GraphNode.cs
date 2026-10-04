namespace UmlWizard.Core.Model;

/// <summary>A node of a state machine, activity, use case or component diagram.</summary>
public sealed class GraphNode : ModelElement
{
    public GraphNodeKind Kind { get; set; }

    /// <summary>The name shown in or under the node; <c>null</c> for nodes without a name (e.g. an initial state).</summary>
    public string? Name { get; set; }
}
