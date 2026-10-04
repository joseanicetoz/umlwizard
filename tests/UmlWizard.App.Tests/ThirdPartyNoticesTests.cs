using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using UmlWizard.App.ViewModels;
using UmlWizard.App.Views;

namespace UmlWizard.App.Tests;

public sealed class ThirdPartyNoticesTests
{
    private static string Notices => File.ReadAllText(AboutViewModel.NoticesPath);

    /// <summary>The NuGet packages the application ships with, as recorded in its dependency manifest.</summary>
    /// <remarks>
    /// Only packages that put files next to the application count: Visual Studio's build also lists packages that
    /// contribute nothing, such as Roslyn's analyzers or System.* packages the .NET runtime already provides.
    /// </remarks>
    private static IEnumerable<(string Id, string Version)> ShippedPackages()
    {
        using var deps = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "UmlWizard.deps.json")));
        var target = deps.RootElement.GetProperty("targets").EnumerateObject().First().Value;
        foreach (var library in deps.RootElement.GetProperty("libraries").EnumerateObject())
        {
            if (library.Value.GetProperty("type").GetString() == "package" &&
                target.TryGetProperty(library.Name, out var assets) &&
                assets.EnumerateObject().Any(a => a.Name is "runtime" or "native" or "runtimeTargets" or "resources"))
            {
                var parts = library.Name.Split('/');
                yield return (parts[0], parts[1]);
            }
        }
    }

    [Fact]
    public void NoticesFile_IsInstalledNextToTheApplication()
    {
        Assert.True(File.Exists(AboutViewModel.NoticesPath));
        Assert.True(new AboutViewModel(new FakeServices()).HasNotices);
    }

    [Fact]
    public void EveryShippedPackage_AndItsVersion_IsInTheNotices()
    {
        var notices = Notices;
        var packages = ShippedPackages().ToList();
        Assert.NotEmpty(packages);

        foreach (var (id, version) in packages)
        {
            // Native asset packages are listed per family, e.g. "SkiaSharp.NativeAssets.* (Win32, macOS, Linux, …)".
            var nativeAssets = id.IndexOf(".NativeAssets.", StringComparison.Ordinal);
            var listed = nativeAssets < 0
                ? notices.Contains(id, StringComparison.Ordinal)
                : notices.Contains(id[..nativeAssets] + ".NativeAssets.*", StringComparison.Ordinal) &&
                  notices.Contains(id[(nativeAssets + ".NativeAssets.".Length)..], StringComparison.Ordinal);
            Assert.True(listed, $"{id} is shipped but not in THIRD-PARTY-NOTICES.txt");
            Assert.True(notices.Contains(version, StringComparison.Ordinal), $"{id} {version}: the version is not in THIRD-PARTY-NOTICES.txt");
        }
    }

    [Fact]
    public void Notices_HoldTheFullLicenseTexts()
    {
        var notices = Notices;

        Assert.Contains("Pictogrammers Free License", notices);
        Assert.Contains("TERMS AND CONDITIONS FOR USE, REPRODUCTION, AND DISTRIBUTION", notices); // Apache 2.0
        Assert.Contains("SIL OPEN FONT LICENSE Version 1.1", notices, StringComparison.OrdinalIgnoreCase); // Inter
        Assert.Contains("Copyright (c) 2011 Google Inc.", notices); // Skia
        Assert.Contains("HarfBuzz is licensed under the so-called \"Old MIT\" license", notices);
        Assert.Contains("The ANGLE Project Authors", notices);
        Assert.Contains("Copyright (c) AvaloniaUI", notices);
        Assert.Contains("Copyright (c) 2021 SKProCH", notices);
        Assert.Contains("Permission is hereby granted, free of charge", notices); // MIT
    }

    [Fact]
    public void EveryAcknowledgement_IsCoveredByTheNotices()
    {
        var notices = Notices;
        var about = new AboutViewModel(new FakeServices());

        Assert.Equal("Material Design Icons (Pictogrammers)", about.Acknowledgements[0].Name);
        foreach (var acknowledgement in about.Acknowledgements)
        {
            // "SkiaSharp and Skia" is covered by the "SkiaSharp" section, and so on.
            Assert.Contains(acknowledgement.Name.Split(" (")[0].Split(" and ")[0], notices);
            Assert.True(acknowledgement.Uri.IsAbsoluteUri);
        }
    }

    [AvaloniaFact]
    public void AboutDialog_ShowsAcknowledgements_AndTheNoticesButton()
    {
        var about = new AboutViewModel(new FakeServices());
        var window = new AboutWindow { DataContext = about };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        // The acknowledgements are on their own tab.
        window.FindControl<TabControl>("AboutTabs")!.SelectedIndex = 2;
        Dispatcher.UIThread.RunJobs();
        var section = window.GetLogicalDescendants().OfType<StackPanel>().Distinct().Single(p => p.Name == "AcknowledgementsSection");
        var links = section.GetLogicalDescendants().OfType<HyperlinkButton>().ToList();
        Assert.Equal(about.Acknowledgements.Select(a => a.Name), links.Select(l => l.Content as string));
        Assert.Equal(about.Acknowledgements.Select(a => a.Uri), links.Select(l => l.NavigateUri));
        Assert.Contains(section.GetLogicalDescendants().OfType<TextBlock>(), t => t.Text == "Apache License 2.0");
        Assert.True(window.FindControl<Button>("NoticesButton")!.IsEffectivelyVisible);
        window.Close();
    }
}
