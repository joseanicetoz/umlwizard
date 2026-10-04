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
}
