using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using UmlWizard.App.ViewModels;
using UmlWizard.App.Views;

namespace UmlWizard.App.Tests;

public sealed class ShortcutsTests
{
    [Fact]
    public void MacOS_UsesCommand_RedoIsShiftZ_AndDeleteIsBackspace()
    {
        Assert.Equal(KeyModifiers.Meta, Shortcuts.CommandModifierFor(isMacOS: true));
        Assert.Equal(new KeyGesture(Key.Z, KeyModifiers.Meta | KeyModifiers.Shift), Shortcuts.RedoFor(isMacOS: true));
        Assert.Equal(new KeyGesture(Key.Back), Shortcuts.DeleteFor(isMacOS: true));
    }

    [Fact]
    public void WindowsAndLinux_UseCtrl_RedoIsY_AndDeleteIsDelete()
    {
        Assert.Equal(KeyModifiers.Control, Shortcuts.CommandModifierFor(isMacOS: false));
        Assert.Equal(new KeyGesture(Key.Y, KeyModifiers.Control), Shortcuts.RedoFor(isMacOS: false));
        Assert.Equal(new KeyGesture(Key.Delete), Shortcuts.DeleteFor(isMacOS: false));
    }

    [Fact]
    public void AllCommandShortcuts_UseThePlatformCommandKey()
    {
        var expected = Shortcuts.CommandModifierFor(OperatingSystem.IsMacOS());
        Assert.Equal(expected, Shortcuts.CommandModifier);
        KeyGesture[] commands =
        [
            Shortcuts.NewProject, Shortcuts.OpenProject, Shortcuts.SaveProject, Shortcuts.SaveProjectAs, Shortcuts.Undo,
            Shortcuts.Redo, Shortcuts.Cut, Shortcuts.Copy, Shortcuts.Paste, Shortcuts.ZoomIn, Shortcuts.ZoomOut,
            Shortcuts.ZoomReset, Shortcuts.BringToFront, Shortcuts.BringForward, Shortcuts.SendBackward, Shortcuts.SendToBack,
        ];
        Assert.All(commands, g => Assert.True(g.KeyModifiers.HasFlag(expected), g.ToString()));
        Assert.Equal(new KeyGesture(Key.S, expected), Shortcuts.SaveProject);
        Assert.Equal(new KeyGesture(Key.S, expected | KeyModifiers.Shift), Shortcuts.SaveProjectAs);
    }

    [AvaloniaFact]
    public void MenusKeyBindingsAndTooltips_UseTheSharedShortcuts()
    {
        using var f = new ExplorerFixture();
        var window = new MainWindow { DataContext = f.Main };
        window.Show();
        f.Main.OpenDiagramCommand.Execute((DiagramItemViewModel)f.Node("A"));
        Dispatcher.UIThread.RunJobs();

        var menuItems = window.GetLogicalDescendants().OfType<MenuItem>().ToList();
        Assert.Same(Shortcuts.SaveProject, menuItems.Single(m => (string?)m.Header == "_Save Project").InputGesture);
        Assert.Same(Shortcuts.Redo, menuItems.Single(m => (string?)m.Header == "_Redo").InputGesture);
        Assert.Same(Shortcuts.Delete, menuItems.Single(m => (string?)m.Header == "_Delete").InputGesture);
        Assert.Contains(window.KeyBindings, b => b.Gesture == Shortcuts.SaveProjectAs && b.Command == f.Main.SaveProjectAsCommand);

        var toolbar = window.GetLogicalDescendants().OfType<DiagramToolbar>().Single();
        var tips = toolbar.GetLogicalDescendants().OfType<Button>().Append(window.FindControl<Button>("SaveProjectButton")!)
            .Select(b => ToolTip.GetTip(b) as string).ToList();
        Assert.Contains($"Save project ({Shortcuts.Format(Shortcuts.SaveProject)})", tips);
        Assert.Contains($"Paste ({Shortcuts.Format(Shortcuts.Paste)})", tips);
        if (!OperatingSystem.IsMacOS())
        {
            Assert.Contains("Save project (Ctrl+S)", tips);
            Assert.Contains("Bring to front (Ctrl+Shift+])", tips);
        }

        window.Close();
    }
}
