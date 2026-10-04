namespace UmlWizard.Core.Model;

/// <summary>The node kinds of state machine, activity, use case and component diagrams.</summary>
public enum GraphNodeKind
{
    // State machine
    State,
    InitialState,
    FinalState,
    Choice,

    // Activity
    Action,
    InitialNode,
    ActivityFinal,
    Decision,
    ForkJoin,

    // Use case
    Actor,
    UseCase,
    SystemBoundary,

    // Component (components also appear in deployment diagrams)
    Component,
    Interface,

    // Deployment
    Node,
    Device,
    ExecutionEnvironment,
    Artifact,
}
