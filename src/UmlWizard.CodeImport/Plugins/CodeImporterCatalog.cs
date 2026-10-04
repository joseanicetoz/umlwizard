using UmlWizard.Core.Plugins;

namespace UmlWizard.CodeImport.Plugins;

/// <summary>
/// The languages the application can import, found in plug-in DLLs (see <see cref="PluginAssemblies"/>). Every public,
/// non-abstract <see cref="ICodeImporter"/> with a parameterless constructor in them is offered. A plug-in that cannot
/// be loaded is skipped and reported in <see cref="Problems"/>.
/// </summary>
public sealed class CodeImporterCatalog
{
    private readonly List<ICodeImporter> _importers = [];
    private readonly List<string> _problems = [];

    /// <summary>A catalog of the given importers, e.g. for tests. An importer whose Id is taken is skipped and reported.</summary>
    public CodeImporterCatalog(IEnumerable<ICodeImporter> importers, IEnumerable<string>? problems = null)
    {
        _problems.AddRange(problems ?? []);
        foreach (var importer in importers)
        {
            if (_importers.FirstOrDefault(i => string.Equals(i.Id, importer.Id, StringComparison.OrdinalIgnoreCase)) is { } taken)
            {
                _problems.Add($"The code importer \"{importer.DisplayName}\" ({importer.GetType().Assembly.GetName().Name}) " +
                    $"is ignored: \"{taken.DisplayName}\" already uses the Id \"{importer.Id}\".");
                continue;
            }

            _importers.Add(importer);
        }

        _importers.Sort((a, b) => StringComparer.CurrentCultureIgnoreCase.Compare(a.DisplayName, b.DisplayName));
    }

    /// <summary>The importers, by display name.</summary>
    public IReadOnlyList<ICodeImporter> Importers => _importers;

    /// <summary>The plug-ins or importers that could not be loaded, and why.</summary>
    public IReadOnlyList<string> Problems => _problems;

    /// <summary>The importer with the Id, ignoring case, or <c>null</c>.</summary>
    public ICodeImporter? Find(string? id) =>
        _importers.FirstOrDefault(i => string.Equals(i.Id, id, StringComparison.OrdinalIgnoreCase));

    /// <summary>The importer that reads files with the extension (e.g. <c>.cs</c>), or <c>null</c>.</summary>
    public ICodeImporter? ForExtension(string extension) =>
        _importers.FirstOrDefault(i => i.FileExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase));

    /// <summary>The importers of the plug-ins in <paramref name="folder"/>; empty when the folder does not exist.</summary>
    public static CodeImporterCatalog Load(string folder) => From(PluginAssemblies.Load(folder));

    /// <summary>The importers of plug-ins that are already loaded.</summary>
    public static CodeImporterCatalog From(PluginAssemblies plugins)
    {
        var problems = new List<string>(plugins.Problems);
        var importers = plugins.Create<ICodeImporter>("code importer", problems);
        return new CodeImporterCatalog(importers, problems);
    }
}
