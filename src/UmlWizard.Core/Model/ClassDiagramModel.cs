using System.Text.Json.Serialization;

namespace UmlWizard.Core.Model;

public sealed class ClassDiagramModel : DiagramModel
{
    [JsonIgnore]
    public override DiagramType DiagramType => DiagramType.Class;

    public List<UmlClass> Classes { get; set; } = [];

    public List<UmlRelationship> Relationships { get; set; } = [];

    public UmlClass? FindClass(string id) => Classes.Find(c => c.Id == id);

    protected override ModelElement? FindUmlElement(string id) =>
        (ModelElement?)FindClass(id) ?? Relationships.Find(r => r.Id == id);

    protected override IEnumerable<ModelElement> UmlElements() =>
        Classes.Cast<ModelElement>().Concat(Relationships);

    protected override IReadOnlyList<string> RemoveUmlElement(string id)
    {
        var removed = new List<string>();
        if (Classes.RemoveAll(c => c.Id == id) > 0)
        {
            removed.Add(id);
            foreach (var relationship in Relationships.Where(r => r.SourceId == id || r.TargetId == id).ToList())
            {
                Relationships.Remove(relationship);
                removed.Add(relationship.Id);
            }
        }
        else if (Relationships.RemoveAll(r => r.Id == id) > 0)
        {
            removed.Add(id);
        }

        return removed;
    }
}
