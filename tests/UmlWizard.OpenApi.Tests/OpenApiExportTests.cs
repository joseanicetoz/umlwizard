using System.Text.Json.Nodes;
using UmlWizard.Core.Model;
using UmlWizard.Core.Serialization;
using UmlWizard.OpenApi;
using static UmlWizard.OpenApi.Tests.OpenApiImportTests;

namespace UmlWizard.OpenApi.Tests;

public sealed class OpenApiExportTests
{
    private static JsonObject Export(DiagramDocument document, out IReadOnlyList<string> warnings, OpenApiFormat format = OpenApiFormat.Json)
    {
        var result = OpenApiExporter.Export(document, new OpenApiExportOptions(Format: format));
        warnings = result.Warnings;
        return (JsonObject)YamlJson.Parse(result.Text)!;
    }

    private static string Differences(JsonNode? expected, JsonNode? actual, string path = "$")
    {
        if (JsonNode.DeepEquals(expected, actual))
        {
            return string.Empty;
        }

        if (expected is JsonObject e && actual is JsonObject a)
        {
            return string.Join("", e.Select(p => p.Key).Union(a.Select(p => p.Key)).Select(k => Differences(e[k], a[k], $"{path}.{k}")));
        }

        return $"\n{path}: expected {expected?.ToJsonString() ?? "(none)"}, got {actual?.ToJsonString() ?? "(none)"}";
    }

    [Theory]
    [InlineData(OpenApiFormat.Json)]
    [InlineData(OpenApiFormat.Yaml)]
    public void PetStore_ImportedAndExported_IsTheSameDescription(OpenApiFormat format)
    {
        var original = YamlJson.Parse(File.ReadAllText(Fixture("petstore-3.0.yaml")));
        var imported = OpenApiImporter.ImportFile(Fixture("petstore-3.0.yaml"));

        var exported = Export(imported.Document, out var warnings, format);

        Assert.Equal(string.Empty, Differences(original, exported));
        Assert.Empty(warnings);
    }

    [Fact]
    public void PetStore_SurvivesSavingTheProject()
    {
        var imported = OpenApiImporter.ImportFile(Fixture("petstore-3.0.yaml"));
        var saved = DiagramSerializer.Deserialize(DiagramSerializer.Serialize(imported.Document));

        Assert.Equal(string.Empty, Differences(Export(imported.Document, out _), Export(saved, out _)));
    }

    [Fact]
    public void Swagger2_IsExportedAsValidOpenApi30()
    {
        var imported = OpenApiImporter.ImportFile(Fixture("library-2.0.json"));

        var exported = Export(imported.Document, out var warnings);

        Assert.Equal("3.0.3", exported["openapi"]!.GetValue<string>());
        Assert.False(exported.ContainsKey("swagger") || exported.ContainsKey("host") || exported.ContainsKey("definitions"));
        Assert.Equal("0.9", exported["info"]!["version"]!.GetValue<string>());
        var add = exported["paths"]!["/books"]!["post"]!;
        Assert.Equal("#/components/schemas/Book", add["requestBody"]!["content"]!["application/json"]!["schema"]!["$ref"]!.GetValue<string>());
        Assert.Equal("#/components/schemas/Author", exported["components"]!["schemas"]!["Book"]!["properties"]!["authors"]!["items"]!["$ref"]!.GetValue<string>());
        Assert.DoesNotContain("#/definitions/", exported.ToJsonString());
        var upload = exported["paths"]!["/books/{isbn}/cover"]!["put"]!;
        Assert.NotNull(upload["requestBody"]!["content"]!["multipart/form-data"]);
        Assert.Null(upload["requestBody"]!["content"]!["application/json"]);
        Assert.Empty(warnings);
    }

    [Fact]
    public void ExportingTheOnlineShopTwice_GivesTheSameDescription()
    {
        var project = ProjectSerializer.Load(Path.Combine(AppContext.BaseDirectory, "samples", "OnlineShop", "OnlineShop.umlwiz"));
        var domain = project.Diagrams.First(d => d.DiagramType == DiagramType.Class);

        var first = Export(domain, out _);
        var again = Export(OpenApiImporter.Import(first.ToJsonString(), "again").Document, out var warnings);

        Assert.Equal(string.Empty, Differences(first, again));
        Assert.Empty(warnings);

        // Associations and compositions are properties referring to the other schema.
        var schemas = first["components"]!["schemas"]!.AsObject();
        Assert.Contains("Order", schemas.Select(p => p.Key));
        Assert.Contains("#/components/schemas/", schemas["Order"]!.ToJsonString());
        Assert.Empty(first["paths"]!.AsObject());
    }

