using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using UmlWizard.App.ViewModels;

namespace UmlWizard.App.Views;

public sealed partial class AboutWindow : Window
{
    public AboutWindow() => InitializeComponent();

    private void OnOk(object? sender, RoutedEventArgs e) => Close();

    /// <summary>Opens the third-party notices in the system's viewer for text files.</summary>
    private async void OnShowNotices(object? sender, RoutedEventArgs e) =>
        await Launcher.LaunchFileInfoAsync(new FileInfo(AboutViewModel.NoticesPath));
}
