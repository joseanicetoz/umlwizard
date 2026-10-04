using Avalonia.Data.Converters;
using Avalonia.Input;
using UmlWizard.App.Resources;

namespace UmlWizard.App;

/// <summary>
/// The keyboard shortcuts of the application. They use the platform's command key: ⌘ (Command) on macOS and Ctrl
/// elsewhere, so every menu, key binding, tooltip and the canvas agree.
/// </summary>
public static class Shortcuts
{
    private static readonly bool IsMacOS = OperatingSystem.IsMacOS();

    /// <summary>The modifier of command shortcuts on this platform.</summary>
    public static KeyModifiers CommandModifier { get; } = CommandModifierFor(IsMacOS);

    public static KeyGesture NewProject { get; } = Command(Key.N, KeyModifiers.Shift);

    public static KeyGesture OpenProject { get; } = Command(Key.O);

    public static KeyGesture SaveProject { get; } = Command(Key.S);

    public static KeyGesture SaveProjectAs { get; } = Command(Key.S, KeyModifiers.Shift);

    public static KeyGesture Undo { get; } = Command(Key.Z);

    /// <summary>⌘⇧Z on macOS, Ctrl+Y elsewhere; the canvas accepts both forms on every platform.</summary>
    public static KeyGesture Redo { get; } = RedoFor(IsMacOS);

    public static KeyGesture Cut { get; } = Command(Key.X);

    public static KeyGesture Copy { get; } = Command(Key.C);

    public static KeyGesture Paste { get; } = Command(Key.V);

    /// <summary>The ⌫ key on macOS keyboards, which have no separate Delete key; Delete elsewhere.</summary>
    public static KeyGesture Delete { get; } = DeleteFor(IsMacOS);

    /// <summary>Ctrl+Alt+L (⌘⌥L on macOS), as Solution Explorer in Visual Studio.</summary>
    public static KeyGesture ToggleProjectExplorer { get; } = Command(Key.L, KeyModifiers.Alt);

    /// <summary>Shows or hides the AI chat panel, next to the Project Explorer's shortcut.</summary>
    public static KeyGesture ToggleAiChat { get; } = Command(Key.I, KeyModifiers.Alt);

    public static KeyGesture ZoomIn { get; } = Command(Key.OemPlus);

    public static KeyGesture ZoomOut { get; } = Command(Key.OemMinus);

    public static KeyGesture ZoomReset { get; } = Command(Key.D0);

    public static KeyGesture BringToFront { get; } = Command(Key.OemCloseBrackets, KeyModifiers.Shift);

    public static KeyGesture BringForward { get; } = Command(Key.OemCloseBrackets);

    public static KeyGesture SendBackward { get; } = Command(Key.OemOpenBrackets);

    public static KeyGesture SendToBack { get; } = Command(Key.OemOpenBrackets, KeyModifiers.Shift);

    /// <summary>Opens the Preferences window: Ctrl+, (⌘, on macOS), as in most applications.</summary>
    public static KeyGesture Preferences { get; } = Command(Key.OemComma);

    /// <summary>Opens the User Manual: F1, the usual help key, on every platform.</summary>
    public static KeyGesture UserManual { get; } = new(Key.F1);

    /// <summary>The command key as the platform displays it: "Ctrl", or "⌘" on macOS.</summary>
    public static string CommandKeyName => Format(new KeyGesture(Key.None, CommandModifier));

    /// <summary>True when the command key of this platform is held.</summary>
    public static bool IsCommand(KeyModifiers modifiers) => modifiers.HasFlag(CommandModifier);

    /// <summary>A gesture as the platform displays it, e.g. "Ctrl+S" on Windows and "⌘S" on macOS.</summary>
    public static string Format(KeyGesture gesture) => gesture.ToString("p", null);

    internal static KeyModifiers CommandModifierFor(bool isMacOS) => isMacOS ? KeyModifiers.Meta : KeyModifiers.Control;

    internal static KeyGesture RedoFor(bool isMacOS) =>
        isMacOS ? new(Key.Z, KeyModifiers.Meta | KeyModifiers.Shift) : new(Key.Y, KeyModifiers.Control);

    internal static KeyGesture DeleteFor(bool isMacOS) => new(isMacOS ? Key.Back : Key.Delete);

    private static KeyGesture Command(Key key, KeyModifiers extra = KeyModifiers.None) => new(key, CommandModifier | extra);

    /// <summary>
    /// Formats a tooltip as "text (shortcut)": bind a <see cref="KeyGesture"/> and pass the text as the parameter.
    /// </summary>
    public static IValueConverter Tip { get; } = new FuncValueConverter<KeyGesture?, string?, string?>(
        (gesture, text) => gesture is null ? text : Loc.Format(Strings.Tip_WithShortcut, text, Format(gesture)));
}
