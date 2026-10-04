using Avalonia;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Styling;
using Avalonia.Threading;
using SkiaSharp;
using UmlWizard.App.Services;
using UmlWizard.App.ViewModels;
using UmlWizard.Core.Model;
using UmlWizard.Core.Rendering;

namespace UmlWizard.App.Tests;

public sealed class ThemeTests
{
    [AvaloniaFact]
    public void Theme_IsLoadedFromSettings_AndSavedWhenChosenInTheMenu()
    {
        var services = new FakeServices();
        var store = new InMemorySettingsStore(new AppSettings { Theme = AppTheme.Light });
        var main = new MainWindowViewModel(services, services, settings: store);
        Assert.Equal(AppTheme.Light, main.Theme);
        Assert.True(main.IsLightTheme);

        main.IsDarkTheme = true;
        // The radio items the menu unchecks do not change the choice.
        main.IsLightTheme = false;

        Assert.Equal(AppTheme.Dark, main.Theme);
        Assert.Equal((false, false, true), (main.IsSystemTheme, main.IsLightTheme, main.IsDarkTheme));
        Assert.Equal(AppTheme.Dark, store.Load().Theme);
    }

    [AvaloniaFact]
    public void Theme_FollowsTheSystem_ByDefault()
    {
        var services = new FakeServices();
        var main = new MainWindowViewModel(services, services);

        Assert.Equal(AppTheme.System, main.Theme);
        Assert.Equal(ThemeVariant.Default, App.ToThemeVariant(main.Theme));
        Assert.Equal(ThemeVariant.Dark, App.ToThemeVariant(AppTheme.Dark));
        Assert.Equal(ThemeVariant.Light, App.ToThemeVariant(AppTheme.Light));
    }

    [Fact]
    public void SettingsFile_RoundTrips_AndFallsBackToDefaults()
    {
        var path = Path.Combine(Path.GetTempPath(), $"umlwizard-settings-{Guid.NewGuid():N}", "settings.json");
        try
        {
            var store = new SettingsFileStore(path);
            Assert.Equal(AppTheme.System, store.Load().Theme);

            store.Save(new AppSettings { Theme = AppTheme.Dark });
            Assert.Contains("\"Dark\"", File.ReadAllText(path));
            Assert.Equal(AppTheme.Dark, new SettingsFileStore(path).Load().Theme);

            File.WriteAllText(path, "{ not json");
            Assert.Equal(AppTheme.System, store.Load().Theme);
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(path)!, recursive: true);
        }
    }

    [AvaloniaFact]
    public void Canvas_IsDrawnInTheDarkColours_InDarkMode_AndRedrawnWhenTheThemeChanges()
    {
        var h = new DiagramHarness(DiagramType.Class);

        h.Window.RequestedThemeVariant = ThemeVariant.Dark;
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(RenderTheme.Dark.CanvasBackground, CanvasPixel(h));
        Assert.Same(RenderTheme.Dark, h.ViewModel.Renderer.Theme);

        h.Window.RequestedThemeVariant = ThemeVariant.Light;
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(RenderTheme.Light.CanvasBackground, CanvasPixel(h));
        Assert.Same(RenderTheme.Light, h.ViewModel.Renderer.Theme);
    }

    /// <summary>A pixel of the empty canvas, between the grid dots.</summary>
    private static SKColor CanvasPixel(DiagramHarness h)
    {
        // The headless renderer sometimes draws a visual invalidated by a theme change one frame late: render one first.
        h.Window.CaptureRenderedFrame();
        var frame = h.Window.CaptureRenderedFrame()!;
        using var stream = new MemoryStream();
        frame.Save(stream);
        stream.Position = 0;
        using var bitmap = SKBitmap.Decode(stream);
        var point = h.ToWindow(310, 210);
        return bitmap.GetPixel((int)point.X, (int)point.Y);
    }
}
