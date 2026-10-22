using Avalonia.Headless.XUnit;
using UmlWizard.App.ViewModels.Dialogs;
using UmlWizard.Core.Model;
using UmlWizard.Core.Serialization;

namespace UmlWizard.App.Tests;

public sealed class ImportProjectDiagramsTests
{
    [Fact]
    public void ViewModel_InitializesWithDiagrams_AllSelectedByDefault()
    {
        var services = new FakeServices();
        var d1 = DiagramDocument.Create("Classes", DiagramType.Class);
        var d2 = DiagramDocument.Create("Sequence", DiagramType.Sequence);

        var vm = new ImportProjectDiagramsViewModel(services, "C:\\test\\source.umlproj.json", [d1, d2]);

        Assert.Equal("C:\\test\\source.umlproj.json", vm.SourcePath);
        Assert.Equal(2, vm.Diagrams.Count);
        Assert.All(vm.Diagrams, d => Assert.True(d.IsSelected));
        Assert.Equal(2, vm.SelectedDiagrams.Count());
        Assert.True(vm.TryAccept());
        Assert.Null(vm.ErrorMessage);
    }

    [Fact]
    public void ViewModel_SelectAllAndDeselectAll_UpdatesSelection()
    {
        var services = new FakeServices();
        var d1 = DiagramDocument.Create("Classes", DiagramType.Class);
        var d2 = DiagramDocument.Create("Sequence", DiagramType.Sequence);

        var vm = new ImportProjectDiagramsViewModel(services, "C:\\test\\source.umlproj.json", [d1, d2]);

        vm.DeselectAllCommand.Execute(null);
        Assert.All(vm.Diagrams, d => Assert.False(d.IsSelected));
        Assert.Empty(vm.SelectedDiagrams);

        Assert.False(vm.TryAccept());
        Assert.NotNull(vm.ErrorMessage);

        vm.SelectAllCommand.Execute(null);
        Assert.All(vm.Diagrams, d => Assert.True(d.IsSelected));
        Assert.Equal(2, vm.SelectedDiagrams.Count());
        Assert.True(vm.TryAccept());
    }

    [Fact]
    public void ViewModel_WhenNoDiagrams_ValidationFails()
    {
        var services = new FakeServices();
        var vm = new ImportProjectDiagramsViewModel(services, "C:\\test\\source.umlproj.json", []);

        Assert.False(vm.TryAccept());
        Assert.NotNull(vm.ErrorMessage);
    }

    [Fact]
    public async Task ViewModel_BrowseSource_LoadsNewProject()
    {
        var path = Path.Combine(Path.GetTempPath(), $"umlwizard-test-{Guid.NewGuid():N}.umlproj.json");
        try
        {
            var project = new UmlProject();
            project.Diagrams.Add(DiagramDocument.Create("ImportedClass", DiagramType.Class));
            ProjectSerializer.Save(project, path);

            var services = new FakeServices { OpenFilePath = path };
            var vm = new ImportProjectDiagramsViewModel(services, "initial.umlproj.json", []);

            await vm.BrowseSourceCommand.ExecuteAsync(null);

            Assert.Equal(path, vm.SourcePath);
            Assert.Single(vm.Diagrams);
            Assert.Equal("ImportedClass", vm.Diagrams[0].Name);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [AvaloniaFact]
    public async Task MainWindow_ImportProjectDiagrams_ResolvesNameAndIdConflicts()
    {
        using var f = new ExplorerFixture();
        var sourcePath = Path.Combine(f.Folder, "source.umlproj.json");

        var sourceProject = new UmlProject();
        var classDoc = DiagramDocument.Create("A", DiagramType.Class);
        var origClass = new UmlClass { Name = "Customer" };
        ((ClassDiagramModel)classDoc.Model).Classes.Add(origClass);
        var origElemId = origClass.Id;
        var origDocId = classDoc.Id;

        sourceProject.Diagrams.Add(classDoc);
        ProjectSerializer.Save(sourceProject, sourcePath);

        // Target project already has a diagram named "A"
        Assert.Contains(f.Main.Project!.Diagrams, d => d.Name == "A");

        f.Services.OpenFilePath = sourcePath;
        f.Services.AcceptDialogs = true;

        await f.Main.ImportProjectDiagramsCommand.ExecuteAsync(null);

        // Diagram should be imported with renamed non-conflicting name "A (1)"
        var imported = f.Saved.Diagrams.Single(d => d.Name == "A (1)");
        Assert.NotEqual(origDocId, imported.Id);

        var importedClass = ((ClassDiagramModel)imported.Model).Classes.Single(c => c.Name == "Customer");
        Assert.NotEqual(origElemId, importedClass.Id);
    }

    [AvaloniaFact]
    public async Task MainWindow_ImportProjectDiagrams_WhenCancelled_ImportsNothing()
    {
        using var f = new ExplorerFixture();
        var sourcePath = Path.Combine(f.Folder, "source.umlproj.json");

        var sourceProject = new UmlProject();
        sourceProject.Diagrams.Add(DiagramDocument.Create("NewDiagram", DiagramType.Class));
        ProjectSerializer.Save(sourceProject, sourcePath);

        var countBefore = f.Main.Project!.Diagrams.Count;

        f.Services.OpenFilePath = sourcePath;
        f.Services.AcceptDialogs = false; // user cancelled dialog

        await f.Main.ImportProjectDiagramsCommand.ExecuteAsync(null);

        Assert.Equal(countBefore, f.Main.Project.Diagrams.Count);
    }
}
