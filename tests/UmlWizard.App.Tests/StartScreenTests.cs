using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using UmlWizard.App.ViewModels;
using UmlWizard.App.ViewModels.Dialogs;
using UmlWizard.App.Views;

namespace UmlWizard.App.Tests;

public sealed class StartScreenTests
{
    private static Button Card(Window window, string name) => window.GetLogicalDescendants().OfType<Button>().Single(b => b.Name == name);

    [AvaloniaFact]
    public void WithoutAProject_ThreeCardsOfferToCreateOpenOrImportAProject()
    {
        var services = new FakeServices();
        var main = new MainWindowViewModel(services, services);
        var window = new MainWindow { DataContext = main, Width = 1200, Height = 800 };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var cards = new[] { "NewProjectCard", "OpenProjectCard", "ImportFolderCard" }.Select(n => Card(window, n)).ToList();
        Assert.All(cards, c => Assert.True(c.IsEffectivelyVisible && c.IsEffectivelyEnabled));
        Assert.Equal(
            ["Create a New Project", "Open an Existing Project", "Import Source Folder"],
            cards.Select(c => c.GetLogicalDescendants().OfType<TextBlock>().First().Text));
        Assert.Equal(
            [main.NewProjectCommand, main.OpenProjectCommand, main.ImportFolderCommand],
            cards.Select(c => c.Command));

        // Side by side, left to right, as square tiles.
        var lefts = cards.Select(c => c.TranslatePoint(default, window)!.Value).ToList();
        Assert.True(lefts[0].X < lefts[1].X && lefts[1].X < lefts[2].X);
        Assert.All(lefts, p => Assert.Equal(lefts[0].Y, p.Y));
        Assert.All(cards, c => Assert.InRange(c.Bounds.Width / c.Bounds.Height, 0.9, 1.15));

        // The diagram placeholder is for a project without an open diagram.
        Assert.DoesNotContain(window.GetLogicalDescendants().OfType<TextBlock>(), t => t.IsEffectivelyVisible && t.Text == main.PlaceholderText);
        window.Close();
    }

    [AvaloniaFact]
    public void AboveTheCards_TheAppIconNameAndDescription_LineUpWithTheFirstCard()
    {
        var services = new FakeServices();
        var window = new MainWindow { DataContext = new MainWindowViewModel(services, services), Width = 1200, Height = 800 };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var header = window.GetLogicalDescendants().OfType<StackPanel>().Single(p => p.Name == "StartHeader");
        Assert.True(header.IsEffectivelyVisible);
        Assert.NotNull(Assert.Single(header.GetLogicalChildren().OfType<Image>()).Source);
        Assert.Equal(
            ["UML Wizard", Resources.Strings.About_Description],
            header.GetLogicalChildren().OfType<TextBlock>().Select(t => t.Text));

        var headerAt = header.TranslatePoint(default, window)!.Value;
        var cards = window.GetLogicalDescendants().OfType<WrapPanel>().Single(p => p.Name == "StartCards");
        var firstCardAt = Card(window, "NewProjectCard").TranslatePoint(default, window)!.Value;
        Assert.Equal(firstCardAt.X, headerAt.X, 0.5);
        Assert.True(headerAt.Y + header.Bounds.Height < firstCardAt.Y);
        Assert.Equal(cards.Bounds.Width, header.Bounds.Width, 0.5);
        window.Close();
    }

    [AvaloniaFact]
    public void TheNewProjectCard_StartsANewProject()
    {
        var services = new FakeServices();
        var window = new MainWindow { DataContext = new MainWindowViewModel(services, services) };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var card = Card(window, "NewProjectCard");
        var centre = card.TranslatePoint(new Point(card.Bounds.Width / 2, card.Bounds.Height / 2), window)!.Value;
        window.MouseDown(centre, MouseButton.Left);
        window.MouseUp(centre, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();

        Assert.True(Assert.IsType<ProjectPropertiesViewModel>(Assert.Single(services.ShownDialogs)).IsNew);
        window.Close();
    }

    [AvaloniaFact]
    public void WithAProject_TheStartScreenMakesWayForTheDiagrams()
    {
        using var f = new ExplorerFixture();
        var window = new MainWindow { DataContext = f.Main };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        Assert.False(window.GetLogicalDescendants().OfType<Grid>().Single(g => g.Name == "StartScreen").IsEffectivelyVisible);
        Assert.Contains(window.GetLogicalDescendants().OfType<TextBlock>(), t => t.IsEffectivelyVisible && t.Text == f.Main.PlaceholderText);

        f.Main.CloseProjectCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();
        Assert.True(window.GetLogicalDescendants().OfType<Grid>().Single(g => g.Name == "StartScreen").IsEffectivelyVisible);
        window.Close();
    }
}
