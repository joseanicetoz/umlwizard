using UmlWizard.App.Resources;
using UmlWizard.Core.Model;

namespace UmlWizard.App.ViewModels;

public enum ToolKind
{
    Select,

    /// <summary>Not a tool: a divider between groups of tools.</summary>
    Separator,

    // Class diagram nodes
    Class,
    AbstractClass,
    Interface,
    Enumeration,

    // Class diagram relationships
    Association,
    DirectedAssociation,
    Inheritance,
    Realization,
    Aggregation,
    Composition,
    Dependency,

    // Sequence diagram
    Lifeline,
    Actor,
    SyncMessage,
    AsyncMessage,
    ReplyMessage,
    CreateMessage,

    // State machine, activity, use case and component diagrams: the kind is in the tool item
    GraphNode,
    GraphEdge,

    // Any diagram
    Note,
    NoteAttachment,
    TextBox,
}
