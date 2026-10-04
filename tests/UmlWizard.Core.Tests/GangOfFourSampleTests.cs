using UmlWizard.Core.Model;
using UmlWizard.Core.Rendering;
using UmlWizard.Core.Serialization;

namespace UmlWizard.Core.Tests;

public sealed class GangOfFourSampleTests
{
    private static UmlProject Load() =>
        ProjectSerializer.Load(Path.Combine(AppContext.BaseDirectory, "samples", "GangOfFour", "GangOfFour.umlwiz"));

    [Fact]
    public void HasThe23Patterns_GroupedByPurpose()
    {
        var project = Load();

        Assert.Equal(["Creational Patterns", "Structural Patterns", "Behavioral Patterns"], project.Folders.Select(f => f.Name));
        string[] Names(int folder) => project.Folders[folder].DiagramIds.Select(id => project.FindDiagram(id)!.Name).ToArray();
        Assert.Equal(["Abstract Factory", "Builder", "Factory Method", "Prototype", "Singleton"], Names(0));
        Assert.Equal(["Adapter", "Bridge", "Composite", "Decorator", "Facade", "Flyweight", "Proxy"], Names(1));
        Assert.Equal(
            ["Chain of Responsibility", "Command", "Interpreter", "Iterator", "Mediator", "Memento", "Observer", "State",
             "Strategy", "Template Method", "Visitor"],
            Names(2));
        Assert.Empty(project.TopLevelDiagrams());
        Assert.All(project.Diagrams, d =>
        {
            Assert.Equal(DiagramType.Class, d.DiagramType);
            Assert.False(string.IsNullOrWhiteSpace(d.Description));
        });
    }

    [Fact]
    public void ClassesDoNotOverlap()
    {
        using var renderer = DiagramRenderer.Create(DiagramType.Class);
        foreach (var document in Load().Diagrams)
        {
            renderer.UpdateLayout(document);
            var boxes = ((ClassDiagramModel)document.Model).Classes.Select(c => document.Layout.Find(c.Id)!).ToList();
            for (var i = 0; i < boxes.Count; i++)
            {
                for (var j = i + 1; j < boxes.Count; j++)
                {
                    var (a, b) = (boxes[i], boxes[j]);
                    Assert.False(
                        a.X < b.X + b.Width && b.X < a.X + a.Width && a.Y < b.Y + b.Height && b.Y < a.Y + a.Height,
                        $"Classes overlap in {document.Name}");
                }
            }
        }
    }
}
