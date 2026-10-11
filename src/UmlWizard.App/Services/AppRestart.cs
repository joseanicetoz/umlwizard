using System.ComponentModel;
using System.Diagnostics;
using System.Reflection;

namespace UmlWizard.App.Services;

/// <summary>Starts the application again, e.g. to show it in a new display language.</summary>
public static class AppRestart
{
    /// <summary>
    /// Starts a new instance of the application, opening <paramref name="projectPath"/> when there is one. This one
    /// is expected to exit right after.
    /// </summary>
    /// <returns>Whether the new instance was started.</returns>
    public static bool StartNewInstance(string? projectPath)
    {
        if (Environment.ProcessPath is not { } executable)
        {
            return false;
        }

        var start = new ProcessStartInfo(executable) { UseShellExecute = false };

        // Run with "dotnet UmlWizard.dll" (as from an IDE), the host needs the application's assembly first.
        if (Path.GetFileNameWithoutExtension(executable).Equals("dotnet", StringComparison.OrdinalIgnoreCase) &&
            Assembly.GetEntryAssembly()?.Location is { Length: > 0 } assembly)
        {
            start.ArgumentList.Add(assembly);
        }

        if (projectPath is not null)
        {
            start.ArgumentList.Add(projectPath);
        }

        try
        {
            using var process = Process.Start(start);
            return process is not null;
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            return false;
        }
    }
}
