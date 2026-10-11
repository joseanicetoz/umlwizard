using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using SkiaSharp;
using UmlWizard.App.Services;
using UmlWizard.App.ViewModels;
using UmlWizard.App.ViewModels.Dialogs;
using UmlWizard.App.Views.Dialogs;
using UmlWizard.Core.Model;
using UmlWizard.Core.Serialization;

namespace UmlWizard.App.Tests;

public sealed class ExportDialogTests
{
    private static ExportPngViewModel Dialog(string folder, string name = "A.png") =>
        new(new FakeServices(), "A", folder, name, transparentBackground: false);

    [AvaloniaFact]
    public async Task Export_OffersTheProjectFolderAndDiagramName_AndWritesThePng()
    {
        using var f = new ExplorerFixture();
        f.Main.SelectedNode = f.Node("B");
        var before = ProjectSerializer.Serialize(f.Main.Project!);
        f.Services.FillDialog = d =>
        {
            var options = Assert.IsType<ExportPngViewModel>(d);
            Assert.Equal((f.Folder, "B.png", false), (options.OutputFolder, options.FileName, options.TransparentBackground));
            Assert.True(options.TryAccept());
        };

        await f.Main.ExportPngCommand.ExecuteAsync(null);

        Assert.True(new FileInfo(Path.Combine(f.Folder, "B.png")).Length > 0);
        Assert.Empty(f.Main.OpenDiagrams);
        Assert.Equal(before, ProjectSerializer.Serialize(f.Main.Project!));
    }

    [AvaloniaFact]
    public async Task Export_UsesTheChosenFolderNameAndBackground_AndOffersThemAgain()
    {
        using var f = new ExplorerFixture();
        var output = Directory.CreateDirectory(Path.Combine(f.Folder, "images")).FullName;
        f.Main.SelectedNode = f.Node("A");
        f.Services.FillDialog = d =>
        {
            var options = (ExportPngViewModel)d;
            options.OutputFolder = output;
            options.FileName = "domain"; // .png is added
            options.TransparentBackground = true;
        };

        await f.Main.ExportPngCommand.ExecuteAsync(null);

        using (var bitmap = SKBitmap.Decode(Path.Combine(output, "domain.png")))
        {
            Assert.Equal(0, bitmap.GetPixel(1, 1).Alpha);
        }

        f.Services.FillDialog = d =>
        {
            var options = (ExportPngViewModel)d;
            Assert.Equal((output, true), (options.OutputFolder, options.TransparentBackground));
        };
        f.Services.AcceptDialogs = false;
        await f.Main.ExportPngCommand.ExecuteAsync(null);
    }

    [AvaloniaFact]
    public async Task CancellingTheDialog_ExportsNothing()
    {
        using var f = new ExplorerFixture();
        f.Main.SelectedNode = f.Node("A");
        f.Services.AcceptDialogs = false;

        await f.Main.ExportPngCommand.ExecuteAsync(null);

        Assert.Single(f.Services.ShownDialogs.OfType<ExportPngViewModel>());
        Assert.False(File.Exists(Path.Combine(f.Folder, "A.png")));
    }

    [AvaloniaFact]
    public async Task AnExistingFile_IsOnlyReplacedAfterConfirming_OtherwiseTheDialogReturns()
    {
        using var f = new ExplorerFixture();
        f.Main.SelectedNode = f.Node("A");
        var png = Path.Combine(f.Folder, "A.png");
        File.WriteAllText(png, "keep me");

        // "No": the dialog comes back with the same entries; cancel it there.
        var shown = 0;
        f.Services.ConfirmAnswer = ConfirmResult.No;
        f.Services.FillDialog = d =>
        {
            shown++;
            Assert.Equal("A.png", ((ExportPngViewModel)d).FileName);
            f.Services.AcceptDialogs = shown == 1;
        };
        f.Services.AcceptDialogs = true;
        await f.Main.ExportPngCommand.ExecuteAsync(null);

        Assert.Equal(2, shown);
        Assert.Contains("'A.png' already exists", Assert.Single(f.Services.ConfirmMessages));
        Assert.Equal("keep me", File.ReadAllText(png));

        // "Yes": the file is replaced.
        f.Services.ConfirmAnswer = ConfirmResult.Yes;
        f.Services.FillDialog = null;
        f.Services.AcceptDialogs = true;
        await f.Main.ExportPngCommand.ExecuteAsync(null);

        Assert.NotEqual("keep me", File.ReadAllText(png));
        Assert.NotNull(SKBitmap.Decode(png));
    }

    [AvaloniaFact]
    public async Task ExportingAFolder_DoesNothing()
    {
        using var f = new ExplorerFixture();
        f.Main.SelectedNode = f.Node("Model");

        await f.Main.ExportPngCommand.ExecuteAsync(null);

        Assert.Empty(f.Services.ShownDialogs);
    }

