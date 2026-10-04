using Avalonia.Headless.XUnit;
using UmlWizard.App.ViewModels;
using UmlWizard.App.ViewModels.Dialogs;
using UmlWizard.Core.Model;
using UmlWizard.OpenApi;

namespace UmlWizard.App.Tests;

public sealed class OpenApiUiTests
{
    private const string Spec = """
        openapi: 3.0.3
        info: { title: Shop API, version: 2.1.0 }
        paths:
          /orders/{id}:
            get:
              tags: [orders]
              operationId: getOrder
              parameters:
                - { name: id, in: path, required: true, schema: { type: string } }
              responses:
                '200':
                  description: The order
                  content:
                    application/json:
                      schema: { $ref: '#/components/schemas/Order' }
        components:
          schemas:
            Order:
              type: object
              properties:
                id: { type: string }
        """;

    [AvaloniaFact]
    public async Task ImportOpenApi_AddsAClassDiagramToTheSelectedFolder_AndOpensIt()
    {
        using var f = new ExplorerFixture();
        var file = Path.Combine(f.Folder, "shop.yaml");
        File.WriteAllText(file, Spec);
        f.Services.OpenFilePath = file;
        f.Main.SelectedNode = f.Node("Nested");

        await f.Main.ImportOpenApiCommand.ExecuteAsync(null);

        var node = Assert.IsType<DiagramItemViewModel>(f.Main.SelectedNode);
        Assert.Equal(("Shop API", "Nested"), (node.Name, node.Parent?.Name));
        Assert.Equal("Shop API", f.Main.ActiveDiagram?.Name);
        var saved = f.Saved.FindDiagram(node.Id)!;
        var model = (ClassDiagramModel)saved.Model;
        Assert.Equal(["OrdersApi", "Order"], model.Classes.Select(c => c.Name));
        Assert.Equal(("GET", "/orders/{id}"), (model.Classes[0].Operations[0].HttpMethod, model.Classes[0].Operations[0].Path));
        Assert.Empty(f.Services.Messages);
        Assert.Empty(f.Services.Errors);
    }

    [AvaloniaFact]
    public async Task ImportOpenApi_ReportsFilesThatAreNotOpenApi_AndAddsNothing()
    {
        using var f = new ExplorerFixture();
        var file = Path.Combine(f.Folder, "notes.yaml");
        File.WriteAllText(file, "shopping: [milk, eggs]");
        f.Services.OpenFilePath = file;
        var diagrams = f.Main.Project!.Diagrams.Count;

        await f.Main.ImportOpenApiCommand.ExecuteAsync(null);

        Assert.Contains("OpenAPI", Assert.Single(f.Services.Errors));
        Assert.Equal(diagrams, f.Main.Project.Diagrams.Count);
    }

    [AvaloniaFact]
    public async Task ExportOpenApi_WritesTheOpenDiagramAsShown_AndCanBeImportedAgain()
    {
        using var f = new ExplorerFixture();
        var file = Path.Combine(f.Folder, "shop.yaml");
        File.WriteAllText(file, Spec);
        f.Services.OpenFilePath = file;
        await f.Main.ImportOpenApiCommand.ExecuteAsync(null);

        // An unsaved change in the open tab is exported.
        var open = f.Main.ActiveDiagram!;
        open.Editor.Execute(() => ((ClassDiagramModel)open.Document.Model).Classes[1].Attributes.Add(new UmlAttribute { Name = "total", Type = "decimal" }));
        Assert.True(f.Main.ExportOpenApiCommand.CanExecute(null));
        f.Services.FillDialog = d =>
        {
            var options = Assert.IsType<ExportOpenApiViewModel>(d);
            Assert.Equal(("Shop API.yaml", "Shop API", "2.1.0"), (options.FileName, options.ApiTitle, options.ApiVersion));
            options.FileName = "shop-out.json";
            options.ApiVersion = "2.2.0";
            Assert.True(options.TryAccept());
        };

        await f.Main.ExportOpenApiCommand.ExecuteAsync(null);

        var written = Path.Combine(f.Folder, "shop-out.json");
        var json = File.ReadAllText(written);
        Assert.StartsWith("{", json);
        Assert.Contains("\"version\": \"2.2.0\"", json);
        Assert.Contains("\"total\"", json);
        Assert.Contains(written, Assert.Single(f.Services.Messages));

        var again = OpenApiImporter.ImportFile(written);
        Assert.Equal(["OrdersApi", "Order"], ((ClassDiagramModel)again.Document.Model).Classes.Select(c => c.Name));
    }

