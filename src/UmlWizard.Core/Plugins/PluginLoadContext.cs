using System.Reflection;
using System.Runtime.Loader;

namespace UmlWizard.Core.Plugins;

/// <summary>
/// Loads a plug-in DLL and its own dependencies from the plug-in's folder. Assemblies the application has, such as
/// <c>UmlWizard.Core</c> and the framework, are shared with it, so the interfaces it implements are the application's.
/// </summary>
internal sealed class PluginLoadContext(string pluginPath) : AssemblyLoadContext(Path.GetFileNameWithoutExtension(pluginPath))
{
    private readonly AssemblyDependencyResolver _resolver = new(pluginPath);

    protected override Assembly? Load(AssemblyName assemblyName)
    {
        // Returning null lets the application's context load it: shared with the application.
        if (HostHas(assemblyName))
        {
            return null;
        }

        return _resolver.ResolveAssemblyToPath(assemblyName) is { } path ? LoadFromAssemblyPath(path) : null;
    }

    protected override IntPtr LoadUnmanagedDll(string unmanagedDllName) =>
        _resolver.ResolveUnmanagedDllToPath(unmanagedDllName) is { } path ? LoadUnmanagedDllFromPath(path) : IntPtr.Zero;

    private static bool HostHas(AssemblyName name)
    {
        if (Default.Assemblies.Any(a => AssemblyName.ReferenceMatchesDefinition(name, a.GetName())))
        {
            return true;
        }

        try
        {
            Default.LoadFromAssemblyName(name);
            return true;
        }
        catch (FileNotFoundException)
        {
            return false;
        }
    }
}
