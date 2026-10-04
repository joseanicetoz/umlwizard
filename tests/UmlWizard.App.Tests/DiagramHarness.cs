using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using Avalonia.VisualTree;
using UmlWizard.App.Controls;
using UmlWizard.App.ViewModels;
using UmlWizard.App.Views;
using UmlWizard.Core.Model;

namespace UmlWizard.App.Tests;

/// <summary>A diagram view hosted in a headless window, with helpers to send input in world coordinates.</summary>
internal sealed class DiagramHarness
{
    public DiagramHarness(DiagramType type)
    {
        Services = new FakeServices();
        var document = DiagramDocument.Create("Test", type);
        ViewModel = new DiagramViewModel(
            document,
            Services,
            Services,
            _ => Task.CompletedTask);
        Window = new Window { Width = 1000, Height = 700, Content = new DiagramView { DataContext = ViewModel } };
        Window.Show();
        Dispatcher.UIThread.RunJobs();
        Canvas = Window.GetVisualDescendants().OfType<DiagramCanvas>().Single();
    }

    public FakeServices Services { get; }

    public DiagramViewModel ViewModel { get; }

    public Window Window { get; }

    public DiagramCanvas Canvas { get; }

    public TextBox InlineEditor => Canvas.GetVisualChildren().OfType<TextBox>().Single();

    /// <summary>Converts a world point (zoom 1, no offset) to window coordinates.</summary>
    public Point ToWindow(double x, double y) => Canvas.TranslatePoint(new Point(x, y), Window)!.Value;

    public void Click(double x, double y, Avalonia.Input.MouseButton button = Avalonia.Input.MouseButton.Left)
    {
        var point = ToWindow(x, y);
        Window.MouseDown(point, button);
        Window.MouseUp(point, button);
        Dispatcher.UIThread.RunJobs();
    }

    public void DoubleClick(double x, double y)
    {
        Click(x, y);
        Click(x, y);
    }

    public void Drag(double fromX, double fromY, double toX, double toY)
    {
        var from = ToWindow(fromX, fromY);
        var to = ToWindow(toX, toY);
        Window.MouseDown(from, Avalonia.Input.MouseButton.Left);
        Window.MouseMove(new Point((from.X + to.X) / 2, (from.Y + to.Y) / 2));
        Window.MouseMove(to);
        Window.MouseUp(to, Avalonia.Input.MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
    }

    public void Type(string text)
    {
        Window.KeyTextInput(text);
        Dispatcher.UIThread.RunJobs();
    }

    public void Press(Avalonia.Input.Key key, Avalonia.Input.RawInputModifiers modifiers = Avalonia.Input.RawInputModifiers.None)
    {
        Window.KeyPressQwerty(ToPhysicalKey(key), modifiers);
        Window.KeyReleaseQwerty(ToPhysicalKey(key), modifiers);
        Dispatcher.UIThread.RunJobs();
    }

    /// <summary>Presses a key down without releasing it, as when it is held and repeats.</summary>
    public void KeyDown(Avalonia.Input.Key key, Avalonia.Input.RawInputModifiers modifiers = Avalonia.Input.RawInputModifiers.None)
    {
        Window.KeyPressQwerty(ToPhysicalKey(key), modifiers);
        Dispatcher.UIThread.RunJobs();
    }

    public void KeyUp(Avalonia.Input.Key key, Avalonia.Input.RawInputModifiers modifiers = Avalonia.Input.RawInputModifiers.None)
    {
        Window.KeyReleaseQwerty(ToPhysicalKey(key), modifiers);
        Dispatcher.UIThread.RunJobs();
    }

    private static Avalonia.Input.PhysicalKey ToPhysicalKey(Avalonia.Input.Key key) => key switch
    {
        Avalonia.Input.Key.Enter => Avalonia.Input.PhysicalKey.Enter,
        Avalonia.Input.Key.Escape => Avalonia.Input.PhysicalKey.Escape,
        Avalonia.Input.Key.Delete => Avalonia.Input.PhysicalKey.Delete,
        Avalonia.Input.Key.Left => Avalonia.Input.PhysicalKey.ArrowLeft,
        Avalonia.Input.Key.Right => Avalonia.Input.PhysicalKey.ArrowRight,
        Avalonia.Input.Key.Up => Avalonia.Input.PhysicalKey.ArrowUp,
        Avalonia.Input.Key.Down => Avalonia.Input.PhysicalKey.ArrowDown,
        _ => throw new ArgumentOutOfRangeException(nameof(key)),
    };
}
