using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using UmlWizard.App.Services;
using UmlWizard.App.ViewModels;
using UmlWizard.App.Views;
using UmlWizard.CodeGeneration.Plugins;
using UmlWizard.Core.Model;
using UmlWizard.Core.Serialization;

namespace UmlWizard.App.Tests;

/// <summary>A project on disk with a folder "Model" (containing "Nested" and diagram A) and top-level diagram B.</summary>
internal sealed class ExplorerFixture : IDisposable
{
    /// <param name="codeGenerators">The languages of Generate Code; the plug-ins in the test's plugins folder when omitted.</param>
    public ExplorerFixture(CodeGeneratorCatalog? codeGenerators = null)
    {
        Directory.CreateDirectory(Folder);
        var project = new UmlProject { Name = "Shop", Author = "Ada" };
        var a = DiagramDocument.Create("A", DiagramType.Class);
        var b = DiagramDocument.Create("B", DiagramType.Sequence);
        project.Diagrams.AddRange([a, b]);
        var model = project.AddFolder("Model");
        project.AddFolder("Nested", model.Id);
        project.MoveDiagram(a.Id, model.Id);
        ProjectSerializer.Save(project, Path);
        Main = new MainWindowViewModel(Services, Services, codeGenerators: codeGenerators, projectStates: ProjectStates);
        Main.OpenProjectFileAsync(Path).GetAwaiter().GetResult();
    }

    public string Folder { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"umlwizard-explorer-{Guid.NewGuid():N}");

    public string Path => System.IO.Path.Combine(Folder, "Shop.umlwiz");

    public FakeServices Services { get; } = new() { AcceptDialogs = true };

    public MainWindowViewModel Main { get; }

    public UmlProject Saved => ProjectSerializer.Load(Path);

    /// <summary>Where the user's state of each project is kept.</summary>
    public InMemoryProjectStateStore ProjectStates { get; } = new();

    /// <summary>The user's tabs of the project, as kept in <see cref="ProjectStates"/>; <c>null</c> when none were.</summary>
    public ProjectUserState? SavedTabs => ProjectStates.Load(Path);

    public ProjectNodeViewModel Node(string name) => Main.AllNodes().Single(n => n.Name == name);

    public void Dispose() => Directory.Delete(Folder, recursive: true);
}
