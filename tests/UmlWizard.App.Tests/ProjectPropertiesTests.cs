using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using UmlWizard.App.ViewModels.Dialogs;
using UmlWizard.App.Views.Dialogs;

namespace UmlWizard.App.Tests;

public sealed class ProjectPropertiesTests
{
    [AvaloniaFact]
    public async Task TheDialogShowsTheProjectFolder_AndAClickCopiesIt_WithACueThatFades()
    {
        using var f = new ExplorerFixture();
        f.Services.AcceptDialogs = false;
        await f.Main.EditProjectPropertiesCommand.ExecuteAsync(null);
        var dialog = Assert.IsType<ProjectPropertiesViewModel>(Assert.Single(f.Services.ShownDialogs));

        Assert.True(dialog.ShowsFolder);
        Assert.Equal(Path.GetFullPath(f.Folder), dialog.Folder);

        var window = new Window { Content = new ProjectPropertiesView { DataContext = dialog } };
        window.Show();
        var button = window.GetLogicalDescendants().OfType<Button>().Single(b => b.Name == "FolderButton");
        var cue = window.GetLogicalDescendants().OfType<StackPanel>().Single(p => p.Name == "CopiedCue");
        Assert.True(button.IsEffectivelyVisible);
        Assert.Contains(button.GetLogicalDescendants().OfType<TextBlock>(), t => t.Text == dialog.Folder);
        Assert.False(cue.IsEffectivelyVisible);

        // Read-only: there is nothing to type the folder into.
        Assert.DoesNotContain(window.GetLogicalDescendants().OfType<TextBox>(), t => t.Text == dialog.Folder);

        // The cue fades when the test says so, not after a real wait that a busy machine could overrun.
        var fade = new TaskCompletionSource();
        TimeSpan? shownFor = null;
        dialog.Delay = duration => { shownFor = duration; return fade.Task; };
        try
        {
            var copy = dialog.CopyFolderCommand.ExecuteAsync(null);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(dialog.Folder, f.Services.ClipboardText);
            Assert.True(cue.IsEffectivelyVisible);
            Assert.Equal(ProjectPropertiesViewModel.CopiedDuration, shownFor);

            fade.SetResult();
            await copy;
            Dispatcher.UIThread.RunJobs();
            Assert.False(cue.IsEffectivelyVisible);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void ANewProject_AsksForALocation_InsteadOfShowingAFolder()
    {
        var services = new FakeServices();
        var dialog = new ProjectPropertiesViewModel(services, isNew: true, services);
        var window = new Window { Content = new ProjectPropertiesView { DataContext = dialog } };
        window.Show();

        Assert.False(dialog.ShowsFolder);
        Assert.False(window.GetLogicalDescendants().OfType<Button>().Single(b => b.Name == "FolderButton").IsEffectivelyVisible);
        Assert.Contains(window.GetLogicalDescendants().OfType<TextBox>(), t => t.IsEffectivelyVisible && t.Text == dialog.Location);
        window.Close();
    }

    [AvaloniaFact]
    public async Task TheProjectVersion_IsEditedInProjectProperties_AndSavedWithTheProject()
    {
        using var f = new ExplorerFixture();
        f.Services.AcceptDialogs = true;
        f.Services.FillDialog = d =>
        {
            var dialog = Assert.IsType<ProjectPropertiesViewModel>(d);
            Assert.Null(dialog.Version);
            dialog.Version = " 1.2.0 ";
        };

        await f.Main.EditProjectPropertiesCommand.ExecuteAsync(null);

        Assert.Equal("1.2.0", f.Saved.Version);
        f.Services.FillDialog = d =>
        {
            var dialog = (ProjectPropertiesViewModel)d;
            Assert.Equal("1.2.0", dialog.Version);
            dialog.Version = "";
        };
        await f.Main.EditProjectPropertiesCommand.ExecuteAsync(null);
        Assert.Null(f.Saved.Version);
        Assert.DoesNotContain("\"version\"", File.ReadAllText(f.Path));
    }

    [AvaloniaFact]
    public void TheDialog_HasAVersionBox_BelowTheName()
    {
        var services = new FakeServices();
        var dialog = new ProjectPropertiesViewModel(services, isNew: false, services) { Name = "Shop", Version = "2.0" };
        var window = new Window { Content = new ProjectPropertiesView { DataContext = dialog } };
        window.Show();

        var box = window.GetLogicalDescendants().OfType<TextBox>().Single(t => t.Name == "VersionBox");
        Assert.Equal("2.0", box.Text);
        Assert.Equal("e.g. 1.0.0", box.Watermark);
        Assert.Equal(2, Grid.GetRow(box));
        window.Close();
    }
}
