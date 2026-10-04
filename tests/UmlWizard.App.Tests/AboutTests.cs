using Avalonia;
using System.Runtime.InteropServices;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using UmlWizard.App.Services;
using UmlWizard.App.ViewModels;
using UmlWizard.App.Views;
using UmlWizard.CodeGeneration.Plugins;
using UmlWizard.CodeImport.Plugins;

namespace UmlWizard.App.Tests;

public sealed class AboutTests
{
    [AvaloniaFact]
    public void MacAppMenu_IsNamedAfterTheApp_AndOffersAboutAndPreferences()
    {
        var services = new FakeServices();
        var main = new MainWindowViewModel(services, services);

        // macOS uses the application's name for Hide and Quit in the menu it adds after these items.
        Assert.Equal("UML Wizard", Application.Current!.Name);
        var items = App.CreateMacAppMenu(main).Items;
        Assert.Equal(3, items.Count);
        var about = Assert.IsType<NativeMenuItem>(items[0]);
        Assert.Equal("About UML Wizard", about.Header);
        Assert.Same(main.AboutCommand, about.Command);
        Assert.IsType<NativeMenuItemSeparator>(items[1]);
        var preferences = Assert.IsType<NativeMenuItem>(items[2]);
        Assert.Equal("Preferences…", preferences.Header);
        Assert.Same(main.PreferencesCommand, preferences.Command);
        Assert.Same(Shortcuts.Preferences, preferences.Gesture);
    }

    [AvaloniaFact]
    public void AboutViewModel_ReportsNameVersionAndOperatingSystem()
    {
        var about = new AboutViewModel(new FakeServices());

        Assert.Equal("UML Wizard", about.ProductName);
        Assert.Equal("1.0.0", about.Version);
        Assert.Equal(RuntimeInformation.OSDescription.Trim(), about.OperatingSystem);
        Assert.StartsWith(".NET", about.Runtime);
        Assert.StartsWith("Avalonia 11", about.UiFramework);
        Assert.Equal("About UML Wizard", about.Title);
        Assert.Contains("UML Wizard 1.0.0", about.Details);
        Assert.StartsWith("UML 2.5.1", about.UmlSupport);
        Assert.Contains($"UML: {about.UmlSupport}", about.Details);
        Assert.Contains(about.OperatingSystem, about.Details);
    }

    [AvaloniaFact]
    public void CopyDetails_PutsDetailsOnClipboard()
    {
        var services = new FakeServices();
        var about = new AboutViewModel(services);

        about.CopyDetailsCommand.Execute(null);

        Assert.Equal(about.Details, services.ClipboardText);
        Assert.True(about.Copied);
    }

    [AvaloniaFact]
    public void AboutCommand_ShowsAboutDialog()
    {
        var services = new FakeServices();
        var main = new MainWindowViewModel(services, services);

        main.AboutCommand.Execute(null);

        Assert.Single(services.ShownAbout);
    }

    [AvaloniaFact]
    public void AboutWindow_HasGeneralLanguagesAndAcknowledgementsTabs_BelowTheDescription_AndKeepsItsSize()
    {
        var about = new AboutViewModel(new FakeServices());
        var window = new AboutWindow { DataContext = about };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var tabs = window.FindControl<TabControl>("AboutTabs")!;
        Assert.Equal(["General", "Languages", "Acknowledgements"], tabs.Items.OfType<TabItem>().Select(t => t.Header as string));
        Assert.Equal(0, tabs.SelectedIndex);

        // Compact 16 px headers rather than Fluent's 24 px light ones.
        Assert.All(tabs.Items.OfType<TabItem>(), t =>
        {
            Assert.Equal(16, t.FontSize);
            Assert.Equal(Avalonia.Media.FontWeight.Normal, t.FontWeight);
            Assert.Equal(36, t.Bounds.Height, 0.5);
        });

        // Name, version and description stay above the tabs.
        var description = window.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Text == about.Description);
        double Top(Visual v) => v.TranslatePoint(default, window)!.Value.Y;
        Assert.True(Top(description) < Top(tabs));
        Assert.False(tabs.IsVisualAncestorOf(description));

        // General shows the system details and not the acknowledgements.
        var general = window.GetVisualDescendants().OfType<TextBlock>().Where(t => tabs.IsVisualAncestorOf(t)).Select(t => t.Text).ToList();
        Assert.Contains(about.OperatingSystem, general);
        Assert.DoesNotContain(general, t => t?.Contains("open source components") == true);
        var size = window.Bounds.Size;

