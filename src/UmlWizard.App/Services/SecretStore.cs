using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace UmlWizard.App.Services;

/// <summary>Where secrets such as API keys are kept: the operating system's credential store, never the settings file.</summary>
public interface ISecretStore
{
    /// <summary>Whether secrets survive the application: false when the platform has no store it can use.</summary>
    bool IsPersistent { get; }

    /// <summary>The secret, or <c>null</c> when there is none.</summary>
    string? Get(string name);

    /// <summary>Keeps the secret, or forgets it when <paramref name="value"/> is empty; false when it could not.</summary>
    bool Set(string name, string? value);
}

/// <summary>Keeps secrets while the application runs; for tests, and where the platform has no store.</summary>
public sealed class InMemorySecretStore : ISecretStore
{
    private readonly Dictionary<string, string> _secrets = [];

    public bool IsPersistent => false;

    public string? Get(string name) => _secrets.GetValueOrDefault(name);

    public bool Set(string name, string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            _secrets.Remove(name);
        }
        else
        {
            _secrets[name] = value;
        }

        return true;
    }
}

/// <summary>The credential store of the platform the application runs on.</summary>
public static class SystemSecretStore
{
    /// <summary>The name secrets are kept under: a service on macOS and Linux, a prefix of the target on Windows.</summary>
    public const string Service = "UmlWizard";

    public static ISecretStore Create() =>
        OperatingSystem.IsWindows() ? new WindowsCredentialStore()
        : OperatingSystem.IsMacOS() ? new CommandSecretStore(MacKeychain)
        : OperatingSystem.IsLinux() ? new CommandSecretStore(SecretTool)
        : new InMemorySecretStore();

    /// <summary>
    /// The macOS Keychain, through the <c>security</c> tool. The secret is set through its interactive mode, which reads
    /// the command from its input: as an argument, any user of the Mac could see the secret in <c>ps</c> while it runs.
    /// </summary>
    internal static readonly SecretCommands MacKeychain = new(
        Get: name => ("security", ["find-generic-password", "-s", Service, "-a", name, "-w"], null),
        Set: (name, value) => ("security", ["-i"],
            $"add-generic-password -U -s {Quote(Service)} -a {Quote(name)} -w {Quote(value)}\n"),
        Delete: name => ("security", ["delete-generic-password", "-s", Service, "-a", name], null));

    /// <summary>A word of a <c>security -i</c> command line: in double quotes, with <c>\</c> and <c>"</c> escaped.</summary>
    internal static string Quote(string word) => "\"" + word.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";

    /// <summary>The Secret Service of Linux desktops (GNOME Keyring, KWallet), through <c>secret-tool</c> (libsecret-tools).</summary>
    private static readonly SecretCommands SecretTool = new(
        Get: name => ("secret-tool", ["lookup", "service", Service, "key", name], null),
        Set: (name, value) => ("secret-tool", ["store", "--label", $"{Service} {name}", "service", Service, "key", name], value),
        Delete: name => ("secret-tool", ["clear", "service", Service, "key", name], null));
}

/// <summary>The command lines that get, set and delete a secret; the third item is what is written to the command's input.</summary>
internal sealed record SecretCommands(
    Func<string, (string File, string[] Arguments, string? Input)> Get,
    Func<string, string, (string File, string[] Arguments, string? Input)> Set,
    Func<string, (string File, string[] Arguments, string? Input)> Delete);

/// <summary>A credential store reached through a command-line tool (macOS, Linux).</summary>
internal sealed class CommandSecretStore(SecretCommands commands) : ISecretStore
{
    private bool? _available;

    public bool IsPersistent => _available ??= Run(commands.Get("probe")).Started;

    public string? Get(string name)
    {
        var (started, exitCode, output) = Run(commands.Get(name));
        return started && exitCode == 0 && output.TrimEnd('\r', '\n') is { Length: > 0 } secret ? secret : null;
    }

    public bool Set(string name, string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            Run(commands.Delete(name));
            return true;
        }

