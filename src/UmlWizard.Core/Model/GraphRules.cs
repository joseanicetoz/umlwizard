namespace UmlWizard.Core.Model;

/// <summary>
/// What each node and edge kind is: the diagram types it belongs to, whether it has a name, its size and which
/// connections are valid.
/// </summary>
public static class GraphRules
{
    public static bool IsGraphDiagram(DiagramType type) =>
        type is DiagramType.StateMachine or DiagramType.Activity or DiagramType.UseCase or DiagramType.Component or
            DiagramType.Deployment;

    public static IReadOnlyList<GraphNodeKind> NodeKinds(DiagramType type) => type switch
    {
        DiagramType.StateMachine => [GraphNodeKind.State, GraphNodeKind.InitialState, GraphNodeKind.FinalState, GraphNodeKind.Choice],
        DiagramType.Activity =>
            [GraphNodeKind.Action, GraphNodeKind.InitialNode, GraphNodeKind.ActivityFinal, GraphNodeKind.Decision, GraphNodeKind.ForkJoin],
        DiagramType.UseCase => [GraphNodeKind.Actor, GraphNodeKind.UseCase, GraphNodeKind.SystemBoundary],
        DiagramType.Component => [GraphNodeKind.Component, GraphNodeKind.Interface],
        DiagramType.Deployment =>
            [GraphNodeKind.Node, GraphNodeKind.Device, GraphNodeKind.ExecutionEnvironment, GraphNodeKind.Artifact, GraphNodeKind.Component],
        _ => [],
    };

    public static IReadOnlyList<GraphEdgeKind> EdgeKinds(DiagramType type) => type switch
    {
        DiagramType.StateMachine => [GraphEdgeKind.Transition],
        DiagramType.Activity => [GraphEdgeKind.ControlFlow],
        DiagramType.UseCase => [GraphEdgeKind.Association, GraphEdgeKind.Include, GraphEdgeKind.Extend, GraphEdgeKind.Generalization],
        DiagramType.Component => [GraphEdgeKind.Dependency, GraphEdgeKind.Realization, GraphEdgeKind.Association],
        DiagramType.Deployment =>
            [GraphEdgeKind.CommunicationPath, GraphEdgeKind.Deploy, GraphEdgeKind.Manifest, GraphEdgeKind.Dependency],
        _ => [],
    };

    /// <summary>Nodes that show a name; pseudostates, final nodes, decisions and fork/join bars do not.</summary>
    public static bool HasName(GraphNodeKind kind) => kind is GraphNodeKind.State or GraphNodeKind.Action or
        GraphNodeKind.Actor or GraphNodeKind.UseCase or GraphNodeKind.SystemBoundary or GraphNodeKind.Component or
        GraphNodeKind.Interface or GraphNodeKind.Node or GraphNodeKind.Device or GraphNodeKind.ExecutionEnvironment or
        GraphNodeKind.Artifact;

    /// <summary>Nodes whose size the user can change with the resize handle.</summary>
    public static bool IsResizable(GraphNodeKind kind) => kind is GraphNodeKind.State or GraphNodeKind.Action or
        GraphNodeKind.UseCase or GraphNodeKind.SystemBoundary or GraphNodeKind.Component or GraphNodeKind.ForkJoin or
        GraphNodeKind.Node or GraphNodeKind.Device or GraphNodeKind.ExecutionEnvironment or GraphNodeKind.Artifact;

    /// <summary>
    /// Nodes drawn around other nodes: moving one takes the nodes that lie completely inside it along. A system
    /// boundary groups use cases; deployment nodes, devices and execution environments contain what is deployed on
    /// them, and can be nested.
    /// </summary>
    public static bool IsContainer(GraphNodeKind kind) => kind is GraphNodeKind.SystemBoundary or GraphNodeKind.Node or
        GraphNodeKind.Device or GraphNodeKind.ExecutionEnvironment;

    /// <summary>Where artifacts are deployed: nodes, devices and execution environments.</summary>
    public static bool IsDeploymentTarget(GraphNodeKind kind) =>
        kind is GraphNodeKind.Node or GraphNodeKind.Device or GraphNodeKind.ExecutionEnvironment;

    /// <summary>The keyword shown above a node's name, e.g. «device»; <c>null</c> for kinds without one.</summary>
    public static string? Keyword(GraphNodeKind kind) => kind switch
    {
        GraphNodeKind.Component => "«component»",
        GraphNodeKind.Device => "«device»",
        GraphNodeKind.ExecutionEnvironment => "«executionEnvironment»",
        GraphNodeKind.Artifact => "«artifact»",
        _ => null,
    };

