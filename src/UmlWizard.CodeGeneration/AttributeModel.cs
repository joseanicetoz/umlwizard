using UmlWizard.Core.Model;

namespace UmlWizard.CodeGeneration;

/// <summary>An attribute to generate; implicit ones come from an implemented interface (used by C# only).</summary>
public sealed record AttributeModel(UmlAttribute Attribute, bool IsImplicit = false);
