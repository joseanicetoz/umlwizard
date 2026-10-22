namespace UmlWizard.Core.Model;

/// <summary>
/// A diagram of a project: header information, the UML <see cref="Model"/> and the rendering <see cref="Layout"/>.
/// </summary>
public sealed class DiagramDocument
{
    public string Id { get; set; } = ModelElement.NewId();

    public string Name { get; set; } = "Diagram";

    public string? Description { get; set; }

    public string? Author { get; set; }

    /// <summary>Tagged values of the diagram as a whole, e.g. the servers of an imported OpenAPI description.</summary>
    public List<TaggedValue> TaggedValues { get; set; } = [];

    public DiagramType DiagramType { get; set; }

    public DiagramModel Model { get; set; } = new ClassDiagramModel();

    public DiagramLayout Layout { get; set; } = new();

    public static DiagramDocument Create(string name, DiagramType type) => new()
    {
        Name = name,
        DiagramType = type,
        Model = type switch
        {
            DiagramType.Class => new ClassDiagramModel(),
            DiagramType.Sequence => new SequenceDiagramModel(),
            DiagramType.StateMachine => new StateMachineDiagramModel(),
            DiagramType.Activity => new ActivityDiagramModel(),
            DiagramType.UseCase => new UseCaseDiagramModel(),
            DiagramType.Component => new ComponentDiagramModel(),
            DiagramType.Deployment => new DeploymentDiagramModel(),
            _ => throw new ArgumentOutOfRangeException(nameof(type), type, null),
        },
    };

    /// <summary>
    /// Replaces the ID of the diagram and of every model element and layout entry with a new ID,
    /// updating all references (relationships, messages, edges, note attachments, and layout elements)
    /// so the diagram has no ID conflicts when imported into another project.
    /// </summary>
    public void RegenerateIds()
    {
        Id = ModelElement.NewId();
        var idMap = new Dictionary<string, string>();
        foreach (var element in Model.AllElements())
        {
            var newId = ModelElement.NewId();
            idMap[element.Id] = newId;
            element.Id = newId;
        }

        if (Model is ClassDiagramModel classDiagram)
        {
            foreach (var rel in classDiagram.Relationships)
            {
                if (idMap.TryGetValue(rel.SourceId, out var newSource)) rel.SourceId = newSource;
                if (idMap.TryGetValue(rel.TargetId, out var newTarget)) rel.TargetId = newTarget;
            }
        }
        else if (Model is SequenceDiagramModel sequenceDiagram)
        {
            foreach (var msg in sequenceDiagram.Messages)
            {
                if (idMap.TryGetValue(msg.SourceId, out var newSource)) msg.SourceId = newSource;
                if (idMap.TryGetValue(msg.TargetId, out var newTarget)) msg.TargetId = newTarget;
            }
        }
        else if (Model is GraphDiagramModel graphDiagram)
        {
            foreach (var edge in graphDiagram.Edges)
            {
                if (idMap.TryGetValue(edge.SourceId, out var newSource)) edge.SourceId = newSource;
                if (idMap.TryGetValue(edge.TargetId, out var newTarget)) edge.TargetId = newTarget;
            }
        }

        foreach (var attachment in Model.NoteAttachments)
        {
            if (idMap.TryGetValue(attachment.NoteId, out var newNote)) attachment.NoteId = newNote;
            if (idMap.TryGetValue(attachment.TargetId, out var newTarget)) attachment.TargetId = newTarget;
        }

        foreach (var layout in Layout.Elements)
        {
            if (idMap.TryGetValue(layout.ElementId, out var newElementId))
            {
                layout.ElementId = newElementId;
            }
        }
    }

    /// <summary>
    /// Returns a non-conflicting diagram name based on <paramref name="name"/> that is not in <paramref name="existingNames"/>.
    /// If <paramref name="name"/> already exists, appends " (1)", " (2)", etc. until a unique name is found.
    /// </summary>
    public static string EnsureUniqueName(string name, IEnumerable<string> existingNames)
    {
        var set = existingNames.ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!set.Contains(name))
        {
            return name;
        }

        var number = 1;
        while (true)
        {
            var candidate = $"{name} ({number})";
            if (!set.Contains(candidate))
            {
                return candidate;
            }
            number++;
        }
    }
}
