namespace UmlWizard.Core.Model;

public sealed class SequenceMessage : ModelElement
{
    public string SourceId { get; set; } = string.Empty;

    public string TargetId { get; set; } = string.Empty;

    public string Text { get; set; } = string.Empty;

    public MessageKind Kind { get; set; } = MessageKind.Synchronous;
}
