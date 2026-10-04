using System.Text.Json.Serialization;

namespace UmlWizard.Core.Model;

public sealed class ComponentDiagramModel : GraphDiagramModel
{
    [JsonIgnore]
    public override DiagramType DiagramType => DiagramType.Component;
}
