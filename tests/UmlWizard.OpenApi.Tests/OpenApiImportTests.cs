using System.Text.Json.Nodes;
using UmlWizard.Core.Model;
using UmlWizard.Core.Rendering;
using UmlWizard.OpenApi;

namespace UmlWizard.OpenApi.Tests;

public sealed class OpenApiImportTests
{
    internal static string Fixture(string name) => Path.Combine(AppContext.BaseDirectory, "Fixtures", name);

    internal static ClassDiagramModel Model(OpenApiImportResult result) => (ClassDiagramModel)result.Document.Model;

    internal static UmlClass Class(OpenApiImportResult result, string name) => Model(result).Classes.Single(c => c.Name == name);

    [Fact]
    public void PetStore_SchemasBecomeClassesAndEnumerations()
    {
        var result = OpenApiImporter.ImportFile(Fixture("petstore-3.0.yaml"));

        Assert.Equal("Pet Store", result.Document.Name);
        Assert.Equal("A sample store of pets, written for the UML Wizard tests.", result.Document.Description);
        Assert.Equal(DiagramType.Class, result.Document.DiagramType);

        var newPet = Class(result, "NewPet");
        Assert.Equal(ClassifierKind.Class, newPet.Kind);
        Assert.Equal(
            ["name: string", "tag: string", "status: PetStatus", "birthDate: Date", "photoUrls: List<string>", "owner: Owner", "weight: double"],
            newPet.Attributes.Select(a => $"{a.Name}: {a.Type}"));
        Assert.Equal("true", newPet.Attributes[0].TaggedValues.Get(OpenApiTags.Required));
        Assert.Null(newPet.Attributes[1].TaggedValues.Get(OpenApiTags.Required));
        Assert.Equal("{\"example\":\"Rex\"}", newPet.Attributes[0].TaggedValues.Get(OpenApiTags.Property));
        Assert.Equal("{\"nullable\":true}", newPet.Attributes[1].TaggedValues.Get(OpenApiTags.Property));
        Assert.Equal("1.5", newPet.Attributes[6].DefaultValue);
        Assert.Equal("{\"description\":\"A pet as it is created.\"}", newPet.TaggedValues.Get(OpenApiTags.Schema));

        var status = Class(result, "PetStatus");
        Assert.Equal(ClassifierKind.Enumeration, status.Kind);
        Assert.Equal(["available", "pending", "sold"], status.Attributes.Select(a => a.Name));
        Assert.Empty(status.TaggedValues);

        // Pet: allOf NewPet plus its own id.
        var pet = Class(result, "Pet");
        Assert.Equal(["id: long"], pet.Attributes.Select(a => $"{a.Name}: {a.Type}"));
        var model = Model(result);
        Assert.Contains(model.Relationships, r => r.Kind == RelationshipKind.Inheritance && r.SourceId == pet.Id && r.TargetId == newPet.Id);

        // A property referring to a class is an attribute and a directed association; enumerations get no line.
        var owner = Class(result, "Owner");
        var association = Assert.Single(model.Relationships, r => r.Kind == RelationshipKind.DirectedAssociation);
        Assert.Equal((newPet.Id, owner.Id, "0..1"), (association.SourceId, association.TargetId, association.TargetMultiplicity));
        Assert.Equal(["id: Guid", "email: string"], owner.Attributes.Select(a => $"{a.Name}: {a.Type}"));
    }