        tabs.SelectedIndex = 1;
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(size, window.Bounds.Size);

        tabs.SelectedIndex = 2;
        Dispatcher.UIThread.RunJobs();

        var acknowledgements = window.GetVisualDescendants().OfType<TextBlock>().Where(t => tabs.IsVisualAncestorOf(t)).Select(t => t.Text).ToList();
        Assert.Contains(acknowledgements, t => t?.Contains("open source components") == true);
        Assert.DoesNotContain(about.OperatingSystem, acknowledgements);
        Assert.Equal(size, window.Bounds.Size);

        // Everything fits without scrolling.
        var scroller = window.GetVisualDescendants().OfType<ScrollViewer>().Single(s => s.IsEffectivelyVisible && tabs.IsVisualAncestorOf(s));
        Assert.True(scroller.Extent.Height <= scroller.Viewport.Height + 0.5, $"{scroller.Extent.Height} > {scroller.Viewport.Height}");
        window.Close();
    }

    [AvaloniaFact]
    public void GeneralTab_ShowsTheLicence_AndTheWebsitePlaceholder_BelowTheUiFramework()
    {
        var about = new AboutViewModel(new FakeServices());
        var window = new AboutWindow { DataContext = about };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(("Apache License 2.0", "Not specified yet"), (about.Licence, about.Website));
        Assert.Null(about.WebsiteUri);

        var labels = window.GetVisualDescendants().OfType<TextBlock>().Where(t => t.Classes.Contains("caption")).ToList();
        double Top(string label) => labels.Single(t => t.Text == label).TranslatePoint(default, window)!.Value.Y;
        Assert.True(Top("UI framework") < Top("Licence") && Top("Licence") < Top("Website"));

        Assert.Equal("Apache License 2.0", window.FindControl<SelectableTextBlock>("LicenceText")!.Text);
        Assert.True(window.FindControl<SelectableTextBlock>("WebsiteText")!.IsEffectivelyVisible);
        Assert.False(window.FindControl<HyperlinkButton>("WebsiteLink")!.IsEffectivelyVisible);

        Assert.Contains("Licence: Apache License 2.0", about.Details);
        Assert.Contains("Website: Not specified yet", about.Details);
        window.Close();
    }

    [AvaloniaFact]
    public void LanguagesTab_ListsEveryInstalledPlugin_WithItsVersionAndProvider()
    {
        var about = new AboutViewModel(new FakeServices(), InstalledPlugins.CodeGenerators, InstalledPlugins.CodeImporters);

        // A row for every code generator and every code importer, by language, generation first.
        Assert.Equal(InstalledPlugins.CodeGenerators.Generators.Count + InstalledPlugins.CodeImporters.Importers.Count, about.LanguagePlugins.Count);
        var csharp = about.LanguagePlugins.Where(p => p.Language == "C#").ToList();
        Assert.Equal(["Code generation", "Code import"], csharp.Select(p => p.Kind));
        Assert.Equal(["UmlWizard.CodeGeneration.CSharp.dll", "UmlWizard.CodeImport.CSharp.dll"], csharp.Select(p => p.File));
        Assert.Equal(about.LanguagePlugins.OrderBy(p => p.Language, StringComparer.CurrentCultureIgnoreCase).Select(p => p.Language), about.LanguagePlugins.Select(p => p.Language));

        // The built-in plug-ins are UML Wizard's, at its version.
        Assert.All(about.LanguagePlugins, p => Assert.Equal("UML Wizard", p.Provider));
        Assert.All(about.LanguagePlugins, p => Assert.Matches(@"^\d+\.\d+\.\d+", p.Version));
        Assert.Contains("C# code generation", about.Details);

        var window = new AboutWindow { DataContext = about };
        window.Show();
        var tabs = window.FindControl<TabControl>("AboutTabs")!;
        Assert.Equal(["General", "Languages", "Acknowledgements"], tabs.Items.OfType<TabItem>().Select(t => (string)t.Header!));
        tabs.SelectedItem = window.FindControl<TabItem>("LanguagesTab");
        Dispatcher.UIThread.RunJobs();

        var section = window.FindControl<StackPanel>("LanguagesSection")!;
        var texts = section.GetVisualDescendants().OfType<TextBlock>().Where(t => t.IsEffectivelyVisible).Select(t => t.Text).ToList();
        Assert.Contains("Language", texts);
        Assert.Contains("Provider", texts);
        Assert.Contains("Kotlin", texts);
        Assert.Contains("TypeScript", texts);
        Assert.Contains("UML Wizard", texts);
        Assert.False(window.FindControl<TextBlock>("NoLanguages")!.IsEffectivelyVisible);
        Assert.False(window.FindControl<StackPanel>("PluginProblems")!.IsEffectivelyVisible);
        window.Close();
    }

    [AvaloniaFact]
    public void LanguagesTab_SaysSo_WithoutPlugins_AndListsPluginsThatFailedToLoad()
    {
        var about = new AboutViewModel(
            new FakeServices(),
            new CodeGeneratorCatalog([], ["Broken.dll could not be loaded: bad image."]),
            new CodeImporterCatalog([]));
        Assert.Empty(about.LanguagePlugins);
        Assert.Contains("Languages: No language plug-ins are installed.", about.Details);

        var window = new AboutWindow { DataContext = about };
        window.Show();
        window.FindControl<TabControl>("AboutTabs")!.SelectedItem = window.FindControl<TabItem>("LanguagesTab");
        Dispatcher.UIThread.RunJobs();

        Assert.True(window.FindControl<TextBlock>("NoLanguages")!.IsEffectivelyVisible);
        var problems = window.FindControl<StackPanel>("PluginProblems")!;
        Assert.True(problems.IsEffectivelyVisible);
        Assert.Contains(problems.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "Broken.dll could not be loaded: bad image.");
        window.Close();
    }

    [Fact]
    public void TheLicenceText_AndTheNotice_AreInstalledNextToTheApplication()
    {
        var folder = Path.GetDirectoryName(typeof(AboutViewModel).Assembly.Location)!;
        var licence = Path.Combine(folder, "LICENSE.txt");
        Assert.True(File.Exists(licence), licence);
        Assert.Contains("Apache License", File.ReadAllText(licence));
        Assert.Contains("Version 2.0, January 2004", File.ReadAllText(licence));

        var notice = Path.Combine(folder, "NOTICE.txt");
        Assert.True(File.Exists(notice), notice);
        Assert.Matches(@"(?m)^Copyright \d{4}", File.ReadAllText(notice));
    }

    [Fact]
    public void Website_BecomesALink_OnlyForWebAddresses()
    {
        Assert.Equal(new Uri("https://umlwizard.example/"), AboutViewModel.WebLink("https://umlwizard.example/"));
        Assert.NotNull(AboutViewModel.WebLink("http://umlwizard.example"));
        Assert.Null(AboutViewModel.WebLink("Not specified yet"));
        Assert.Null(AboutViewModel.WebLink("mailto:someone@example.com"));
        Assert.Null(AboutViewModel.WebLink("file:///C:/Windows"));
    }

    [AvaloniaFact]
    public void AboutWindow_DisplaysNameVersionAndOperatingSystem()
    {
        var about = new AboutViewModel(new FakeServices());
        var window = new AboutWindow { DataContext = about };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var texts = window.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text).ToList();

        Assert.Contains("UML Wizard", texts);
        Assert.Contains("Version 1.0.0", texts);
        Assert.Contains(about.UmlSupport, texts);
        Assert.Contains(about.OperatingSystem, texts);
        window.Close();
    }

    [AvaloniaFact]
    public void TheSplashScreen_ShowsTheWholeDescription_AboveTheLoadingLine()
    {
        var services = new FakeServices();
        var about = new AboutViewModel(services);
        var splash = new SplashWindow { DataContext = about };
        splash.Show();
        Dispatcher.UIThread.RunJobs();

        var texts = splash.GetVisualDescendants().OfType<TextBlock>().ToList();
        var description = texts.Single(t => t.Text == about.Description);
        var loading = texts.Single(t => t.Text == Resources.Strings.Splash_Loading);
        var top = description.TranslatePoint(new Point(0, 0), splash)!.Value.Y;
        var bottom = top + description.Bounds.Height;

        // Wrapped onto several lines, each of it inside the window and above the loading line.
        Assert.True(description.Bounds.Height > description.FontSize * 1.5, "the description is expected to wrap");
        Assert.True(description.DesiredSize.Height <= description.Bounds.Height + 0.5, "the description is cut off");
        Assert.True(bottom <= loading.TranslatePoint(new Point(0, 0), splash)!.Value.Y, "the description runs into the loading line");
        splash.Close();
    }
}
