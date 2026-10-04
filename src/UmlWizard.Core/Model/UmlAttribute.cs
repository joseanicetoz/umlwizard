namespace UmlWizard.Core.Model;

public sealed class UmlAttribute
{
    public string Name { get; set; } = "attribute";

    public string? Type { get; set; }

    public UmlVisibility Visibility { get; set; } = UmlVisibility.Private;

    public bool IsStatic { get; set; }

    public string? DefaultValue { get; set; }

    public List<TaggedValue> TaggedValues { get; set; } = [];
}
