using Avalonia;
using Avalonia.Headless;
using Avalonia.VisualTree;
using UmlWizard.App.Controls;
using UmlWizard.App.Services;
using UmlWizard.App.ViewModels;
using UmlWizard.App.ViewModels.Dialogs;
using UmlWizard.App.Views;
using UmlWizard.Core.Model;

[assembly: AvaloniaTestApplication(typeof(UmlWizard.App.Tests.TestAppBuilder))]

namespace UmlWizard.App.Tests;

public static class TestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>()
        .UseSkia()
        .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
}
