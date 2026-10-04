namespace UmlWizard.CodeImport;

/// <summary>A source file to import: its path and text.</summary>
public sealed record SourceFile(string Path, string Text);

/// <summary>What a language plug-in read from the source files.</summary>
/// <param name="Types">The declared types; the parts of a type may come separately (partial or reopened classes).</param>
/// <param name="Notes">What the language has that a class diagram cannot show and was left out, one sentence each.</param>
public sealed record CodeImportParseResult(IReadOnlyList<ImportedType> Types, IReadOnlyList<string> Notes);

/// <summary>
/// Reads the source files of one programming language into <see cref="ImportedType"/>s. Implementations are loaded from
/// plug-in DLLs in the application's <c>plugins</c> folder (see <see cref="Plugins.CodeImporterCatalog"/>); they need a
/// public parameterless constructor. They only read declarations: <see cref="CodeImporter"/> does the rest.
/// </summary>
public interface ICodeImporter
{
    /// <summary>A unique, stable identifier, e.g. <c>CSharp</c>.</summary>
    string Id { get; }

    /// <summary>The language as shown to the user, e.g. <c>C#</c>.</summary>
    string DisplayName { get; }

    /// <summary>The extensions of the files the importer reads, with the dot, e.g. <c>.cs</c>.</summary>
    IReadOnlyList<string> FileExtensions { get; }

    /// <summary>Reads the declarations of the files. Code that cannot be read is skipped and reported in the notes.</summary>
    CodeImportParseResult Parse(IReadOnlyList<SourceFile> files);
}
