using System.Text.Json.Serialization;

namespace UmlWizard.Core.Model;

public sealed class SequenceDiagramModel : DiagramModel
{
    [JsonIgnore]
    public override DiagramType DiagramType => DiagramType.Sequence;

    public List<Lifeline> Lifelines { get; set; } = [];

    /// <summary>Messages. Their order in time is given by their vertical position in the layout.</summary>
    public List<SequenceMessage> Messages { get; set; } = [];

    public Lifeline? FindLifeline(string id) => Lifelines.Find(l => l.Id == id);

    protected override ModelElement? FindUmlElement(string id) =>
        (ModelElement?)FindLifeline(id) ?? Messages.Find(m => m.Id == id);

    protected override IEnumerable<ModelElement> UmlElements() =>
        Lifelines.Cast<ModelElement>().Concat(Messages);

    protected override IReadOnlyList<string> RemoveUmlElement(string id)
    {
        var removed = new List<string>();
        if (Lifelines.RemoveAll(l => l.Id == id) > 0)
        {
            removed.Add(id);
            foreach (var message in Messages.Where(m => m.SourceId == id || m.TargetId == id).ToList())
            {
                Messages.Remove(message);
                removed.Add(message.Id);
            }
        }
        else if (Messages.RemoveAll(m => m.Id == id) > 0)
        {
            removed.Add(id);
        }

        return removed;
    }
}
