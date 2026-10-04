namespace UmlWizard.CodeGeneration;

/// <summary>
/// An association, aggregation or composition end that becomes a field of the owning type.
/// </summary>
/// <param name="Name">The field name before the language's casing, e.g. "orderLines".</param>
/// <param name="Label">The relationship's label, shown as the field's documentation.</param>
public sealed record AssociationEnd(TypeModel Target, string Name, bool IsMany, bool IsOptional, bool IsComposition, string? Label);
