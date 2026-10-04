using UmlWizard.Core.Editing;
using UmlWizard.Core.Model;
using UmlWizard.Core.Rendering;
using UmlWizard.Core.Serialization;

namespace UmlWizard.Core.Tests;

public sealed class TaggedValueTests
{
    [Fact]
    public void SetAndGet_AddReplaceAndRemove()
    {
        var values = new List<TaggedValue>();

        values.Set("required", "true");
        values.Set("openapi.schema", "{}");
        values.Set("required", "false");
        Assert.Equal(["required=false", "openapi.schema={}"], values.Select(v => $"{v.Name}={v.Value}"));
        Assert.Equal("{}", values.Get("openapi.schema"));

        values.Set("required", null);
        Assert.Null(values.Get("required"));
        Assert.Single(values);
    }

    [Fact]
    public void TaggedValuesAndEndpoints_AreSaved_AndEmptyListsAreLeftOut()
    {
        var document = DiagramDocument.Create("Api", DiagramType.Class);
        document.TaggedValues.Set("openapi.servers", "[{\"url\":\"https://example.com\"}]");
        var cls = new UmlClass { Name = "Orders" };
        cls.TaggedValues.Set("openapi.schema", "{\"description\":\"x\"}");
        cls.Attributes.Add(new UmlAttribute { Name = "id", Type = "string", TaggedValues = [new() { Name = "required", Value = "true" }] });
        cls.Attributes.Add(new UmlAttribute { Name = "plain" });
        cls.Operations.Add(new UmlOperation { Name = "getOrder", HttpMethod = "GET", Path = "/orders/{id}" });
        ((ClassDiagramModel)document.Model).Classes.Add(cls);

        var json = DiagramSerializer.Serialize(document);
        var loaded = DiagramSerializer.Deserialize(json);

        var loadedClass = ((ClassDiagramModel)loaded.Model).Classes.Single();
        Assert.Equal("[{\"url\":\"https://example.com\"}]", loaded.TaggedValues.Get("openapi.servers"));
        Assert.Equal("{\"description\":\"x\"}", loadedClass.TaggedValues.Get("openapi.schema"));
        Assert.Equal("true", loadedClass.Attributes[0].TaggedValues.Get("required"));
        Assert.Equal(("GET", "/orders/{id}"), (loadedClass.Operations[0].HttpMethod, loadedClass.Operations[0].Path));
        // Only the lists that have values are written: the diagram's, the class's and the first attribute's.
        Assert.Equal(3, json.Split("\"taggedValues\"").Length - 1);
    }

    [Fact]
    public void InlineEdits_KeepEndpointAndTaggedValues()
    {
        var editor = new DiagramEditor(DiagramDocument.Create("Api", DiagramType.Class));
        var cls = editor.AddClass(ClassifierKind.Interface, 0, 0);
        cls.Attributes.Add(new UmlAttribute { Name = "id", Type = "int", TaggedValues = [new() { Name = "required", Value = "true" }] });
        cls.Operations.Add(new UmlOperation
        {
            Name = "getOrder",
            HttpMethod = "GET",
            Path = "/orders/{id}",
            TaggedValues = [new() { Name = "openapi.operation", Value = "{\"summary\":\"Get\"}" }],
        });

        editor.BeginInlineEdit(new HitInfo(cls.Id, HitPart.ClassAttribute, 0))!.Commit("+id: long");
        editor.BeginInlineEdit(new HitInfo(cls.Id, HitPart.ClassOperation, 0))!.Commit("+findOrder(id: long): Order");

        Assert.Equal(("long", "true"), (cls.Attributes[0].Type, cls.Attributes[0].TaggedValues.Get("required")));
        var operation = cls.Operations[0];
        Assert.Equal(("findOrder", "GET", "/orders/{id}"), (operation.Name, operation.HttpMethod, operation.Path));
        Assert.Equal("{\"summary\":\"Get\"}", operation.TaggedValues.Get("openapi.operation"));
    }

    [Fact]
    public void Undo_RestoresTheDiagramsTaggedValues()
    {
        var editor = new DiagramEditor(DiagramDocument.Create("Api", DiagramType.Class));
        editor.Execute(() => editor.Document.TaggedValues.Set("owner", "team-a"));
        editor.Execute(() => editor.Document.TaggedValues.Set("owner", "team-b"));

        editor.Undo();

        Assert.Equal("team-a", editor.Document.TaggedValues.Get("owner"));
    }

    [Fact]
    public void Endpoint_IsDrawnBeforeTheSignature_AndWidensTheClass()
    {
        var editor = new DiagramEditor(DiagramDocument.Create("Api", DiagramType.Class));
        var cls = editor.AddClass(ClassifierKind.Interface, 0, 0);
        cls.Operations.Add(new UmlOperation { Name = "get" });
        using var renderer = (ClassDiagramRenderer)DiagramRenderer.Create(DiagramType.Class);
        var layout = editor.Document.Layout.Find(cls.Id)!;
        var plain = renderer.Measure(cls, layout, null).Bounds.Width;

        cls.Operations[0].HttpMethod = "get";
        cls.Operations[0].Path = "/a/very/long/path/to/some/resource/{id}";
        var endpoint = renderer.Measure(cls, layout, null);

        Assert.True(endpoint.Bounds.Width > plain);
        Assert.Equal("«GET /a/very/long/path/to/some/resource/{id}»", endpoint.OperationLines[0].Prefix);
    }
}
