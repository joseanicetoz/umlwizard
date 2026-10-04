namespace UmlWizard.Core.Model;

/// <summary>A classifier: class, abstract class, interface or enumeration.</summary>
public sealed class UmlClass : ModelElement
{
    public string Name { get; set; } = "Class";

    public ClassifierKind Kind { get; set; } = ClassifierKind.Class;

    public string? Stereotype { get; set; }

    /// <summary>Attributes; for enumerations these are the literals.</summary>
    public List<UmlAttribute> Attributes { get; set; } = [];

    public List<UmlOperation> Operations { get; set; } = [];

    public List<TaggedValue> TaggedValues { get; set; } = [];
}
