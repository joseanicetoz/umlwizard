namespace UmlWizard.CodeGeneration;

/// <summary>A generated source file.</summary>
/// <param name="Path">Relative to the output folder, with <c>/</c> between folders, e.g. <c>shop/domain/Order.java</c>.</param>
public sealed record GeneratedFile(string Path, string Content);