        // A line break would end the secret early in the tool's input (and on macOS start another command); keys have none.
        if (value.Any(char.IsControl))
        {
            return false;
        }

        // Read back: "security -i" does not report a failed command in its exit code.
        var (started, exitCode, _) = Run(commands.Set(name, value));
        return started && exitCode == 0 && Get(name) == value;
    }

    private static (bool Started, int ExitCode, string Output) Run((string File, string[] Arguments, string? Input) command)
    {
        var info = new ProcessStartInfo(command.File)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = command.Input is not null,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var argument in command.Arguments)
        {
            info.ArgumentList.Add(argument);
        }

        try
        {
            using var process = Process.Start(info);
            if (process is null)
            {
                return (false, -1, string.Empty);
            }

            if (command.Input is not null)
            {
                process.StandardInput.Write(command.Input);
                process.StandardInput.Close();
            }

            var output = process.StandardOutput.ReadToEnd();
            if (!process.WaitForExit(10_000))
            {
                process.Kill();
                return (true, -1, string.Empty);
            }

            return (true, process.ExitCode, output);
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or IOException)
        {
            // The tool is not installed (e.g. a Linux system without libsecret-tools).
            return (false, -1, string.Empty);
        }
    }
}

/// <summary>Windows Credential Manager: generic credentials named <c>UmlWizard:&lt;name&gt;</c>, for the user only.</summary>
internal sealed class WindowsCredentialStore : ISecretStore
{
    private const int GenericCredential = 1;
    private const int PersistLocalMachine = 2;

    public bool IsPersistent => true;

    public string? Get(string name)
    {
        if (!OperatingSystem.IsWindows() || !CredRead(Target(name), GenericCredential, 0, out var pointer))
        {
            return null;
        }

        try
        {
            var credential = Marshal.PtrToStructure<Credential>(pointer);
            return credential.CredentialBlobSize == 0 ? null : Marshal.PtrToStringUni(credential.CredentialBlob, credential.CredentialBlobSize / 2);
        }
        finally
        {
            CredFree(pointer);
        }
    }

    public bool Set(string name, string? value)
    {
        if (!OperatingSystem.IsWindows())
        {
            return false;
        }

        if (string.IsNullOrEmpty(value))
        {
            CredDelete(Target(name), GenericCredential, 0);
            return true;
        }

        var blob = Marshal.StringToCoTaskMemUni(value);
        try
        {
            var credential = new Credential
            {
                Type = GenericCredential,
                TargetName = Target(name),
                CredentialBlobSize = value.Length * 2,
                CredentialBlob = blob,
                Persist = PersistLocalMachine,
                UserName = Environment.UserName,
            };
            return CredWrite(ref credential, 0);
        }
        finally
        {
            Marshal.ZeroFreeCoTaskMemUnicode(blob);
        }
    }

    private static string Target(string name) => $"{SystemSecretStore.Service}:{name}";

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct Credential
    {
        public int Flags;
        public int Type;
        public string TargetName;
        public string? Comment;
        public long LastWritten;
        public int CredentialBlobSize;
        public IntPtr CredentialBlob;
        public int Persist;
        public int AttributeCount;
        public IntPtr Attributes;
        public string? TargetAlias;
        public string? UserName;
    }

    [DllImport("advapi32.dll", EntryPoint = "CredReadW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredRead(string target, int type, int flags, out IntPtr credential);

    [DllImport("advapi32.dll", EntryPoint = "CredWriteW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredWrite(ref Credential credential, int flags);

    [DllImport("advapi32.dll", EntryPoint = "CredDeleteW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredDelete(string target, int type, int flags);

    [DllImport("advapi32.dll")]
    private static extern void CredFree(IntPtr buffer);
}

/// <summary>The API key of an AI service: from the credential store, else from the service's environment variable.</summary>
public static class AiKeys
{
    public static string? Get(ISecretStore store, Ai.AiServiceSettings settings) =>
        store.Get(settings.SecretName) ??
        (settings.KeyEnvironmentVariable is { } variable && Environment.GetEnvironmentVariable(variable) is { Length: > 0 } key ? key : null);
}
