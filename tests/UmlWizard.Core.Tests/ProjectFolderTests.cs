using UmlWizard.Core.Model;
using UmlWizard.Core.Serialization;

namespace UmlWizard.Core.Tests;

public sealed class ProjectFolderTests
{
    private static (UmlProject Project, DiagramDocument A, DiagramDocument B, DiagramDocument C) Project()
    {
        var project = new UmlProject();
        var a = DiagramDocument.Create("A", DiagramType.Class);
        var b = DiagramDocument.Create("B", DiagramType.Sequence);
        var c = DiagramDocument.Create("C", DiagramType.Class);
        project.Diagrams.AddRange([a, b, c]);
        return (project, a, b, c);
    }

    [Fact]
    public void Diagrams_AreTopLevel_UntilMovedIntoAFolder()
    {
        var (project, a, b, c) = Project();
        var folder = project.AddFolder("Domain");

        project.MoveDiagram(b.Id, folder.Id);

        Assert.Equal([a, c], project.TopLevelDiagrams());
        Assert.Same(folder, project.FolderOfDiagram(b.Id));
        Assert.Equal([b.Id], folder.DiagramIds);

        project.MoveDiagram(b.Id, null);
        Assert.Empty(folder.DiagramIds);
        Assert.Equal(3, project.TopLevelDiagrams().Count());
    }

    [Fact]
    public void MoveDiagram_WithIndex_ReordersTopLevel()
    {
        var (project, a, b, c) = Project();

        project.MoveDiagram(c.Id, null, 0);
        Assert.Equal([c, a, b], project.TopLevelDiagrams());

        project.MoveDiagram(c.Id, null, 1);
        Assert.Equal([a, c, b], project.TopLevelDiagrams());

        project.MoveDiagram(a.Id, null, 99);
        Assert.Equal([c, b, a], project.TopLevelDiagrams());
    }

    [Fact]
    public void MoveDiagram_WithIndex_PlacesItInFolder_AndReordersThere()
    {
        var (project, a, b, c) = Project();
        var folder = project.AddFolder("Domain");
        project.MoveDiagram(a.Id, folder.Id);
        project.MoveDiagram(b.Id, folder.Id);

        project.MoveDiagram(c.Id, folder.Id, 1);
        Assert.Equal([a.Id, c.Id, b.Id], folder.DiagramIds);

        project.MoveDiagram(b.Id, folder.Id, 0);
        Assert.Equal([b.Id, a.Id, c.Id], folder.DiagramIds);

        // Out of a folder to a position among the top-level diagrams, which are in project order.
        var d = DiagramDocument.Create("D", DiagramType.Class);
        project.Diagrams.Add(d);
        project.MoveDiagram(a.Id, null, 0);
        Assert.Equal([a, d], project.TopLevelDiagrams());
    }

    [Fact]
    public void Folders_Nest_AndCannotMoveIntoThemselves()
    {
        var (project, a, _, _) = Project();
        var outer = project.AddFolder("Outer");
        var inner = project.AddFolder("Inner", outer.Id);
        var other = project.AddFolder("Other");
        project.MoveDiagram(a.Id, inner.Id);

        Assert.Equal(["Outer", "Inner", "Other"], project.AllFolders().Select(f => f.Name));
        Assert.Same(outer, project.ParentOfFolder(inner.Id));
        Assert.False(project.CanMoveFolder(outer.Id, outer.Id));
        Assert.False(project.CanMoveFolder(outer.Id, inner.Id));
        Assert.Throws<InvalidOperationException>(() => project.MoveFolder(outer.Id, inner.Id));

        project.MoveFolder(inner.Id, other.Id);

        Assert.Empty(outer.Folders);
        Assert.Same(other, project.ParentOfFolder(inner.Id));
        Assert.Same(inner, project.FolderOfDiagram(a.Id));

        project.MoveFolder(inner.Id, null);
        Assert.Equal(["Outer", "Other", "Inner"], project.Folders.Select(f => f.Name));
    }

    [Fact]
    public void RemoveFolder_MovesContentsToParent()
    {
        var (project, a, b, _) = Project();
        var outer = project.AddFolder("Outer");
        var middle = project.AddFolder("Middle", outer.Id);
        var inner = project.AddFolder("Inner", middle.Id);
        project.MoveDiagram(a.Id, middle.Id);
        project.MoveDiagram(b.Id, outer.Id);

        project.RemoveFolder(middle.Id);

        Assert.Equal([inner], outer.Folders);
        Assert.Equal([b.Id, a.Id], outer.DiagramIds);

        project.RemoveFolder(outer.Id);

        Assert.Equal([inner], project.Folders);
        Assert.Equal(3, project.TopLevelDiagrams().Count());
    }

    [Fact]
    public void RemoveDiagram_RemovesItFromItsFolder()
    {
        var (project, a, _, _) = Project();
        var folder = project.AddFolder("F");
        project.MoveDiagram(a.Id, folder.Id);

        project.RemoveDiagram(a.Id);

        Assert.Empty(folder.DiagramIds);
        Assert.Null(project.FindDiagram(a.Id));
    }

    [Fact]
    public void Folders_RoundTrip_AndOldProjectsLoadWithoutFolders()
    {
        var (project, a, b, _) = Project();
        var folder = project.AddFolder("Domain");
        project.AddFolder("Nested", folder.Id);
        project.MoveDiagram(b.Id, folder.Id);
        project.MoveDiagram(a.Id, folder.Id);

        var json = ProjectSerializer.Serialize(project);
        var loaded = ProjectSerializer.Deserialize(json);

        var loadedFolder = Assert.Single(loaded.Folders);
        Assert.Equal("Domain", loadedFolder.Name);
        Assert.Equal([b.Id, a.Id], loadedFolder.DiagramIds);
        Assert.Equal("Nested", Assert.Single(loadedFolder.Folders).Name);

        var schema2 = System.Text.Json.Nodes.JsonNode.Parse(json)!.AsObject();
        schema2.Remove("folders");
        schema2["schemaVersion"] = 2;
        var old = ProjectSerializer.Deserialize(schema2.ToJsonString());
        Assert.Empty(old.Folders);
        Assert.Equal(3, old.TopLevelDiagrams().Count());
    }

    [Fact]
    public void Load_RepairsInvalidFolderReferences()
    {
        var (project, a, _, _) = Project();
        var json = ProjectSerializer.Serialize(project).Replace(
            "\"folders\": []",
            $$"""
            "folders": [
              { "id": "f1", "name": "One", "diagramIds": [ "{{a.Id}}", "missing", "{{a.Id}}" ] },
              { "id": "f1", "name": "Two", "diagramIds": [ "{{a.Id}}" ] }
            ]
            """);

        var loaded = ProjectSerializer.Deserialize(json);

        Assert.Equal([a.Id], loaded.Folders[0].DiagramIds);
        Assert.Empty(loaded.Folders[1].DiagramIds);
        Assert.NotEqual(loaded.Folders[0].Id, loaded.Folders[1].Id);
    }
}
