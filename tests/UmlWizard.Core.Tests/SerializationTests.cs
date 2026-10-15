using UmlWizard.Core.Model;
using UmlWizard.Core.Serialization;

namespace UmlWizard.Core.Tests;

public sealed class SerializationTests
{
    [Fact]
    public void ClassDiagram_RoundTrips_ModelAndLayout()
    {
        var document = DiagramDocument.Create("Domain", DiagramType.Class);
        var model = (ClassDiagramModel)document.Model;
        var order = new UmlClass
        {
            Name = "Order",
            Attributes = { new UmlAttribute { Name = "total", Type = "decimal", IsStatic = true } },
            Operations = { new UmlOperation { Name = "pay", Parameters = { new UmlParameter { Name = "amount", Type = "decimal" } } } },
        };
        var line = new UmlClass { Name = "Line" };
        model.Classes.AddRange([order, line]);
        model.Relationships.Add(new UmlRelationship { Kind = RelationshipKind.Composition, SourceId = order.Id, TargetId = line.Id });
        document.Layout.Add(new ElementLayout { ElementId = order.Id, X = 10, Y = 20, Width = 150, Height = 90 });

        var json = DiagramSerializer.Serialize(document);
        var restored = DiagramSerializer.Deserialize(json);

        var restoredModel = Assert.IsType<ClassDiagramModel>(restored.Model);
        Assert.Equal("Order", restoredModel.Classes[0].Name);
        Assert.True(restoredModel.Classes[0].Attributes[0].IsStatic);
        Assert.Equal("decimal", restoredModel.Classes[0].Operations[0].Parameters[0].Type);
        Assert.Equal(RelationshipKind.Composition, restoredModel.Relationships[0].Kind);
        Assert.Equal(150, restored.Layout.Find(order.Id)!.Width);
        Assert.Contains("\"kind\": \"class\"", json);
        Assert.Contains("\"layout\"", json);
    }

    [Fact]
    public void SequenceDiagram_RoundTrips()
    {
        var document = DiagramDocument.Create("Flow", DiagramType.Sequence);
        var model = (SequenceDiagramModel)document.Model;
        model.Lifelines.Add(new Lifeline { Id = "a", Name = "a", Kind = LifelineKind.Actor });
        model.Messages.Add(new SequenceMessage { SourceId = "a", TargetId = "a", Text = "loop()", Kind = MessageKind.Asynchronous });

        var restored = DiagramSerializer.Deserialize(DiagramSerializer.Serialize(document));

        var restoredModel = Assert.IsType<SequenceDiagramModel>(restored.Model);
        Assert.Equal(LifelineKind.Actor, restoredModel.Lifelines[0].Kind);
        Assert.Equal(MessageKind.Asynchronous, restoredModel.Messages[0].Kind);
    }

    [Fact]
    public void Deserialize_RejectsTypeMismatch()
    {
        var json = DiagramSerializer.Serialize(DiagramDocument.Create("x", DiagramType.Class))
            .Replace("\"diagramType\": \"class\"", "\"diagramType\": \"sequence\"");

        Assert.Throws<InvalidDataException>(() => DiagramSerializer.Deserialize(json));
    }