    [AvaloniaFact]
    public async Task AnOpenDiagram_IsExportedWithItsUnsavedChanges()
    {
        using var f = new ExplorerFixture();
        var png = Path.Combine(f.Folder, "A.png");
        f.Main.SelectedNode = f.Node("A");
        await f.Main.ExportPngCommand.ExecuteAsync(null);
        var emptySize = new FileInfo(png).Length;

        f.Main.OpenDiagramCommand.Execute((DiagramItemViewModel)f.Node("A"));
        f.Main.ActiveDiagram!.Editor.AddClass(ClassifierKind.Class, 100, 100);
        f.Main.SelectedNode = f.Node("A");
        f.Services.ConfirmAnswer = ConfirmResult.Yes;
        await f.Main.ExportPngCommand.ExecuteAsync(null);

        Assert.True(new FileInfo(png).Length > emptySize);
        Assert.True(f.Main.ActiveDiagram.IsDirty);
    }

    [Fact]
    public void BlankOrInvalidEntries_AreRefused()
    {
        var folder = Path.GetTempPath();

        var noFolder = Dialog("  ");
        Assert.False(noFolder.TryAccept());
        Assert.Equal("Enter or choose the output folder.", noFolder.ErrorMessage);

        var noName = Dialog(folder, " ");
        Assert.False(noName.TryAccept());
        Assert.Equal("Enter a file name.", noName.ErrorMessage);

        // "/" is not allowed in a file name on any system; ":" and "?" are only refused on Windows.
        var badName = Dialog(folder, "a/b.png");
        Assert.False(badName.TryAccept());
        Assert.Contains("not allowed", badName.ErrorMessage);

        var missingFolder = Dialog(Path.Combine(folder, $"missing-{Guid.NewGuid():N}"));
        Assert.False(missingFolder.TryAccept());
        Assert.Equal("The output folder does not exist.", missingFolder.ErrorMessage);

        var fine = Dialog(folder, "diagram");
        Assert.True(fine.TryAccept());
        Assert.Equal(Path.Combine(Path.GetFullPath(folder), "diagram.png"), fine.FullPath);
    }

    [AvaloniaFact]
    public async Task Browse_FillsInTheChosenFolder()
    {
        var folder = Path.GetTempPath();
        var services = new FakeServices { FolderPath = folder };
        var dialog = new ExportPngViewModel(services, "A", string.Empty, "A.png", false);

        await dialog.BrowseFolderCommand.ExecuteAsync(null);

        Assert.Equal(folder, dialog.OutputFolder);
    }

    [AvaloniaFact]
    public void LeavingTheFileName_WarnsWhenTheFileAlreadyExists()
    {
        var folder = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), $"umlwizard-exists-{Guid.NewGuid():N}")).FullName;
        try
        {
            File.WriteAllText(Path.Combine(folder, "taken.png"), string.Empty);
            var dialog = Dialog(folder, "free.png");
            var view = new ExportPngView { DataContext = dialog };
            var window = new Window { Content = view };
            window.Show();
            Dispatcher.UIThread.RunJobs();

            var name = view.FindControl<TextBox>("FileNameBox")!;
            var warning = view.FindControl<TextBlock>("FileExistsText")!;
            var icon = view.FindControl<Material.Icons.Avalonia.MaterialIcon>("FileExistsIcon")!;
            Assert.Equal(0, warning.Opacity);
            Assert.Equal(0, icon.Opacity);
            var (viewSize, nameWidth) = (view.Bounds.Size, name.Bounds.Width);

            // Typing does not warn yet; leaving the field does.
            name.Focus();
            name.Text = "taken";
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(0, warning.Opacity);
            view.FindControl<CheckBox>("TransparentBackground")!.Focus();
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(1, warning.Opacity);
            Assert.Equal(1, icon.Opacity);

            // The warning had its space all along: the dialog and the name box keep their size.
            Assert.Equal(viewSize, view.Bounds.Size);
            Assert.Equal(nameWidth, name.Bounds.Width);
            Assert.Equal("File already exists", warning.Text);
            Assert.Equal(Avalonia.Media.Color.Parse("#C62828"), ((Avalonia.Media.ISolidColorBrush)warning.Foreground!).Color);

            // A free name clears the warning when the field is left again.
            name.Focus();
            name.Text = "other.png";
            view.FindControl<CheckBox>("TransparentBackground")!.Focus();
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(0, warning.Opacity);
            Assert.False(icon.IsHitTestVisible);
            Assert.Equal(viewSize, view.Bounds.Size);
            window.Close();
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [AvaloniaFact]
    public void TheDialog_HasFolderNameAndBackgroundFields()
    {
        var view = new ExportPngView { DataContext = Dialog(Path.GetTempPath()) };
        var window = new Window { Content = view };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var texts = view.GetLogicalDescendants().OfType<TextBlock>().Select(t => t.Text).ToList();
        Assert.Contains("Output folder", texts);
        Assert.Contains("File name", texts);
        Assert.Equal("Browse…", view.FindControl<Button>("BrowseButton")!.Content);
        Assert.NotNull(view.FindControl<TextBox>("OutputFolderBox"));
        Assert.NotNull(view.FindControl<CheckBox>("TransparentBackground"));
        window.Close();
    }
}