    [Fact]
    public void PetStore_EndpointsBecomeOperationsOfApiInterfaces()
    {
        var result = OpenApiImporter.ImportFile(Fixture("petstore-3.0.yaml"));

        var pets = Class(result, "PetsApi");
        Assert.Equal((ClassifierKind.Interface, "api", "pets"), (pets.Kind, pets.Stereotype, pets.TaggedValues.Get(OpenApiTags.Tag)));
        Assert.Equal(
            [
                "GET /pets listPets(limit: int, status: PetStatus): List<Pet>",
                "POST /pets createPet(body: NewPet): Pet",
                "GET /pets/{petId} showPetById(petId: string): Pet",
                "DELETE /pets/{petId} deletePet(petId: string): ",
            ],
            pets.Operations.Select(o =>
                $"{o.HttpMethod} {o.Path} {o.Name}({string.Join(", ", o.Parameters.Select(p => $"{p.Name}: {p.Type}"))}): {o.ReturnType}"));
        Assert.Equal("getInventory", Assert.Single(Class(result, "StoreApi").Operations).Name);

        var list = JsonNode.Parse(pets.Operations[0].TaggedValues.Get(OpenApiTags.Operation)!)!.AsObject();
        Assert.False(list.ContainsKey("operationId"));
        Assert.False(list.ContainsKey("tags"));
        Assert.Equal("List all pets", list["summary"]!.GetValue<string>());
        Assert.Equal(2, list["parameters"]!.AsArray().Count);
    }

    [Fact]
    public void PetStore_WhatUmlHasNoPlaceFor_IsKeptOnTheDiagram()
    {
        var result = OpenApiImporter.ImportFile(Fixture("petstore-3.0.yaml"));
        var tags = result.Document.TaggedValues;

        Assert.Equal("3.0.3", tags.Get(OpenApiTags.SourceVersion));
        Assert.Contains("\"license\"", tags.Get(OpenApiTags.Info));
        Assert.Contains("https://api.example.com/v1", tags.Get("openapi.servers"));
        Assert.Equal("\"hand\"", tags.Get("openapi.x-generated-by"));
        var components = JsonNode.Parse(tags.Get(OpenApiTags.Components)!)!.AsObject();
        Assert.Equal(["parameters", "responses", "securitySchemes"], components.Select(p => p.Key));
        Assert.Empty(result.Warnings);
    }

    [Fact]
    public void Swagger2_IsReadAsOpenApi3()
    {
        var result = OpenApiImporter.ImportFile(Fixture("library-2.0.json"));
        var tags = result.Document.TaggedValues;

        Assert.Equal("2.0", tags.Get(OpenApiTags.SourceVersion));
        Assert.Equal("[{\"url\":\"https://library.example.com/api\"}]", tags.Get("openapi.servers"));
        var schemes = JsonNode.Parse(tags.Get(OpenApiTags.Components)!)!["securitySchemes"]!;
        Assert.Equal("http", schemes["basicAuth"]!["type"]!.GetValue<string>());
        Assert.NotNull(schemes["oauth"]!["flows"]!["authorizationCode"]!["tokenUrl"]);

        var book = Class(result, "Book");
        Assert.Equal(["isbn: string", "title: string", "published: DateTime", "subtitle: string", "authors: List<Author>"],
            book.Attributes.Select(a => $"{a.Name}: {a.Type}"));
        Assert.Equal("{\"nullable\":true}", book.Attributes[3].TaggedValues.Get(OpenApiTags.Property));

        var books = Class(result, "BooksApi");
        Assert.Equal(
            ["listBooks(author: string, ids: List<int>): List<Book>", "addBook(body: Book): Book", "getBook(isbn: string): Book", "uploadCover(isbn: string, body: object): "],
            books.Operations.Select(o => $"{o.Name}({string.Join(", ", o.Parameters.Select(p => $"{p.Name}: {p.Type}"))}): {o.ReturnType}"));

        // The form upload is a multipart request body; the path-level isbn became the operation's own parameter.
        var upload = JsonNode.Parse(books.Operations[3].TaggedValues.Get(OpenApiTags.Operation)!)!;
        Assert.Equal("binary", upload["requestBody"]!["content"]!["multipart/form-data"]!["schema"]!["properties"]!["file"]!["format"]!.GetValue<string>());
        var getBook = JsonNode.Parse(books.Operations[2].TaggedValues.Get(OpenApiTags.Operation)!)!;
        Assert.Equal("isbn", getBook["parameters"]![0]!["name"]!.GetValue<string>());
        Assert.Equal("#/components/responses/NotFound", getBook["responses"]!["404"]!["$ref"]!.GetValue<string>());
    }

