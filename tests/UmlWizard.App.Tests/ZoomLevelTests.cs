using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using UmlWizard.App.ViewModels;
using UmlWizard.App.Views;
using UmlWizard.Core.Model;

namespace UmlWizard.App.Tests;

public sealed class ZoomLevelTests
{
    [AvaloniaFact]
    public void ClickingTheZoomLevel_GoesBackTo100Percent()
    {
        var services = new FakeServices();
        var diagram = new DiagramViewModel(DiagramDocument.Create("Test", DiagramType.Class), services, services, _ => Task.CompletedTask);
        var toolbar = new DiagramToolbar { DataContext = diagram };
        DockPanel.SetDock(toolbar, Dock.Top);
        var window = new Window
        {
            Width = 1000, Height = 700,
            Content = new DockPanel { Children = { toolbar, new DiagramView { DataContext = diagram } } },
        };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        var zoomLevel = window.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "ZoomLevelButton");

        // At 100% there is nothing to reset, but the level is still shown.
        Assert.Equal("100%", zoomLevel.Content);
        Assert.False(zoomLevel.IsEffectivelyEnabled);

        diagram.ZoomInCommand.Execute(null);
        diagram.ZoomInCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();
        Assert.NotEqual("100%", zoomLevel.Content);
        Assert.True(zoomLevel.IsEffectivelyEnabled);

        var center = zoomLevel.TranslatePoint(new Point(zoomLevel.Bounds.Width / 2, zoomLevel.Bounds.Height / 2), window)!.Value;
        window.MouseDown(center, MouseButton.Left);
        window.MouseUp(center, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(1.0, diagram.Document.Layout.View.Zoom);
        Assert.Equal("100%", zoomLevel.Content);
        window.Close();
    }
}
