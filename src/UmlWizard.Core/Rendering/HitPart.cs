namespace UmlWizard.Core.Rendering;

/// <summary>The part of a diagram element found under a point.</summary>
public enum HitPart
{
    ClassHeader,
    ClassAttribute,
    ClassAttributesArea,
    ClassOperation,
    ClassOperationsArea,
    ResizeHandle,
    Relationship,
    LifelineHead,
    LifelineBody,
    Message,
    /// <summary>The text of a text box or a note.</summary>
    Text,
    NoteAttachment,

    /// <summary>A node of a state machine, activity, use case or component diagram.</summary>
    Node,

    /// <summary>A system boundary (a node that is not connected).</summary>
    Boundary,

    /// <summary>An edge of a state machine, activity, use case or component diagram, or its label.</summary>
    Edge,
}
