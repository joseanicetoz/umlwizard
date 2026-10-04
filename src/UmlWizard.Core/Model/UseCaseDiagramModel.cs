using System.Text.Json.Serialization;

namespace UmlWizard.Core.Model;

public sealed class UseCaseDiagramModel : GraphDiagramModel
{
    [JsonIgnore]
    public override DiagramType DiagramType => DiagramType.UseCase;
}
