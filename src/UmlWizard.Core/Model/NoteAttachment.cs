namespace UmlWizard.Core.Model;

/// <summary>
/// Connects a note to the element it comments on, drawn as a dashed line. The target is a UML element of the diagram
/// (a class, relationship, lifeline or message), never another note or a text box.
/// </summary>
public sealed class NoteAttachment : ModelElement
{
    public string NoteId { get; set; } = string.Empty;

    public string TargetId { get; set; } = string.Empty;
}
