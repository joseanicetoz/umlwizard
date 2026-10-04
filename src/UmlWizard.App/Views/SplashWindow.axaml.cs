using Avalonia.Controls;

namespace UmlWizard.App.Views;

/// <summary>The window shown briefly at start-up, before the main window.</summary>
public sealed partial class SplashWindow : Window
{
    /// <summary>How long the splash screen stays up at least.</summary>
    public static readonly TimeSpan MinimumDuration = TimeSpan.FromSeconds(3);

    public SplashWindow() => InitializeComponent();
}
