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
    /// <summary>
    /// The tests check English text: without this, an operating system in German, Spanish or French would show the
    /// translations.
    /// </summary>
    [System.Runtime.CompilerServices.ModuleInitializer]
    public static void UseEnglish() => Loc.SetDisplayLanguage("en");

    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>()
        .UseSkia()
        .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
}