    [Fact]
    public void OpenApi31_TypeListsWithNull_BecomeNullable()
    {
        const string text = """
            openapi: 3.1.0
            info: { title: T, version: '1' }
            paths: {}
            webhooks:
              newPet: {}
            components:
              schemas:
                Thing:
                  type: object
                  properties:
                    name: { type: [string, 'null'] }
                    count: { type: integer, exclusiveMinimum: 0 }
                    kind: { const: box }
            """;

        var result = OpenApiImporter.Import(text, "fallback");
        var thing = Class(result, "Thing");

        Assert.Equal(["name: string", "count: int", "kind: object"], thing.Attributes.Select(a => $"{a.Name}: {a.Type}"));
        Assert.Equal("{\"nullable\":true}", thing.Attributes[0].TaggedValues.Get(OpenApiTags.Property));
        Assert.Equal("{\"exclusiveMinimum\":true,\"minimum\":0}", thing.Attributes[1].TaggedValues.Get(OpenApiTags.Property));
        Assert.Equal("{\"enum\":[\"box\"]}", thing.Attributes[2].TaggedValues.Get(OpenApiTags.Property));
        Assert.Equal("{\"newPet\":{}}", result.Document.TaggedValues.Get("openapi.webhooks"));
    }

    [Fact]
    public void OperationsWithoutTagOrId_AreGroupedByPath_AndNamed()
    {
        const string text = """
            {
              "openapi": "3.0.0",
              "info": { "title": "Ops", "version": "1" },
              "paths": {
                "/health/{component}": {
                  "parameters": [ { "name": "component", "in": "path", "required": true, "schema": { "type": "string" } } ],
                  "summary": "Health",
                  "get": { "responses": { "200": { "description": "OK" } } }
                }
              }
            }
            """;

        var result = OpenApiImporter.Import(text, "fallback");
        var api = Class(result, "HealthApi");

        Assert.Equal("true", api.TaggedValues.Get(OpenApiTags.Untagged));
        var operation = Assert.Single(api.Operations);
        Assert.Equal("getHealthByComponent", operation.Name);
        Assert.Equal("component", Assert.Single(operation.Parameters).Name);
        Assert.Contains(result.Warnings, w => w.Contains("\"summary\""));
    }

    [Fact]
    public void NotAnOpenApiDescription_IsRefused()
    {
        Assert.Throws<InvalidDataException>(() => OpenApiImporter.Import("name: not an api", "x"));
        Assert.Throws<InvalidDataException>(() => OpenApiImporter.Import("{ \"openapi\": ", "x"));
        Assert.Throws<InvalidDataException>(() => OpenApiImporter.Import("- a\n- b", "x"));
    }

    [Fact]
    public void Layout_PutsApisFirst_AndNothingOverlaps()
    {
        var result = OpenApiImporter.ImportFile(Fixture("petstore-3.0.yaml"));
        using var renderer = DiagramRenderer.Create(DiagramType.Class);
        var bounds = Model(result).Classes.ToDictionary(c => c, c => renderer.GetElementBounds(result.Document, c.Id)!.Value);

        var apiBottom = bounds.Where(p => p.Key.Stereotype == "api").Max(p => p.Value.Bottom);
        Assert.All(bounds.Where(p => p.Key.Stereotype != "api"), p => Assert.True(p.Value.Top > apiBottom));
        foreach (var (a, ra) in bounds)
        {
            foreach (var (b, rb) in bounds.Where(p => p.Key != a))
            {
                Assert.False(ra.IntersectsWith(rb), $"{a.Name} overlaps {b.Name}");
            }
        }
    }
}
