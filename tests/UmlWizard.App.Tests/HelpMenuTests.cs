using System.Net;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using UmlWizard.App.Resources;
using UmlWizard.App.ViewModels;
using UmlWizard.App.Views;

namespace UmlWizard.App.Tests;

public sealed class HelpMenuTests
{
    [AvaloniaFact]
    public void HelpMenu_ListsTopics_ThenHowTos_ThenAbout()
    {
        var services = new FakeServices();
        var window = new MainWindow { DataContext = new MainWindowViewModel(services, services) };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var help = window.GetLogicalDescendants().OfType<MenuItem>().Single(m => (string?)m.Header == "_Help");

        Assert.Equal(
            [
                "_User Manual", "_Learn UML", "-",
                "How to _Import and Export OpenAPI", "How to Import and Export _PlantUML", "How to _Generate Code", "-",
                "_About UML Wizard…",
            ],
            help.Items.Select(i => i is Separator ? "-" : (string?)((MenuItem)i!).Header));
        window.Close();
    }

    [AvaloniaFact]
    public async Task HelpTopics_OpenTheirInstalledHelpPages()
    {
        var services = new FakeServices();
        var main = new MainWindowViewModel(services, services);

        await main.UserManualCommand.ExecuteAsync(null);
        await main.LearnUmlCommand.ExecuteAsync(null);
        await main.OpenApiHowToCommand.ExecuteAsync(null);
        await main.PlantUmlHowToCommand.ExecuteAsync(null);
        await main.GeneratingCodeHowToCommand.ExecuteAsync(null);

        Assert.Equal(
            [
                Path.Combine(MainWindowViewModel.HelpFolder, "user-manual.html"),
                Path.Combine(MainWindowViewModel.HelpFolder, "learn-uml.html"),
                Path.Combine(MainWindowViewModel.HelpFolder, "openapi-how-to.html"),
                Path.Combine(MainWindowViewModel.HelpFolder, "plantuml-how-to.html"),
                Path.Combine(MainWindowViewModel.HelpFolder, "generating-code-how-to.html"),
            ],
            services.OpenedFiles);
        Assert.All(services.OpenedFiles, path => Assert.True(File.Exists(path), path));
        Assert.Empty(services.Errors);
    }

    [AvaloniaFact]
    public void F1_OpensTheUserManual_AndTheMenuShowsIt()
    {
        var services = new FakeServices();
        var main = new MainWindowViewModel(services, services);
        var window = new MainWindow { DataContext = main };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var binding = Assert.Single(window.KeyBindings, b => b.Gesture == Shortcuts.UserManual);
        Assert.Same(main.UserManualCommand, binding.Command);
        Assert.Equal(new KeyGesture(Key.F1), Shortcuts.UserManual);
        var item = window.GetLogicalDescendants().OfType<MenuItem>().Single(m => (string?)m.Header == "_User Manual");
        Assert.Same(Shortcuts.UserManual, item.InputGesture);

        window.KeyPress(Key.F1, RawInputModifiers.None, PhysicalKey.F1, null);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal([Path.Combine(MainWindowViewModel.HelpFolder, "user-manual.html")], services.OpenedFiles);
        window.Close();
    }

    private static string RepositoryFile(string relative, [CallerFilePath] string testFile = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(testFile)!, "..", "..", relative));

    [Fact]
    public void TheUserManual_NamesEveryMenuItem()
    {
        // Every text of the main menus, without its access key and ellipsis, appears in the manual, so that new
        // commands are documented and renamed ones are not left behind.
        var manual = WebUtility.HtmlDecode(File.ReadAllText(RepositoryFile("docs/help/user-manual.html")));
        var missing = typeof(Strings).GetProperties(BindingFlags.Public | BindingFlags.Static)
            .Where(p => p.PropertyType == typeof(string) && p.Name.StartsWith("Menu_") && !p.Name.EndsWith("_Tip"))
            .Select(p => ((string)p.GetValue(null)!).Replace("_", string.Empty).Replace("…", string.Empty).Trim())
            .Where(text => !text.Contains('{')) // patterns such as the numbered entries of Recent Projects
            .Where(text => !manual.Contains(text, StringComparison.Ordinal))
            .Distinct()
            .ToList();

        Assert.True(missing.Count == 0, "Not in the User Manual: " + string.Join(", ", missing));
    }

    [Fact]
    public void TheUserManualsLinks_AndTheReadmesLinksIntoIt_LeadToItsSections()
    {
        var manual = File.ReadAllText(RepositoryFile("docs/help/user-manual.html"));
        var ids = Regex.Matches(manual, "id=\"([^\"]+)\"").Select(m => m.Groups[1].Value).ToHashSet();
        var internalLinks = Regex.Matches(manual, "href=\"#([^\"]+)\"").Select(m => m.Groups[1].Value).ToList();
        var readmeLinks = Regex.Matches(File.ReadAllText(RepositoryFile("README.md")), @"user-manual\.html#([\w-]+)").Select(m => m.Groups[1].Value).ToList();

        Assert.NotEmpty(internalLinks);
        Assert.NotEmpty(readmeLinks);
        Assert.All(internalLinks.Concat(readmeLinks), link => Assert.Contains(link, ids));
    }
}