    public static (double Width, double Height) DefaultSize(GraphNodeKind kind) => kind switch
    {
        GraphNodeKind.State or GraphNodeKind.Action => (120, 50),
        GraphNodeKind.InitialState or GraphNodeKind.InitialNode => (22, 22),
        GraphNodeKind.FinalState or GraphNodeKind.ActivityFinal => (26, 26),
        GraphNodeKind.Choice or GraphNodeKind.Decision => (32, 32),
        GraphNodeKind.ForkJoin => (100, 8),
        GraphNodeKind.Actor => (36, 64),
        GraphNodeKind.UseCase => (140, 60),
        GraphNodeKind.SystemBoundary => (320, 280),
        GraphNodeKind.Component => (150, 64),
        GraphNodeKind.Interface => (20, 20),
        GraphNodeKind.Node or GraphNodeKind.Device => (240, 160),
        GraphNodeKind.ExecutionEnvironment => (200, 120),
        GraphNodeKind.Artifact => (150, 60),
        _ => (100, 50),
    };

    public static (double Width, double Height) MinimumSize(GraphNodeKind kind) => kind switch
    {
        GraphNodeKind.State or GraphNodeKind.Action => (60, 36),
        GraphNodeKind.UseCase => (80, 40),
        GraphNodeKind.SystemBoundary => (120, 100),
        GraphNodeKind.Component => (90, 50),
        GraphNodeKind.ForkJoin => (8, 8),
        GraphNodeKind.Node or GraphNodeKind.Device => (120, 80),
        GraphNodeKind.ExecutionEnvironment => (110, 70),
        GraphNodeKind.Artifact => (100, 50),
        _ => DefaultSize(kind),
    };

    /// <summary>The name a new node gets before it is numbered, e.g. "State" becomes "State1".</summary>
    public static string DefaultName(GraphNodeKind kind) => kind switch
    {
        GraphNodeKind.SystemBoundary => "System",
        GraphNodeKind.Interface => "IInterface",
        GraphNodeKind.ExecutionEnvironment => "Environment",
        _ => kind.ToString(),
    };

    /// <summary>
    /// Why an edge of <paramref name="kind"/> cannot connect the two nodes, or <c>null</c> when it can. Final nodes have
    /// no outgoing and initial nodes no incoming edges; only transitions may loop back to their own node; system
    /// boundaries are not connected; «include» and «extend» connect use cases, generalizations nodes of the same kind,
    /// and realizations end at an interface. «deploy» goes from an artifact to a node, «manifest» from an artifact to a
    /// component, and communication paths connect nodes.
    /// </summary>
    public static string? CheckEdge(DiagramType type, GraphEdgeKind kind, GraphNode source, GraphNode target)
    {
        if (!EdgeKinds(type).Contains(kind))
        {
            return $"A {kind} does not belong in this diagram.";
        }

        if (source.Kind == GraphNodeKind.SystemBoundary || target.Kind == GraphNodeKind.SystemBoundary)
        {
            return "System boundaries are not connected.";
        }

        if (source.Id == target.Id && kind != GraphEdgeKind.Transition)
        {
            return "Only a transition may lead back to its own state.";
        }

        if (source.Kind is GraphNodeKind.FinalState or GraphNodeKind.ActivityFinal)
        {
            return "Nothing leaves a final node.";
        }

        if (target.Kind is GraphNodeKind.InitialState or GraphNodeKind.InitialNode)
        {
            return "Nothing leads into an initial node.";
        }

        return kind switch
        {
            GraphEdgeKind.Include or GraphEdgeKind.Extend when source.Kind != GraphNodeKind.UseCase || target.Kind != GraphNodeKind.UseCase =>
                $"«{kind.ToString().ToLowerInvariant()}» connects two use cases.",
            GraphEdgeKind.Generalization when source.Kind != target.Kind => "A generalization connects two actors or two use cases.",
            GraphEdgeKind.Realization when target.Kind != GraphNodeKind.Interface => "A realization ends at an interface.",
            GraphEdgeKind.Deploy when source.Kind != GraphNodeKind.Artifact || !IsDeploymentTarget(target.Kind) =>
                "«deploy» connects an artifact to the node, device or execution environment it is deployed on.",
            GraphEdgeKind.Manifest when source.Kind != GraphNodeKind.Artifact || target.Kind != GraphNodeKind.Component =>
                "«manifest» connects an artifact to the component it implements.",
            GraphEdgeKind.CommunicationPath when !IsDeploymentTarget(source.Kind) || !IsDeploymentTarget(target.Kind) =>
                "A communication path connects nodes, devices or execution environments.",
            _ => null,
        };
    }
}
