using System.Text.Json.Serialization;

namespace UmlWizard.Core.Model;

public sealed class DeploymentDiagramModel : GraphDiagramModel
{
    [JsonIgnore]
    public override DiagramType DiagramType => DiagramType.Deployment;
}
