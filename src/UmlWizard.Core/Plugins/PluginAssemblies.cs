using System.Reflection;

namespace UmlWizard.Core.Plugins;

/// <summary>
/// The plug-in DLLs of a folder, each loaded once in its own <see cref="PluginLoadContext"/>. A plug-in is dropped into
/// the folder either as a single DLL (<c>plugins/Foo.dll</c>) or, with its dependencies, in a folder of the same name
/// (<c>plugins/Foo/Foo.dll</c>). The catalogs of code generators and code importers each pick the types they need from
/// the same loaded plug-ins (see <see cref="Create{T}"/>), so a DLL may hold both.
/// </summary>
public sealed class PluginAssemblies
{
    private readonly List<(string File, Type[] Types)> _plugins = [];
    private readonly List<string> _problems = [];

    private PluginAssemblies()
    {
    }

    /// <summary>No plug-ins, e.g. for tests or when the folder does not exist.</summary>
    public static PluginAssemblies None { get; } = new();

    /// <summary>The plug-ins that could not be loaded, and why.</summary>
    public IReadOnlyList<string> Problems => _problems;

    /// <summary>Loads the plug-ins in <paramref name="folder"/>; none when the folder does not exist.</summary>
    public static PluginAssemblies Load(string folder) => Load([folder]);

    /// <summary>
    /// Loads the plug-ins of several folders, in order; folders that do not exist are skipped. A DLL with the name of one
    /// already loaded from an earlier folder is skipped and reported, so the earlier folder wins.
    /// </summary>
    public static PluginAssemblies Load(IEnumerable<string> folders)
    {
        var plugins = new PluginAssemblies();
        var loaded = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in folders.SelectMany(PluginFiles))
        {
            var name = Path.GetFileName(path);
            if (loaded.TryGetValue(name, out var first))
            {
                plugins._problems.Add($"The plug-in {path} is ignored: {first} has the same name and was loaded first.");
                continue;
            }

            loaded[name] = path;
            try
            {
                var assembly = new PluginLoadContext(path).LoadFromAssemblyPath(Path.GetFullPath(path));
                plugins._plugins.Add((name, assembly.GetExportedTypes()));
            }
            catch (Exception ex) when (ex is BadImageFormatException or FileLoadException or FileNotFoundException or TypeLoadException)
            {
                plugins._problems.Add($"The plug-in {name} could not be loaded: {ex.Message}");
            }
            catch (ReflectionTypeLoadException ex)
            {
                plugins._problems.Add($"The plug-in {name} could not be loaded: {ex.LoaderExceptions.FirstOrDefault()?.Message ?? ex.Message}");
            }
        }

        return plugins;
    }

    /// <summary>
    /// An instance of every public, non-abstract class of the plug-ins that implements <typeparamref name="T"/>. Classes
    /// without a public parameterless constructor, or whose constructor fails, are skipped and reported in
    /// <paramref name="problems"/> as a <paramref name="kind"/>, e.g. "code generator".
    /// </summary>
    public IReadOnlyList<T> Create<T>(string kind, ICollection<string> problems) where T : class
    {
        var instances = new List<T>();
        foreach (var (file, types) in _plugins)
        {
            foreach (var type in types.Where(t => t is { IsClass: true, IsAbstract: false } && typeof(T).IsAssignableFrom(t)))
            {
                if (type.GetConstructor(Type.EmptyTypes) is null)
                {
                    problems.Add($"The {kind} {type.FullName} in {file} is ignored: it has no public parameterless constructor.");
                    continue;
                }

                try
                {
                    instances.Add((T)Activator.CreateInstance(type)!);
                }
                catch (Exception ex)
                {
                    problems.Add($"The {kind} {type.FullName} in {file} could not be created: {(ex.InnerException ?? ex).Message}");
                }
            }
        }

        return instances;
    }

    /// <summary>The DLLs directly in the folder, and in each subfolder the DLL named like the subfolder.</summary>
    private static IEnumerable<string> PluginFiles(string folder)
    {
        if (!Directory.Exists(folder))
        {
            return [];
        }

        var single = Directory.EnumerateFiles(folder, "*.dll");
        var inFolders = Directory.EnumerateDirectories(folder)
            .Select(dir => Path.Combine(dir, Path.GetFileName(dir) + ".dll"))
            .Where(File.Exists);
        return single.Concat(inFolders).Order(StringComparer.OrdinalIgnoreCase);
    }
}
