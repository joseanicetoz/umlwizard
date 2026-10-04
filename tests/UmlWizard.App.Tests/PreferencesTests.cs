using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using UmlWizard.App.Services;
using UmlWizard.App.ViewModels;
using UmlWizard.App.ViewModels.Dialogs;
using UmlWizard.App.Views;
using UmlWizard.Core.Model;

namespace UmlWizard.App.Tests;

public sealed class PreferencesTests
{
    [AvaloniaFact]
    public void FilePreferences_AndCtrlComma_OpenThePreferences()
    {
        var services = new FakeServices();
        var main = new MainWindowViewModel(services, services);
        var window = new MainWindow { DataContext = main };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var item = window.GetLogicalDescendants().OfType<MenuItem>().Single(m => (string?)m.Header == "Pr_eferences…");
        Assert.Same(Shortcuts.Preferences, item.InputGesture);
        Assert.Equal(new KeyGesture(Key.OemComma, Shortcuts.CommandModifier), Shortcuts.Preferences);
        Assert.Contains(window.KeyBindings, b => b.Gesture == Shortcuts.Preferences && b.Command == main.PreferencesCommand);

        main.PreferencesCommand.Execute(null);
        Assert.Single(services.ShownPreferences);

        // On macOS the application menu has it too, where Mac users look for it.
        var appMenu = App.CreateMacAppMenu(main).Items.OfType<NativeMenuItem>().ToList();
        Assert.Contains(appMenu, i => i.Header == "Preferences…" && i.Command == main.PreferencesCommand);
        window.Close();
    }

    [AvaloniaFact]
    public void EveryChange_IsAppliedAndSaved_AtOnce()
    {
        var services = new FakeServices();
        var store = new InMemorySettingsStore();
        using var f = new ExplorerFixture();
        var main = new MainWindowViewModel(services, services, settings: store);
        main.OpenProjectFileAsync(f.Path).GetAwaiter().GetResult();
        main.OpenDiagramCommand.Execute(main.AllNodes().OfType<DiagramItemViewModel>().First(d => d.Document.DiagramType == DiagramType.Class));
        var diagram = main.ActiveDiagram!;
        Assert.Null(diagram.Defaults);

        main.PreferencesCommand.Execute(null);
        var preferences = Assert.Single(services.ShownPreferences);

        preferences.SelectedTheme = preferences.Themes.Single(t => t.Theme == AppTheme.Dark);
        Assert.Equal(AppTheme.Dark, main.Theme);
        Assert.Equal(AppTheme.Dark, store.Load().Theme);

        preferences.ProjectsFolder = f.Folder;
        Assert.Equal(f.Folder, store.Load().ProjectsFolder);
        preferences.ProjectsFolder = "  ";
        Assert.Null(store.Load().ProjectsFolder);

        preferences.FontSize = 16;
        Assert.Equal(16, main.UiFontSize);
        preferences.FontSize = 99;
        Assert.Equal(PreferencesViewModel.MaxFontSize, store.Load().UiFontSize);
        preferences.FontSize = null;
        Assert.Null(main.UiFontSize);

        // A fill of the class diagram reaches the open class diagram at once, and new tabs.
        var page = preferences.DiagramPages.Single(p => p.Type == DiagramType.Class);
        Assert.Equal("Class diagram", page.Name);
        page.Fills.Single(c => c.Key == "Interface").Text = "#e0ffe0";
        page.BorderWidth = 2.5m;
        Assert.Equal("#E0FFE0", diagram.Defaults!.Fills["Interface"]);
        Assert.Equal(2.5, diagram.Defaults.BorderWidth);
        Assert.Equal("#E0FFE0", store.Load().StyleFor(DiagramType.Class)!.Fills["Interface"]);

        // What is not a colour yet (while typing) changes nothing; an empty box goes back to the default.
        page.Fills.Single(c => c.Key == "Class").Text = "#12";
        Assert.False(diagram.Defaults.Fills.ContainsKey("Class"));
        page.ResetCommand.Execute(null);
        Assert.Null(diagram.Defaults);
        Assert.Null(store.Load().StyleFor(DiagramType.Class));

        // The Project Explorer's visibility is kept, although it is not on the window.
        main.IsProjectExplorerVisible = false;
        preferences.SelectedTheme = preferences.Themes.Single(t => t.Theme == AppTheme.Light);
        Assert.False(store.Load().ShowProjectExplorer);
    }

