using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Styling;
using UmlWizard.App.Resources;
using UmlWizard.App.Services;
using UmlWizard.App.ViewModels;
using UmlWizard.App.Views;
using UmlWizard.Core.Model;

namespace UmlWizard.App;

public sealed class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);

        // The theme's own font, before a preference replaces it: Default in Preferences, and its preview, use it.
        _defaultFontFamily = TryGetResource("ContentControlThemeFontFamily", null, out var family) ? family : FontFamily.Default;
        _defaultFontSize = TryGetResource("ControlContentThemeFontSize", null, out var size) ? size : PreferencesViewModel.DefaultFontSize;
        DefaultUiFontFamily = _defaultFontFamily as FontFamily ?? FontFamily.Default;
    }

    /// <summary>The font of the user interface when no preference sets one.</summary>
    internal static FontFamily DefaultUiFontFamily { get; private set; } = FontFamily.Default;

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // The display language is applied before any window is made: the views take their text when they load.
            var settings = new SettingsFileStore(SettingsFileStore.DefaultFilePath);
            Loc.SetDisplayLanguage(settings.Load().DisplayLanguage);

            var window = new MainWindow();
            var services = new WindowServices(window);
            var viewModel = new MainWindowViewModel(
                services, services, new RecentProjectsFileStore(RecentProjectsFileStore.DefaultFilePath),
                settings: settings, secrets: SystemSecretStore.Create(),
                projectStates: new ProjectStateFileStore(ProjectStateFileStore.DefaultFolder));

            // The saved theme is applied before the window shows, so it does not flash in the other one.
            RequestedThemeVariant = ToThemeVariant(viewModel.Theme);
            ApplyUiFont(viewModel.UiFontFamily, viewModel.UiFontSize);
            viewModel.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(MainWindowViewModel.Theme))
                {
                    RequestedThemeVariant = ToThemeVariant(viewModel.Theme);
                }
                else if (e.PropertyName is nameof(MainWindowViewModel.UiFontFamily) or nameof(MainWindowViewModel.UiFontSize))
                {
                    ApplyUiFont(viewModel.UiFontFamily, viewModel.UiFontSize);
                }
            };
            window.DataContext = viewModel;

            // macOS shows an application menu in the menu bar; without one of its own it offers "About Avalonia".
            if (OperatingSystem.IsMacOS())
            {
                NativeMenu.SetMenu(this, CreateMacAppMenu(viewModel));
            }

            // The splash screen shows first; once it has been up for a moment, the main window replaces it.
            var splash = new SplashWindow { DataContext = new AboutViewModel(services) };
            desktop.MainWindow = splash;
            splash.Opened += async (_, _) =>
            {
                await Task.Delay(SplashWindow.MinimumDuration);
                desktop.MainWindow = window;
                window.Show();
                splash.Close();
            };

            // A project file passed on the command line is opened at start-up.
            var projectPath = desktop.Args?.FirstOrDefault(UmlProject.IsProjectFile);
            if (projectPath is not null)
            {
                window.Opened += async (_, _) => await viewModel.OpenProjectFileAsync(Path.GetFullPath(projectPath));
            }
        }

        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>
    /// The items of the macOS application menu (the one named after the app). Avalonia adds the standard items after
    /// them: Services, Hide, Hide Others, Show All and Quit, which closes the window as the Exit command does.
    /// </summary>
    internal static NativeMenu CreateMacAppMenu(MainWindowViewModel viewModel) =>
    [
        new NativeMenuItem(Strings.AppMenu_About) { Command = viewModel.AboutCommand },
        new NativeMenuItemSeparator(),
        new NativeMenuItem(Strings.AppMenu_Preferences) { Command = viewModel.PreferencesCommand, Gesture = Shortcuts.Preferences },
    ];

    /// <summary>
    /// Applies the font of the user interface (Preferences → Appearance) to every window: the Fluent theme takes the
    /// font family and size of controls from these resources. <c>null</c> puts the default back.
    /// </summary>
    internal void ApplyUiFont(string? family, double? size)
    {
        Resources["ContentControlThemeFontFamily"] = family is null ? _defaultFontFamily : new FontFamily(family);
        Resources["ControlContentThemeFontSize"] = size ?? _defaultFontSize;
    }

    private object? _defaultFontFamily;
    private object? _defaultFontSize;

    /// <summary>The Avalonia theme variant of a theme choice; <see cref="ThemeVariant.Default"/> follows the operating system.</summary>
    internal static ThemeVariant ToThemeVariant(AppTheme theme) => theme switch
    {
        AppTheme.Light => ThemeVariant.Light,
        AppTheme.Dark => ThemeVariant.Dark,
        _ => ThemeVariant.Default,
    };
}
