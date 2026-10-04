using Avalonia.Controls;
using Avalonia.Interactivity;
using UmlWizard.App.ViewModels;

namespace UmlWizard.App.Views;

/// <summary>The Preferences window; its changes apply as they are made (see <see cref="PreferencesViewModel"/>).</summary>
public sealed partial class PreferencesWindow : Window
{
    /// <summary>Set once closing without the AI warning accepted was confirmed.</summary>
    private bool _canClose;

    public PreferencesWindow()
    {
        InitializeComponent();

        Closing += OnClosing;
    }

    private async void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        if (DataContext is not PreferencesViewModel preferences)
        {
            return;
        }

        // The API key is kept when the window closes, however it is closed, rather than on every key stroke.
        preferences.SaveApiKey();
        if (_canClose || preferences.AiWarningAccepted)
        {
            return;
        }

        // Without the AI warning accepted, the AI service is turned off first; when one is set up the user is asked,
        // and may cancel to stay.
        var canClose = preferences.CanCloseAsync();
        if (canClose.IsCompletedSuccessfully && canClose.Result)
        {
            return;
        }

        e.Cancel = true;
        if (await canClose)
        {
            _canClose = true;
            Close();
        }
    }

    private void OnClose(object? sender, RoutedEventArgs e) => Close();
}
