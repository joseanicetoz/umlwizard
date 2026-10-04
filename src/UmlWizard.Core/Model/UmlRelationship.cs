namespace UmlWizard.Core.Model;

/// <summary>
/// A relationship between two classifiers. Arrow heads (inheritance, realization, dependency,
/// directed association) point at the target; diamonds (aggregation, composition) sit at the source,
/// i.e. the source is the "whole".
/// </summary>
public sealed class UmlRelationship : ModelElement
{
    public RelationshipKind Kind { get; set; } = RelationshipKind.Association;

    public string SourceId { get; set; } = string.Empty;

    public string TargetId { get; set; } = string.Empty;

    public string? Label { get; set; }

    public string? SourceMultiplicity { get; set; }

    public string? TargetMultiplicity { get; set; }
}