    [Fact]
    public void DiagramChanges_WinOverWhatTheImportKept()
    {
        var imported = OpenApiImporter.ImportFile(Fixture("petstore-3.0.yaml"));
        var newPet = Class(imported, "NewPet");
        newPet.Attributes.Single(a => a.Name == "name").Type = "List<string>";
        newPet.Attributes.Add(new UmlAttribute { Name = "age", Type = "int", Visibility = UmlVisibility.Public });
        var pets = Class(imported, "PetsApi");
        var list = pets.Operations.Single(o => o.Name == "listPets");
        list.Parameters.RemoveAll(p => p.Name == "status");
        list.Parameters.Add(new UmlParameter { Name = "sort", Type = "string" });
        list.ReturnType = "List<NewPet>";
        pets.Operations.Add(new UmlOperation { Name = "countPets", HttpMethod = "GET", Path = "/pets/count", ReturnType = "int" });

        var exported = Export(imported.Document, out _);

        var name = exported["components"]!["schemas"]!["NewPet"]!["properties"]!["name"]!;
        Assert.Equal("{\"type\":\"array\",\"items\":{\"type\":\"string\"},\"example\":\"Rex\"}", name.ToJsonString());
        Assert.Equal("{\"type\":\"integer\"}", exported["components"]!["schemas"]!["NewPet"]!["properties"]!["age"]!.ToJsonString());

        var get = exported["paths"]!["/pets"]!["get"]!;
        Assert.Equal(["limit", "sort"], get["parameters"]!.AsArray().Select(p => p!["name"]!.GetValue<string>()));
        Assert.Equal("#/components/schemas/NewPet", get["responses"]!["200"]!["content"]!["application/json"]!["schema"]!["items"]!["$ref"]!.GetValue<string>());
        Assert.Equal("A link to the next page of responses", get["responses"]!["200"]!["headers"]!["x-next"]!["description"]!.GetValue<string>());

        var count = exported["paths"]!["/pets/count"]!["get"]!;
        Assert.Equal("{\"tags\":[\"pets\"],\"operationId\":\"countPets\",\"responses\":{\"200\":{\"description\":\"OK\",\"content\":{\"application/json\":{\"schema\":{\"type\":\"integer\"}}}}}}",
            count.ToJsonString());
    }

    [Fact]
    public void ADiagramDrawnByHand_IsExported()
    {
        var document = DiagramDocument.Create("Orders", DiagramType.Class);
        var model = (ClassDiagramModel)document.Model;
        var order = new UmlClass { Name = "Order", Attributes = [new() { Name = "id", Type = "Guid" }, new() { Name = "lines", Type = "OrderLine[]" }] };
        var line = new UmlClass { Name = "OrderLine", Attributes = [new() { Name = "quantity", Type = "int" }, new() { Name = "note", Type = "Memo" }] };
        var api = new UmlClass
        {
            Name = "OrdersApi",
            Kind = ClassifierKind.Interface,
            Stereotype = "api",
            Operations =
            [
                new() { Name = "getOrder", HttpMethod = "get", Path = "/orders/{id}", Parameters = [new() { Name = "id", Type = "Guid" }], ReturnType = "Order" },
                new() { Name = "addOrder", HttpMethod = "POST", Path = "/orders", Parameters = [new() { Name = "order", Type = "Order" }] },
                new() { Name = "notAnEndpoint" },
            ],
        };
        model.Classes.AddRange([api, order, line]);

        var exported = Export(document, out var warnings);

        Assert.Equal("Orders", exported["info"]!["title"]!.GetValue<string>());
        Assert.Equal("1.0.0", exported["info"]!["version"]!.GetValue<string>());
        Assert.Equal(["Order", "OrderLine"], exported["components"]!["schemas"]!.AsObject().Select(p => p.Key));
        var get = exported["paths"]!["/orders/{id}"]!["get"]!;
        Assert.Equal(["Orders"], get["tags"]!.AsArray().Select(t => t!.GetValue<string>()));
        Assert.Equal("{\"name\":\"id\",\"in\":\"path\",\"required\":true,\"schema\":{\"type\":\"string\",\"format\":\"uuid\"}}", get["parameters"]![0]!.ToJsonString());
        var post = exported["paths"]!["/orders"]!["post"]!;
        Assert.Null(post["parameters"]);
        Assert.Equal("#/components/schemas/Order", post["requestBody"]!["content"]!["application/json"]!["schema"]!["$ref"]!.GetValue<string>());
        Assert.Equal("No Content", post["responses"]!["204"]!["description"]!.GetValue<string>());

        Assert.Contains(warnings, w => w.Contains("OrdersApi.notAnEndpoint") && w.Contains("no HTTP method"));
        Assert.Contains(warnings, w => w.StartsWith("Memo "));
    }

    [Fact]
    public void Webhooks_AreNotWrittenToOpenApi30()
    {
        var document = DiagramDocument.Create("Hooks", DiagramType.Class);
        document.TaggedValues.Set("openapi.webhooks", "{\"newPet\":{}}");
        document.TaggedValues.Set("openapi.servers", "not json");

        var exported = Export(document, out var warnings);

        Assert.False(exported.ContainsKey("webhooks") || exported.ContainsKey("servers"));
        Assert.Contains(warnings, w => w.Contains("\"webhooks\""));
        Assert.Contains(warnings, w => w.Contains("openapi.servers") && w.Contains("not valid JSON"));
    }

    [Fact]
    public void Yaml_QuotesStringsThatWouldReadAsOtherTypes()
    {
        var node = new JsonObject
        {
            ["version"] = "1.0",
            ["flag"] = "true",
            ["empty"] = "",
            ["count"] = 3,
            ["enabled"] = false,
            ["nothing"] = null,
            ["text"] = "plain words",
            ["list"] = new JsonArray("a", 1),
            ["none"] = new JsonArray(),
        };

        var yaml = YamlJson.ToYaml(node);

        Assert.Contains("version: \"1.0\"", yaml);
        Assert.Contains("flag: \"true\"", yaml);
        Assert.Contains("count: 3", yaml);
        Assert.Contains("text: plain words", yaml);
        Assert.Contains("none: []", yaml);
        Assert.True(JsonNode.DeepEquals(node, YamlJson.Parse(yaml)), yaml);
    }
}
