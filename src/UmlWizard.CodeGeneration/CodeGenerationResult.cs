namespace UmlWizard.CodeGeneration;

/// <summary>The generated files, and what of the diagram the language could not express.</summary>
public sealed record CodeGenerationResult(IReadOnlyList<GeneratedFile> Files, IReadOnlyList<string> Warnings);
