using System.Text.Json.Serialization;

namespace UmlWizard.Core.Model;

/// <summary>
/// The semantic (UML) part of a diagram, plus its notes and text boxes. Contains no rendering information;
/// positions and sizes live in <see cref="DiagramLayout"/>.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(ClassDiagramModel), "class")]
[JsonDerivedType(typeof(SequenceDiagramModel), "sequence")]
[JsonDerivedType(typeof(StateMachineDiagramModel), "stateMachine")]
[JsonDerivedType(typeof(ActivityDiagramModel), "activity")]
[JsonDerivedType(typeof(UseCaseDiagramModel), "useCase")]
[JsonDerivedType(typeof(ComponentDiagramModel), "component")]
[JsonDerivedType(typeof(DeploymentDiagramModel), "deployment")]
public abstract class DiagramModel
{
    [JsonIgnore]
    public abstract DiagramType DiagramType { get; }

    /// <summary>UML notes, drawn above the UML elements; see <see cref="TextElements"/> for their order.</summary>
    public List<NoteElement> Notes { get; set; } = [];

    /// <summary>Text labels, drawn above the UML elements; see <see cref="TextElements"/> for their order.</summary>
    public List<TextBoxElement> TextBoxes { get; set; } = [];

    /// <summary>Dashed lines connecting notes to the elements they comment on.</summary>
    public List<NoteAttachment> NoteAttachments { get; set; } = [];

    /// <summary>
    /// Notes and text boxes in drawing order, by <see cref="TextElement.ZIndex"/>: the last one is on top.
    /// </summary>
    public IEnumerable<TextElement> TextElements() => Notes.Concat<TextElement>(TextBoxes).OrderBy(e => e.ZIndex);

    public TextBoxElement? FindTextBox(string id) => TextBoxes.Find(t => t.Id == id);

    public NoteElement? FindNote(string id) => Notes.Find(n => n.Id == id);

    public TextElement? FindTextElement(string id) => (TextElement?)FindTextBox(id) ?? FindNote(id);

    public NoteAttachment? FindNoteAttachment(string id) => NoteAttachments.Find(a => a.Id == id);

    /// <summary>A class, relationship, lifeline or message; not a note, text box or attachment.</summary>
    public ModelElement? FindUmlElementById(string id) => FindUmlElement(id);

    public ModelElement? FindElement(string id) =>
        (ModelElement?)FindTextElement(id) ?? FindNoteAttachment(id) ?? FindUmlElement(id);

    public IEnumerable<ModelElement> AllElements() => UmlElements().Concat(TextElements()).Concat(NoteAttachments);

    /// <summary>Adds a note or a text box to its list, on top of the other notes and text boxes.</summary>
    public void Add(TextElement element)
    {
        element.ZIndex = TextElements().Any() ? TextElements().Max(e => e.ZIndex) + 1 : 0;
        switch (element)
        {
            case NoteElement note: Notes.Add(note); break;
            case TextBoxElement textBox: TextBoxes.Add(textBox); break;
        }
    }

    /// <summary>
    /// Removes an element and every element that depends on it, including the attachments of removed notes and
    /// removed targets (e.g. of a relationship removed with its class).
    /// </summary>
    /// <returns>The ids of all removed elements.</returns>
    public IReadOnlyList<string> RemoveElement(string id)
    {
        if (NoteAttachments.RemoveAll(a => a.Id == id) > 0)
        {
            return [id];
        }

        var removed = new List<string>();
        if (TextBoxes.RemoveAll(t => t.Id == id) + Notes.RemoveAll(n => n.Id == id) > 0)
        {
            removed.Add(id);
        }
        else
        {
            removed.AddRange(RemoveUmlElement(id));
        }

        foreach (var attachment in NoteAttachments.Where(a => removed.Contains(a.NoteId) || removed.Contains(a.TargetId)).ToList())
        {
            NoteAttachments.Remove(attachment);
            removed.Add(attachment.Id);
        }

        return removed;
    }

    /// <summary>Drops attachments whose note or target does not exist (e.g. in a hand-edited file).</summary>
    public void RemoveDanglingAttachments() =>
        NoteAttachments.RemoveAll(a => a is null || FindNote(a.NoteId) is null || FindUmlElement(a.TargetId) is null);

    protected abstract ModelElement? FindUmlElement(string id);

    protected abstract IEnumerable<ModelElement> UmlElements();

    protected abstract IReadOnlyList<string> RemoveUmlElement(string id);
}
