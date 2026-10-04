namespace UmlWizard.Core.Model;

public sealed class Lifeline : ModelElement
{
    public string Name { get; set; } = "object";

    /// <summary>Classifier of the participant, rendered as <c>name : Type</c>.</summary>
    public string? Type { get; set; }

    public LifelineKind Kind { get; set; } = LifelineKind.Object;
}
