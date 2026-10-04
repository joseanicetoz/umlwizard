namespace UmlWizard.Core.Model;

/// <summary>The model of the diagram types made of nodes and edges.</summary>
public abstract class GraphDiagramModel : DiagramModel
{
    public List<GraphNode> Nodes { get; set; } = [];

    public List<GraphEdge> Edges { get; set; } = [];

    public GraphNode? FindNode(string id) => Nodes.Find(n => n.Id == id);

    public GraphEdge? FindEdge(string id) => Edges.Find(e => e.Id == id);

    protected override ModelElement? FindUmlElement(string id) => (ModelElement?)FindNode(id) ?? FindEdge(id);

    protected override IEnumerable<ModelElement> UmlElements() => Nodes.Cast<ModelElement>().Concat(Edges);

    protected override IReadOnlyList<string> RemoveUmlElement(string id)
    {
        var removed = new List<string>();
        if (Nodes.RemoveAll(n => n.Id == id) > 0)
        {
            removed.Add(id);
            foreach (var edge in Edges.Where(e => e.SourceId == id || e.TargetId == id).ToList())
            {
                Edges.Remove(edge);
                removed.Add(edge.Id);
            }
        }
        else if (Edges.RemoveAll(e => e.Id == id) > 0)
        {
            removed.Add(id);
        }

        return removed;
    }
}
