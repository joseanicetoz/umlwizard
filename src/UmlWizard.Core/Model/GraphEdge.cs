namespace UmlWizard.Core.Model;

/// <summary>
/// An edge between two nodes of a state machine, activity, use case or component diagram. Arrow heads point at the
/// target.
/// </summary>
public sealed class GraphEdge : ModelElement
{
    public GraphEdgeKind Kind { get; set; }

    public string SourceId { get; set; } = string.Empty;

    public string TargetId { get; set; } = string.Empty;

    /// <summary>
    /// The text on the edge: <c>trigger [guard] / effect</c> for a transition, <c>[guard]</c> for a control flow, a
    /// name for the other kinds. «include» and «extend» are shown on top of it.
    /// </summary>
    public string? Label { get; set; }
}
