using UmlWizard.Core.Plugins;

namespace UmlWizard.CodeGeneration.Plugins;

/// <summary>
/// The code generators the application offers, found in plug-in DLLs (see <see cref="PluginAssemblies"/>). Every
/// public, non-abstract <see cref="ICodeGenerator"/> with a parameterless constructor in them is offered. A plug-in
/// that cannot be loaded is skipped and reported in <see cref="Problems"/>.
/// </summary>
public sealed class CodeGeneratorCatalog
{
    private readonly List<ICodeGenerator> _generators = [];
    private readonly List<string> _problems = [];

    /// <summary>A catalog of the given generators, e.g. for tests. A generator whose Id is taken is skipped and reported.</summary>
    public CodeGeneratorCatalog(IEnumerable<ICodeGenerator> generators, IEnumerable<string>? problems = null)
    {
        _problems.AddRange(problems ?? []);
        foreach (var generator in generators)
        {
            if (_generators.FirstOrDefault(g => string.Equals(g.Id, generator.Id, StringComparison.OrdinalIgnoreCase)) is { } taken)
            {
                _problems.Add($"The code generator \"{generator.DisplayName}\" ({generator.GetType().Assembly.GetName().Name}) " +
                    $"is ignored: \"{taken.DisplayName}\" already uses the Id \"{generator.Id}\".");
                continue;
            }

            _generators.Add(generator);
        }

        _generators.Sort((a, b) => StringComparer.CurrentCultureIgnoreCase.Compare(a.DisplayName, b.DisplayName));
    }

    /// <summary>The generators, by display name.</summary>
    public IReadOnlyList<ICodeGenerator> Generators => _generators;

    /// <summary>The plug-ins or generators that could not be loaded, and why.</summary>
    public IReadOnlyList<string> Problems => _problems;

    /// <summary>The generator with the Id, ignoring case, or <c>null</c>.</summary>
    public ICodeGenerator? Find(string? id) =>
        _generators.FirstOrDefault(g => string.Equals(g.Id, id, StringComparison.OrdinalIgnoreCase));

    /// <summary>The generators of the plug-ins in <paramref name="folder"/>; empty when the folder does not exist.</summary>
    public static CodeGeneratorCatalog Load(string folder) => From(PluginAssemblies.Load(folder));

    /// <summary>The generators of plug-ins that are already loaded.</summary>
    public static CodeGeneratorCatalog From(PluginAssemblies plugins)
    {
        var problems = new List<string>(plugins.Problems);
        var generators = plugins.Create<ICodeGenerator>("code generator", problems);
        return new CodeGeneratorCatalog(generators, problems);
    }
}