    [AvaloniaFact]
    public void ExportOpenApi_IsOnlyOfferedForClassDiagrams()
    {
        using var f = new ExplorerFixture();

        f.Main.SelectedNode = f.Node("A");
        Assert.True(f.Main.ExportOpenApiCommand.CanExecute(null));
        f.Main.SelectedNode = f.Node("B");
        Assert.False(f.Main.ExportOpenApiCommand.CanExecute(null));
    }

    [AvaloniaFact]
    public void ClassEditor_EditsEndpointsAndTaggedValues_OfClassAndMembers()
    {
        var cls = new UmlClass
        {
            Name = "OrdersApi",
            Kind = ClassifierKind.Interface,
            TaggedValues = [new() { Name = "openapi.tag", Value = "orders" }],
            Attributes = [new() { Name = "version", TaggedValues = [new() { Name = "required", Value = "true" }] }],
            Operations = [new() { Name = "getOrder", HttpMethod = "get", Path = "/orders/{id}" }],
        };
        var dialog = new ClassEditorViewModel(cls);

        Assert.Same(dialog.TaggedValues, dialog.SelectedTaggedValues);
        Assert.Equal("Tagged values of the class", dialog.TaggedValuesHeader);
        Assert.Equal(("GET", "/orders/{id}"), (dialog.Operations[0].HttpMethod, dialog.Operations[0].Path));

        dialog.Attributes[0].EditTaggedValuesCommand.Execute(null);
        Assert.Equal("Tagged values of attribute version", dialog.TaggedValuesHeader);
        dialog.SelectedTaggedValues.Rows[0].Value = "false";
        dialog.SelectedTaggedValues.AddCommand.Execute(null);
        dialog.SelectedTaggedValues.Rows[1].Name = "  note ";
        dialog.SelectedTaggedValues.Rows[1].Value = "hi";

        dialog.Operations[0].EditTaggedValuesCommand.Execute(null);
        Assert.Same(dialog.Operations[0].TaggedValues, dialog.SelectedTaggedValues);
        dialog.SelectedTaggedValues.AddCommand.Execute(null); // left without a name: dropped
        dialog.Operations[0].HttpMethod = "";
        dialog.Operations[0].RemoveCommand.Execute(null);
        Assert.Same(dialog.TaggedValues, dialog.SelectedTaggedValues);
        dialog.AddOperationCommand.Execute(null);
        dialog.Operations[0].HttpMethod = "POST";
        dialog.Operations[0].Path = " /orders ";

        dialog.ApplyTo(cls);

        Assert.Equal("orders", cls.TaggedValues.Get("openapi.tag"));
        Assert.Equal(["required=false", "note=hi"], cls.Attributes[0].TaggedValues.Select(t => $"{t.Name}={t.Value}"));
        Assert.Equal(("POST", "/orders"), (cls.Operations[0].HttpMethod, cls.Operations[0].Path));
        Assert.Empty(cls.Operations[0].TaggedValues);
    }

    [AvaloniaFact]
    public async Task DiagramProperties_EditTheDiagramsTaggedValues_AlsoInTheOpenTab()
    {
        using var f = new ExplorerFixture();
        f.Main.SelectedNode = f.Node("A");
        f.Main.OpenDiagramCommand.Execute(f.Node("A"));
        f.Main.SelectedNode = f.Node("A");
        f.Services.FillDialog = d =>
        {
            var dialog = Assert.IsType<DiagramPropertiesViewModel>(d);
            dialog.TaggedValues.AddCommand.Execute(null);
            dialog.TaggedValues.Rows[0].Name = "openapi.servers";
            dialog.TaggedValues.Rows[0].Value = "[{\"url\":\"https://example.com\"}]";
            Assert.True(dialog.TryAccept());
        };

        await f.Main.EditDiagramPropertiesCommand.ExecuteAsync(null);

        var id = f.Node("A").Id;
        Assert.Equal("[{\"url\":\"https://example.com\"}]", f.Saved.FindDiagram(id)!.TaggedValues.Get("openapi.servers"));
        Assert.Equal("[{\"url\":\"https://example.com\"}]", f.Main.OpenDiagrams.Single(d => d.Id == id).Document.TaggedValues.Get("openapi.servers"));
    }
}