    [AvaloniaFact]
    public void NewProjects_AreCreatedInTheProjectsFolder()
    {
        var folder = Directory.CreateTempSubdirectory().FullName;
        try
        {
            var services = new FakeServices();
            var main = new MainWindowViewModel(services, services, settings: new InMemorySettingsStore(new AppSettings { ProjectsFolder = folder }));
            main.NewProjectCommand.Execute(null);
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(folder, Assert.IsType<ProjectPropertiesViewModel>(Assert.Single(services.ShownDialogs)).Location);

            // A folder that no longer exists falls back to the Documents folder.
            Assert.Equal(
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                new AppSettings { ProjectsFolder = Path.Combine(folder, "gone") }.ProjectsFolderOrDefault);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [AvaloniaFact]
    public void TheWindow_HasGeneralAppearanceAndDiagramTabs_LaidOutAsLabelsAndControls()
    {
        var applied = new List<AppSettings>();
        var preferences = new PreferencesViewModel(new AppSettings(), new FakeServices(), applied.Add);
        var window = new PreferencesWindow { DataContext = preferences };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var tabs = window.FindControl<TabControl>("PreferencesTabs")!;
        Assert.Equal(["General", "Appearance", "Diagram", "AI"], tabs.Items.OfType<TabItem>().Select(t => (string)t.Header!));
        Assert.Empty(applied);

        // General: the projects folder with a browse button, and the display language (English only, typeahead).
        Assert.NotNull(window.FindControl<TextBox>("ProjectsFolderBox")!.InnerRightContent);
        var language = window.FindControl<ComboBox>("DisplayLanguageBox")!;
        Assert.True(language.IsTextSearchEnabled);
        Assert.Equal(["English"], language.Items.Select(i => i!.ToString()));

        // Appearance: theme, font (typeahead, the default first) and size.
        tabs.SelectedItem = window.FindControl<TabItem>("AppearanceTab");
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(3, window.FindControl<ComboBox>("ThemeBox")!.ItemCount);
        var font = window.FindControl<ComboBox>("FontBox")!;
        Assert.True(font.IsTextSearchEnabled);
        Assert.Equal("Default", font.Items[0]!.ToString());

        // Diagram: a page per diagram type; picking a colour from the palette sets it.
        tabs.SelectedItem = window.FindControl<TabItem>("DiagramTab");
        Dispatcher.UIThread.RunJobs();
        var types = window.FindControl<ListBox>("DiagramTypeList")!;
        Assert.Equal(Enum.GetValues<DiagramType>().Length, types.ItemCount);
        types.SelectedIndex = 1;
        Dispatcher.UIThread.RunJobs();
        var page = preferences.SelectedDiagramPage;
        Assert.Equal(DiagramType.Sequence, page.Type);
        Assert.Equal(["Lifeline", "Note"], page.Fills.Select(c => c.Label));

        var lifeline = page.Fills[0];
        lifeline.Text = "#2F7FFF";
        Assert.Equal("#2F7FFF", lifeline.Hex);
        Assert.Equal(lifeline.Hex, applied[^1].StyleFor(DiagramType.Sequence)!.Fills["Lifeline"]);
        Assert.Contains(window.FindControl<StackPanel>("DiagramPage")!.GetLogicalDescendants().OfType<TextBox>(), t => t.Text == lifeline.Hex);
        window.Close();
    }

    [AvaloniaFact]
    public void TheWindow_CanBeResized_ButNotMinimisedOrMaximised()
    {
        var window = new PreferencesWindow { DataContext = new PreferencesViewModel(new AppSettings(), new FakeServices(), _ => { }) };

        Assert.False(window.CanMinimize);
        Assert.False(window.CanMaximize);
        Assert.True(window.CanResize);
    }

    [AvaloniaFact]
    public void ThePreview_ShowsTheChosenFontAndSize()
    {
        var preferences = new PreferencesViewModel(new AppSettings(), new FakeServices(), _ => { });
        var window = new PreferencesWindow { DataContext = preferences };
        window.Show();
        window.FindControl<TabControl>("PreferencesTabs")!.SelectedItem = window.FindControl<TabItem>("AppearanceTab");
        Dispatcher.UIThread.RunJobs();

        var preview = window.FindControl<Border>("FontPreview")!;
        var text = window.FindControl<TextBlock>("PreviewText")!;
        Assert.True(preview.IsEffectivelyVisible);
        Assert.Equal(PreferencesViewModel.DefaultFontSize, text.FontSize);
        Assert.Equal(App.DefaultUiFontFamily, text.FontFamily);

        preferences.FontSize = 18;
        preferences.SelectedFont = preferences.Fonts.Count > 1 ? preferences.Fonts[1] : preferences.Fonts[0];
        Dispatcher.UIThread.RunJobs();

        // Every sample control follows: text, text box, check box and button.
        Assert.All(preview.GetLogicalDescendants().OfType<TemplatedControl>().Cast<Control>().Concat(preview.GetLogicalDescendants().OfType<TextBlock>()),
            c => Assert.Equal(18, c is TemplatedControl t ? t.FontSize : ((TextBlock)c).FontSize));
        if (preferences.Fonts.Count > 1)
        {
            Assert.Equal(preferences.Fonts[1].Family, text.FontFamily.Name);
        }

        window.Close();
    }

    [AvaloniaFact]
    public void TheColoursOfADiagramType_LineUp_HoweverLongTheirLabels()
    {
        var preferences = new PreferencesViewModel(new AppSettings(), new FakeServices(), _ => { });
        var window = new PreferencesWindow { DataContext = preferences };
        window.Show();
        window.FindControl<TabControl>("PreferencesTabs")!.SelectedItem = window.FindControl<TabItem>("DiagramTab");
        preferences.SelectedDiagramPage = preferences.DiagramPages.Single(p => p.Type == DiagramType.Deployment);
        Dispatcher.UIThread.RunJobs();

        // The colour boxes of the fills and of the border all start at the same place, after the longest label
        // ("Execution environment"), which is not cut off.
        var page = window.FindControl<StackPanel>("DiagramPage")!;
        var boxes = page.GetLogicalDescendants().OfType<TextBox>().ToList();
        Assert.Equal(preferences.SelectedDiagramPage.Fills.Count + 1, boxes.Count);
        var lefts = boxes.Select(b => Math.Round(b.TranslatePoint(default, page)!.Value.X, 1)).Distinct().ToList();
        Assert.Single(lefts);

        var longest = page.GetLogicalDescendants().OfType<TextBlock>().Single(t => t.Text == "Execution environment");
        Assert.True(longest.TranslatePoint(new Point(longest.Bounds.Width, 0), page)!.Value.X <= lefts[0]);
        Assert.True(longest.Bounds.Width >= longest.DesiredSize.Width - longest.Margin.Left - longest.Margin.Right - 0.5);
        window.Close();
    }

    [AvaloniaFact]
    public void TheUserInterfaceFont_ChangesEveryWindow_AndGoesBackToTheDefault()
    {
        var app = (App)Application.Current!;
        var window = new Window { Content = new TextBlock { Text = "Text" } };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        var text = (TextBlock)window.Content!;
        var defaultSize = text.FontSize;
        var defaultFamily = text.FontFamily;
        try
        {
            app.ApplyUiFont("Courier New", 18);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(18, text.FontSize);
            Assert.Equal("Courier New", text.FontFamily.Name);

            app.ApplyUiFont(null, null);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(defaultSize, text.FontSize);
            Assert.Equal(defaultFamily, text.FontFamily);
        }
        finally
        {
            app.ApplyUiFont(null, null);
            window.Close();
        }
    }
}