    [Fact]
    public void Project_SaveAndLoad_EmbedsDiagrams()
    {
        var path = Path.Combine(Path.GetTempPath(), $"umlwizard-{Guid.NewGuid():N}.umlwiz");
        try
        {
            var project = new UmlProject { Name = "Shop", Author = "Ada", Description = "Test" };
            var sequence = DiagramDocument.Create("Flow", DiagramType.Sequence);
            ((SequenceDiagramModel)sequence.Model).Lifelines.Add(new Lifeline { Name = "a" });
            project.Diagrams.AddRange([DiagramDocument.Create("Domain", DiagramType.Class), sequence]);
            ProjectSerializer.Save(project, path);

            var json = File.ReadAllText(path);
            var loaded = ProjectSerializer.Load(path);

            Assert.Contains("\"schemaVersion\": 3", json);
            Assert.Contains("\"lifelines\"", json);
            Assert.Equal("Ada", loaded.Author);
            Assert.Equal(["Domain", "Flow"], loaded.Diagrams.Select(d => d.Name));
            Assert.IsType<ClassDiagramModel>(loaded.Diagrams[0].Model);
            Assert.Equal("a", Assert.IsType<SequenceDiagramModel>(loaded.Diagrams[1].Model).Lifelines[0].Name);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Project_RejectsInvalidEmbeddedDiagram()
    {
        var project = new UmlProject();
        project.Diagrams.Add(DiagramDocument.Create("x", DiagramType.Class));
        var json = ProjectSerializer.Serialize(project)
            .Replace("\"diagramType\": \"class\"", "\"diagramType\": \"sequence\"");

        Assert.Throws<InvalidDataException>(() => ProjectSerializer.Deserialize(json));
    }

    [Fact]
    public void Project_RejectsNewerSchema()
    {
        var json = ProjectSerializer.Serialize(new UmlProject()).Replace("\"schemaVersion\": 3", "\"schemaVersion\": 99");

        Assert.Throws<InvalidDataException>(() => ProjectSerializer.Deserialize(json));
    }

    [Fact]
    public void Project_Schema1_EmbedsReferencedDiagramFiles()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"umlwizard-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(Path.Combine(directory, "Diagrams"));
            var diagram = DiagramDocument.Create("Domain Model", DiagramType.Class);
            ((ClassDiagramModel)diagram.Model).Classes.Add(new UmlClass { Name = "Order" });
            File.WriteAllText(Path.Combine(directory, "Diagrams", "Domain Model.diagram.json"),
                "{ \"schemaVersion\": 1," + DiagramSerializer.Serialize(diagram)[1..]);
            var projectPath = Path.Combine(directory, "Old.umlproj");
            File.WriteAllText(projectPath, $$"""
                {
                  "schemaVersion": 1,
                  "name": "Old",
                  "diagrams": [ { "id": "{{diagram.Id}}", "name": "Domain Model", "type": "class", "path": "Diagrams/Domain Model.diagram.json" } ]
                }
                """);

            var project = ProjectSerializer.Load(projectPath);

            Assert.Equal(UmlProject.CurrentSchemaVersion, project.SchemaVersion);
            var loaded = Assert.Single(project.Diagrams);
            Assert.Equal(diagram.Id, loaded.Id);
            Assert.Equal("Order", Assert.IsType<ClassDiagramModel>(loaded.Model).Classes[0].Name);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Theory]
    [InlineData("../outside.json")]
    [InlineData("Diagrams/../../outside.json")]
    [InlineData(@"\\server\share\diagram.json")]
    [InlineData("/etc/diagram.json")]
    public void Project_Schema1_RejectsDiagramFilesOutsideTheProjectFolder(string path)
    {
        if (path.StartsWith(@"\\", StringComparison.Ordinal) && !OperatingSystem.IsWindows())
        {
            return; // Only Windows reads "\\server\share" as another machine; elsewhere it is an odd file name.
        }

        var directory = Path.Combine(Path.GetTempPath(), $"umlwizard-{Guid.NewGuid():N}");
        var projectDirectory = Path.Combine(directory, "Project");
        try
        {
            Directory.CreateDirectory(projectDirectory);
            File.WriteAllText(Path.Combine(directory, "outside.json"), "{}");
            var json = $$"""
                {
                  "schemaVersion": 1,
                  "name": "Old",
                  "diagrams": [ { "path": {{System.Text.Json.JsonSerializer.Serialize(path)}} } ]
                }
                """;

            var ex = Assert.Throws<InvalidDataException>(() => ProjectSerializer.Deserialize(json, projectDirectory));
            Assert.Contains("not inside the project's folder", ex.Message);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Theory]
    [InlineData("Shop.umlwiz", true)]
    [InlineData(@"C:WorkSHOP.UMLWIZ", true)]
    [InlineData("Old.umlproj", true)]
    [InlineData("Diagram.diagram.json", false)]
    [InlineData("Shop.umlwiz.bak", false)]
    public void IsProjectFile_AcceptsCurrentAndLegacyExtensions(string path, bool expected) =>
        Assert.Equal(expected, UmlProject.IsProjectFile(path));

    [Fact]
    public void Clone_IsIndependentCopy()
    {
        var document = DiagramDocument.Create("A", DiagramType.Class);

        var copy = DiagramSerializer.Clone(document);
        copy.Name = "B";
        ((ClassDiagramModel)copy.Model).Classes.Add(new UmlClass { Name = "C" });

        Assert.Equal("A", document.Name);
        Assert.Empty(((ClassDiagramModel)document.Model).Classes);
        Assert.Equal(document.Id, copy.Id);
    }

    [Fact]
    public void SampleProject_LoadsWithAllDiagrams()
    {
        var projectPath = Path.Combine(AppContext.BaseDirectory, "samples", "OnlineShop", "OnlineShop.umlwiz");
        var project = ProjectSerializer.Load(projectPath);

        // The sample is edited in the app, so check ids, types and structure rather than display names.
        // It has one diagram of every type.
        Assert.Equal(
            ["d0main000001", "checkout0001", "ordstate0001", "checkoutact1", "usecases0001", "components01", "deploy000001"],
            project.Diagrams.Select(d => d.Id));
        Assert.Equal(Enum.GetValues<DiagramType>(), project.Diagrams.Select(d => d.DiagramType));
        Assert.All(project.Diagrams, d => Assert.NotEmpty(d.Model.AllElements()));
        Assert.Equal(["Ordering", "Requirements", "Architecture"], project.Folders.Select(f => f.Name));

        // Which folder holds which diagram changes as the sample is reorganised; each is in one folder or at the top level.
        Assert.Equal(project.Diagrams.Count, project.Folders.Sum(f => f.DiagramIds.Count) + project.TopLevelDiagrams().Count());

        // The new diagram types' samples use their notations: a self-transition, a note attachment, fork/join,
        // «include»/«extend» and realizations.
        var lifecycle = (GraphDiagramModel)project.FindDiagram("ordstate0001")!.Model;
        Assert.Contains(lifecycle.Edges, e => e.SourceId == e.TargetId);
        Assert.Single(lifecycle.NoteAttachments);
        Assert.Equal(2, ((GraphDiagramModel)project.FindDiagram("checkoutact1")!.Model).Nodes.Count(n => n.Kind == GraphNodeKind.ForkJoin));
        var useCases = (GraphDiagramModel)project.FindDiagram("usecases0001")!.Model;
        Assert.Contains(useCases.Edges, e => e.Kind == GraphEdgeKind.Include);
        Assert.Contains(useCases.Edges, e => e.Kind == GraphEdgeKind.Extend);
        Assert.Contains(((GraphDiagramModel)project.FindDiagram("components01")!.Model).Edges, e => e.Kind == GraphEdgeKind.Realization);
    }
}
