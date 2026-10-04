namespace UmlWizard.CodeGeneration;

/// <summary>What to generate from a class diagram.</summary>
/// <param name="Namespace">
/// The namespace (C#, C++), package (Java, Kotlin) or module (Ruby, TypeScript) of the generated types, e.g.
/// <c>Shop.Domain</c>; parts may be separated by <c>.</c> or <c>::</c>. Empty for none.
/// </param>
/// <param name="DiagramName">Named in the comment at the top of every file.</param>
public sealed record CodeGenerationOptions(string? Namespace, string DiagramName);
