using UmlWizard.Core.Model;

namespace UmlWizard.Core.Rendering;

/// <summary>What is drawn at an end of a connector.</summary>
public enum ConnectorHead
{
    None,

    /// <summary>Two strokes: a directed association, a dependency, an asynchronous message, a transition.</summary>
    OpenArrow,

    /// <summary>A filled triangle: a synchronous message.</summary>
    FilledArrow,

    /// <summary>A hollow triangle: inheritance, generalization, realization.</summary>
    HollowTriangle,

    /// <summary>A hollow diamond at the whole: aggregation.</summary>
    HollowDiamond,

    /// <summary>A filled diamond at the whole: composition.</summary>
    FilledDiamond,
}

/// <summary>
/// How a kind of connector is drawn: a solid or dashed line, what is drawn at each end, and the keyword shown on it
/// (<c>«include»</c>). The one description the canvas and the drawing toolbar's icons both follow, so that they always
/// agree. Arrow heads sit at the target; diamonds at the source, the whole.
/// </summary>
public sealed record ConnectorStyle(bool Dashed, ConnectorHead SourceHead, ConnectorHead TargetHead, string? Keyword = null)
{
    public static ConnectorStyle Of(RelationshipKind kind) => kind switch
    {
        RelationshipKind.DirectedAssociation => new(false, ConnectorHead.None, ConnectorHead.OpenArrow),
        RelationshipKind.Inheritance => new(false, ConnectorHead.None, ConnectorHead.HollowTriangle),
        RelationshipKind.Realization => new(true, ConnectorHead.None, ConnectorHead.HollowTriangle),
        RelationshipKind.Aggregation => new(false, ConnectorHead.HollowDiamond, ConnectorHead.None),
        RelationshipKind.Composition => new(false, ConnectorHead.FilledDiamond, ConnectorHead.None),
        RelationshipKind.Dependency => new(true, ConnectorHead.None, ConnectorHead.OpenArrow),
        _ => new(false, ConnectorHead.None, ConnectorHead.None),
    };

    public static ConnectorStyle Of(MessageKind kind) => kind switch
    {
        MessageKind.Asynchronous => new(false, ConnectorHead.None, ConnectorHead.OpenArrow),
        MessageKind.Reply => new(true, ConnectorHead.None, ConnectorHead.OpenArrow),
        MessageKind.Create => new(true, ConnectorHead.None, ConnectorHead.OpenArrow, "«create»"),
        _ => new(false, ConnectorHead.None, ConnectorHead.FilledArrow),
    };

    public static ConnectorStyle Of(GraphEdgeKind kind) => kind switch
    {
        GraphEdgeKind.Transition or GraphEdgeKind.ControlFlow => new(false, ConnectorHead.None, ConnectorHead.OpenArrow),
        GraphEdgeKind.Include => new(true, ConnectorHead.None, ConnectorHead.OpenArrow, "«include»"),
        GraphEdgeKind.Extend => new(true, ConnectorHead.None, ConnectorHead.OpenArrow, "«extend»"),
        GraphEdgeKind.Generalization => new(false, ConnectorHead.None, ConnectorHead.HollowTriangle),
        GraphEdgeKind.Dependency => new(true, ConnectorHead.None, ConnectorHead.OpenArrow),
        GraphEdgeKind.Realization => new(true, ConnectorHead.None, ConnectorHead.HollowTriangle),
        GraphEdgeKind.Deploy => new(true, ConnectorHead.None, ConnectorHead.OpenArrow, "«deploy»"),
        GraphEdgeKind.Manifest => new(true, ConnectorHead.None, ConnectorHead.OpenArrow, "«manifest»"),
        _ => new(false, ConnectorHead.None, ConnectorHead.None),
    };
}
