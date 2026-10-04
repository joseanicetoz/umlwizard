namespace UmlWizard.CodeImport;

/// <summary>
/// What to import: the source files (all of one language), the name of the diagram to make of them, and the files that
/// are left out because they are not source files of that language.
/// </summary>
/// <param name="Name">The name of the diagram, e.g. the project or folder name.</param>
/// <param name="Label">The source as the summary names it, e.g. <c>Shop.Domain.csproj</c> or <c>the folder src</c>.</param>
/// <param name="Files">Full paths of the source files.</param>
/// <param name="SkippedFiles">Other files that were found, by extension.</param>
public sealed record CodeSource(string Name, string Label, IReadOnlyList<string> Files, IReadOnlyDictionary<string, int> SkippedFiles);

/// <summary>Finds the source files of a folder and its subfolders, without build output and tool folders.</summary>
public static class SourceFolder
{
    /// <summary>Folders that hold build output, dependencies or tool state rather than source.</summary>
    private static readonly HashSet<string> IgnoredFolders = new(StringComparer.OrdinalIgnoreCase)
    {
        "bin", "obj", "out", "build", "target", "dist", "packages", "node_modules", "vendor", "__pycache__",
    };

    /// <summary>The files of <paramref name="extensions"/> under <paramref name="folder"/>, and a count of the others.</summary>
    public static CodeSource Read(string folder, IReadOnlyCollection<string> extensions)
    {
        folder = Path.GetFullPath(folder.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        var files = new List<string>();
        var skipped = new Dictionary<string, int>();
        foreach (var file in Files(folder))
        {
            var extension = Extension(file);
            if (extensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
            {
                files.Add(file);
            }
            else
            {
                skipped[extension] = skipped.GetValueOrDefault(extension) + 1;
            }
        }

        var name = Path.GetFileName(folder);
        return new CodeSource(name, $"the folder {name}", files.Order(StringComparer.OrdinalIgnoreCase).ToList(), skipped);
    }

    /// <summary>How many files of each importer's language a folder has, e.g. to suggest the language.</summary>
    public static IReadOnlyDictionary<ICodeImporter, int> Count(string folder, IEnumerable<ICodeImporter> importers)
    {
        var byExtension = Files(Path.GetFullPath(folder)).GroupBy(Extension, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);
        return importers.ToDictionary(i => i, i => i.FileExtensions.Sum(e => byExtension.GetValueOrDefault(e)));
    }

    /// <summary>
    /// The files under a folder, leaving out hidden folders (e.g. <c>.git</c>, <c>.vs</c>), build output and
    /// dependencies, and, when <paramref name="skip"/> says so, other folders (e.g. those of other projects).
    /// </summary>
    public static IEnumerable<string> Files(string folder, Func<string, bool>? skip = null)
    {
        foreach (var file in Directory.EnumerateFiles(folder))
        {
            yield return file;
        }

        foreach (var sub in Directory.EnumerateDirectories(folder))
        {
            var name = Path.GetFileName(sub);
            if (IgnoredFolders.Contains(name) || name.StartsWith('.') || skip?.Invoke(sub) == true)
            {
                continue;
            }

            foreach (var file in Files(sub, skip))
            {
                yield return file;
            }
        }
    }

    /// <summary>The extension of a file in lower case, or <c>(no extension)</c>.</summary>
    public static string Extension(string file) =>
        Path.GetExtension(file) is { Length: > 0 } extension ? extension.ToLowerInvariant() : "(no extension)";
}
