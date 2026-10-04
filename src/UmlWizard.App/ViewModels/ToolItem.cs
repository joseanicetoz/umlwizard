using Material.Icons;
using UmlWizard.App.Resources;
using UmlWizard.Core.Model;
using UmlWizard.Core.Rendering;

namespace UmlWizard.App.ViewModels;

/// <summary>An entry of the diagram toolbox.</summary>
public sealed record ToolItem(ToolKind Kind, string Label, string ToolTip)
{
    /// <summary>The node a <see cref="ToolKind.GraphNode"/> tool adds.</summary>
    public GraphNodeKind? NodeKind { get; init; }

    /// <summary>The edge a <see cref="ToolKind.GraphEdge"/> tool draws.</summary>
    public GraphEdgeKind? EdgeKind { get; init; }

    public bool IsNodeTool => Kind is ToolKind.Class or ToolKind.AbstractClass or ToolKind.Interface or ToolKind.Enumeration
        or ToolKind.Lifeline or ToolKind.Actor or ToolKind.Note or ToolKind.TextBox or ToolKind.GraphNode;

    public bool IsConnectorTool => Kind is not (ToolKind.Select or ToolKind.Separator) && !IsNodeTool;

    public bool IsSeparator => Kind == ToolKind.Separator;

    /// <summary>Shown instead of <see cref="Label"/> when set; the tooltip then names the tool.</summary>
    public MaterialIconKind? Icon { get; init; }

    public bool HasIcon => Icon is not null;

    /// <summary><see cref="Icon"/> for binding; only shown when <see cref="HasIcon"/>.</summary>
    public MaterialIconKind IconKind => Icon ?? default;

    /// <summary>
    /// How the connector a connector tool draws looks, as the canvas draws it; the tool's icon is drawn from it. Null for
    /// the other tools.
    /// </summary>
    public ConnectorStyle? Connector => Kind switch
    {
        ToolKind.Association => ConnectorStyle.Of(RelationshipKind.Association),
        ToolKind.DirectedAssociation => ConnectorStyle.Of(RelationshipKind.DirectedAssociation),
        ToolKind.Inheritance => ConnectorStyle.Of(RelationshipKind.Inheritance),
        ToolKind.Realization => ConnectorStyle.Of(RelationshipKind.Realization),
        ToolKind.Aggregation => ConnectorStyle.Of(RelationshipKind.Aggregation),
        ToolKind.Composition => ConnectorStyle.Of(RelationshipKind.Composition),
        ToolKind.Dependency => ConnectorStyle.Of(RelationshipKind.Dependency),
        ToolKind.SyncMessage => ConnectorStyle.Of(MessageKind.Synchronous),
        ToolKind.AsyncMessage => ConnectorStyle.Of(MessageKind.Asynchronous),
        ToolKind.ReplyMessage => ConnectorStyle.Of(MessageKind.Reply),
        ToolKind.CreateMessage => ConnectorStyle.Of(MessageKind.Create),
        ToolKind.GraphEdge when EdgeKind is { } edge => ConnectorStyle.Of(edge),
        _ => null,
    };

    public bool HasConnector => Connector is not null;

    /// <summary>The tool shows its <see cref="Label"/>: it is neither a divider nor drawn as an icon.</summary>
    public bool ShowsLabel => !IsSeparator && !HasIcon && !HasConnector;

    /// <summary>A divider in the toolbox; it cannot be selected.</summary>
    public static ToolItem Separator { get; } = new(ToolKind.Separator, string.Empty, string.Empty);

    /// <summary>The first tool of every diagram type, drawn as a mouse pointer.</summary>
    private static readonly ToolItem SelectTool =
        new(ToolKind.Select, Strings.Tool_Select, Strings.Tool_Select_Tip) { Icon = MaterialIconKind.CursorDefaultOutline };

    /// <summary>The tools every diagram type ends with, after a divider: notes, note attachments and text boxes.</summary>
    private static readonly ToolItem[] AnnotationTools =
    [
        Separator,
        new(ToolKind.Note, Strings.Tool_Note, Strings.Tool_Note_Tip) { Icon = MaterialIconKind.StickerTextOutline },
        new(ToolKind.NoteAttachment, Strings.Tool_AttachNote, Strings.Tool_AttachNote_Tip)
        {
            Icon = MaterialIconKind.VectorLine,
        },
        new(ToolKind.TextBox, Strings.Tool_TextBox, Strings.Tool_TextBox_Tip) { Icon = MaterialIconKind.FormTextbox },
    ];

