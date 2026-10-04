namespace UmlWizard.Core.Model;

/// <summary>Base type for every identifiable element of a UML model.</summary>
public abstract class ModelElement
{
    public string Id { get; set; } = NewId();

    public static string NewId() => Guid.NewGuid().ToString("N")[..12];
}
