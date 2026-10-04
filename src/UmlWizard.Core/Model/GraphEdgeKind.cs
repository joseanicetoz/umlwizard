namespace UmlWizard.Core.Model;

/// <summary>The edge kinds of state machine, activity, use case and component diagrams.</summary>
public enum GraphEdgeKind
{
    Transition,
    ControlFlow,
    Association,
    Include,
    Extend,
    Generalization,
    Dependency,
    Realization,
    CommunicationPath,
    Deploy,
    Manifest,
}