    public static IReadOnlyList<ToolItem> For(DiagramType type) => type switch
    {
        DiagramType.Class =>
        [
            SelectTool,
            new(ToolKind.Class, Strings.Tool_Class, Strings.Tool_Class_Tip),
            new(ToolKind.AbstractClass, Strings.Tool_AbstractClass, Strings.Tool_AbstractClass_Tip),
            new(ToolKind.Interface, Strings.Tool_Interface, Strings.Tool_Interface_Tip),
            new(ToolKind.Enumeration, Strings.Tool_Enumeration, Strings.Tool_Enumeration_Tip),
            new(ToolKind.Association, Strings.Tool_Association, Strings.Tool_Association_Tip),
            new(ToolKind.DirectedAssociation, Strings.Tool_DirectedAssociation, Strings.Tool_DirectedAssociation_Tip),
            new(ToolKind.Inheritance, Strings.Tool_Inheritance, Strings.Tool_Inheritance_Tip),
            new(ToolKind.Realization, Strings.Tool_Realization, Strings.Tool_Realization_Tip),
            new(ToolKind.Aggregation, Strings.Tool_Aggregation, Strings.Tool_Aggregation_Tip),
            new(ToolKind.Composition, Strings.Tool_Composition, Strings.Tool_Composition_Tip),
            new(ToolKind.Dependency, Strings.Tool_Dependency, Strings.Tool_Dependency_Tip),
            .. AnnotationTools,
        ],
        DiagramType.Sequence =>
        [
            SelectTool,
            new(ToolKind.Lifeline, Strings.Tool_Lifeline, Strings.Tool_Lifeline_Tip),
            new(ToolKind.Actor, Strings.Tool_Actor, Strings.Tool_Actor_Tip),
            new(ToolKind.SyncMessage, Strings.Tool_SyncMessage, Strings.Tool_SyncMessage_Tip),
            new(ToolKind.AsyncMessage, Strings.Tool_AsyncMessage, Strings.Tool_AsyncMessage_Tip),
            new(ToolKind.ReplyMessage, Strings.Tool_ReplyMessage, Strings.Tool_ReplyMessage_Tip),
            new(ToolKind.CreateMessage, Strings.Tool_CreateMessage, Strings.Tool_CreateMessage_Tip),
            .. AnnotationTools,
        ],
        _ when GraphRules.IsGraphDiagram(type) =>
        [
            SelectTool,
            .. GraphRules.NodeKinds(type).Select(k => new ToolItem(ToolKind.GraphNode, NodeLabel(k), NodeToolTip(k)) { NodeKind = k }),
            .. GraphRules.EdgeKinds(type).Select(k => new ToolItem(ToolKind.GraphEdge, EdgeLabel(k), EdgeToolTip(k)) { EdgeKind = k }),
            .. AnnotationTools,
        ],
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, null),
    };

    private static string NodeLabel(GraphNodeKind kind) => kind switch
    {
        GraphNodeKind.InitialState or GraphNodeKind.InitialNode => Strings.Tool_Node_Initial,
        GraphNodeKind.FinalState or GraphNodeKind.ActivityFinal => Strings.Tool_Node_Final,
        GraphNodeKind.ForkJoin => Strings.Tool_Node_ForkJoin,
        GraphNodeKind.UseCase => Strings.Tool_Node_UseCase,
        GraphNodeKind.SystemBoundary => Strings.Tool_Node_SystemBoundary,
        GraphNodeKind.ExecutionEnvironment => Strings.Tool_Node_ExecutionEnvironment,
        _ => Loc.Of(kind),
    };

    private static string NodeToolTip(GraphNodeKind kind) => kind switch
    {
        GraphNodeKind.State => Strings.Tool_Node_State_Tip,
        GraphNodeKind.InitialState => Strings.Tool_Node_InitialState_Tip,
        GraphNodeKind.FinalState => Strings.Tool_Node_FinalState_Tip,
        GraphNodeKind.Choice => Strings.Tool_Node_Choice_Tip,
        GraphNodeKind.Action => Strings.Tool_Node_Action_Tip,
        GraphNodeKind.InitialNode => Strings.Tool_Node_InitialNode_Tip,
        GraphNodeKind.ActivityFinal => Strings.Tool_Node_ActivityFinal_Tip,
        GraphNodeKind.Decision => Strings.Tool_Node_Decision_Tip,
        GraphNodeKind.ForkJoin => Strings.Tool_Node_ForkJoin_Tip,
        GraphNodeKind.Actor => Strings.Tool_Node_Actor_Tip,
        GraphNodeKind.UseCase => Strings.Tool_Node_UseCase_Tip,
        GraphNodeKind.SystemBoundary => Strings.Tool_Node_SystemBoundary_Tip,
        GraphNodeKind.Component => Strings.Tool_Node_Component_Tip,
        GraphNodeKind.Interface => Strings.Tool_Node_Interface_Tip,
        GraphNodeKind.Node => Strings.Tool_Node_Node_Tip,
        GraphNodeKind.Device => Strings.Tool_Node_Device_Tip,
        GraphNodeKind.ExecutionEnvironment => Strings.Tool_Node_ExecutionEnvironment_Tip,
        GraphNodeKind.Artifact => Strings.Tool_Node_Artifact_Tip,
        _ => Loc.Format(Strings.Tool_Node_Other_Tip, Loc.Of(kind)),
    };

    private static string EdgeLabel(GraphEdgeKind kind) => kind switch
    {
        GraphEdgeKind.ControlFlow => Strings.Tool_Edge_ControlFlow,
        GraphEdgeKind.CommunicationPath => Strings.Tool_Edge_CommunicationPath,
        _ => Loc.Of(kind),
    };

    private static string EdgeToolTip(GraphEdgeKind kind) => kind switch
    {
        GraphEdgeKind.Transition => Strings.Tool_Edge_Transition_Tip,
        GraphEdgeKind.ControlFlow => Strings.Tool_Edge_ControlFlow_Tip,
        GraphEdgeKind.Association => Strings.Tool_Edge_Association_Tip,
        GraphEdgeKind.Include => Strings.Tool_Edge_Include_Tip,
        GraphEdgeKind.Extend => Strings.Tool_Edge_Extend_Tip,
        GraphEdgeKind.Generalization => Strings.Tool_Edge_Generalization_Tip,
        GraphEdgeKind.Dependency => Strings.Tool_Edge_Dependency_Tip,
        GraphEdgeKind.Realization => Strings.Tool_Edge_Realization_Tip,
        GraphEdgeKind.CommunicationPath => Strings.Tool_Edge_CommunicationPath_Tip,
        GraphEdgeKind.Deploy => Strings.Tool_Edge_Deploy_Tip,
        GraphEdgeKind.Manifest => Strings.Tool_Edge_Manifest_Tip,
        _ => Loc.Of(kind),
    };
}
