using System.Text.Json.Nodes;
using UmlWizard.CodeGeneration;
using UmlWizard.Core.Model;
using UmlWizard.Core.Rendering;

namespace UmlWizard.OpenApi;

/// <summary>A class diagram made from an OpenAPI description, and what could not be kept.</summary>
public sealed record OpenApiImportResult(DiagramDocument Document, IReadOnlyList<string> Warnings);

/// <summary>
/// Makes a class diagram from an OpenAPI 3.x or Swagger 2.0 description (JSON or YAML): schemas become classes and
/// enumerations, <c>allOf</c> references become inheritance, and the endpoints of each tag become the operations of
/// an «api» interface. What UML has no place for is kept in tagged values (see <see cref="OpenApiTags"/>), so
/// exporting the diagram gives the description back.
/// </summary>
public static class OpenApiImporter
{
    private static readonly string[] Methods = ["get", "put", "post", "delete", "options", "head", "patch", "trace"];

    /// <summary>Reads the file; the diagram is named after the API's title, or the file when it has none.</summary>
    /// <exception cref="InvalidDataException">The file is not an OpenAPI or Swagger description.</exception>
    public static OpenApiImportResult ImportFile(string path) =>
        Import(File.ReadAllText(path), Path.GetFileNameWithoutExtension(path));

    /// <exception cref="InvalidDataException">The text is not an OpenAPI or Swagger description.</exception>
    public static OpenApiImportResult Import(string text, string fallbackName)
    {
        if (YamlJson.Parse(text) is not JsonObject root)
        {
            throw new InvalidDataException("The file is not an OpenAPI description: it does not contain an object.");
        }

        var warnings = new List<string>();
        var source = OpenApiNormalizer.Normalize(root, warnings);
        var document = new Importer(root, warnings).Build(fallbackName, source);
        ClassDiagramLayout.Arrange(document, c => c.Kind == ClassifierKind.Interface && c.Stereotype == OpenApiTags.ApiStereotype);
        return new OpenApiImportResult(document, warnings);
    }

    private sealed class Importer(JsonObject root, List<string> warnings)
    {
        private readonly ClassDiagramModel _model = new();
        private readonly Dictionary<string, UmlClass> _bySchema = new(StringComparer.Ordinal);
        private readonly Dictionary<string, UmlClass> _interfaces = new(StringComparer.Ordinal);
        private readonly JsonObject _components = root["components"] as JsonObject ?? [];

        public DiagramDocument Build(string fallbackName, string source)
        {
            var info = root["info"] as JsonObject ?? [];
            var document = DiagramDocument.Create(Json.String(info["title"]) is { Length: > 0 } title ? title : fallbackName, DiagramType.Class);
            document.Model = _model;
            document.Description = Json.String(info["description"]);
            document.TaggedValues.Set(OpenApiTags.SourceVersion, source);

            var otherInfo = Json.Diff(info, new JsonObject { ["title"] = info["title"]?.DeepClone(), ["description"] = info["description"]?.DeepClone() });
            if (otherInfo.Count > 0)
            {
                document.TaggedValues.Set(OpenApiTags.Info, Json.Compact(otherInfo));
            }

            foreach (var (key, value) in root)
            {
                if (key is not ("openapi" or "info" or "paths" or "components") && value is not null)
                {
                    document.TaggedValues.Set(OpenApiTags.RootPrefix + key, Json.Compact(value));
                }
            }

            var otherComponents = Json.Diff(_components, new JsonObject { ["schemas"] = _components["schemas"]?.DeepClone() });
            if (otherComponents.Count > 0)
            {
                document.TaggedValues.Set(OpenApiTags.Components, Json.Compact(otherComponents));
            }

            var schemas = _components["schemas"] as JsonObject ?? [];
            foreach (var (name, _) in schemas)
            {
                var cls = new UmlClass { Name = UniqueName(ClassName(name)) };
                if (cls.Name != name)
                {
                    cls.TaggedValues.Set(OpenApiTags.SchemaName, name);
                }

                _bySchema[name] = cls;
            }

            // Endpoints first, so the «api» interfaces come first in the diagram.
            foreach (var (path, item) in root["paths"] as JsonObject ?? [])
            {
                if (item is JsonObject pathItem)
                {
                    AddEndpoints(path, pathItem);
                }
            }

            foreach (var (name, schema) in schemas)
            {
                FillClass(_bySchema[name], schema as JsonObject ?? []);
                _model.Classes.Add(_bySchema[name]);
            }

            MarkAbstractBases();
            return document;
        }

        #region Schemas

        private void FillClass(UmlClass cls, JsonObject schema)
        {
            if (schema["enum"] is JsonArray literals)
            {
                cls.Kind = ClassifierKind.Enumeration;
                cls.Attributes = literals.Select(l => new UmlAttribute
                {
                    Name = l is null ? "null" : Json.String(l) ?? l.ToJsonString(),
                    Visibility = UmlVisibility.Public,
                }).ToList();
                var canonical = new JsonObject { ["enum"] = literals.DeepClone() };
                if (Json.String(schema["type"]) == "string")
                {
                    canonical["type"] = "string";
                }

                SetSchemaTag(cls, Json.Diff(schema, canonical));
                return;
            }

            var properties = new List<(string Name, JsonObject Schema)>();
            var required = new HashSet<string>(Strings(schema["required"]), StringComparer.Ordinal);
            var consumed = new JsonObject();
            AddProperties(schema, consumed);

            if (schema["allOf"] is JsonArray allOf)
            {
                var inline = allOf.OfType<JsonObject>().Where(i => !i.ContainsKey("$ref")).ToList();
                var simple = allOf.All(i => i is JsonObject) && inline.Count <= 1 &&
                             inline.All(i => i.All(p => p.Key is "type" or "properties" or "required") &&
                                             Json.String(i["type"]) is null or "object");
                if (simple)
                {
                    foreach (var reference in allOf.OfType<JsonObject>().Select(i => Json.String(i["$ref"])).OfType<string>())
                    {
                        if (SchemaClass(reference) is { } baseClass)
                        {
                            _model.Relationships.Add(new UmlRelationship { Kind = RelationshipKind.Inheritance, SourceId = cls.Id, TargetId = baseClass.Id });
                        }
                    }

                    foreach (var part in inline)
                    {
                        required.UnionWith(Strings(part["required"]));
                        AddProperties(part, null);
                    }

                    consumed["allOf"] = allOf.DeepClone();
                }
                else
                {
                    warnings.Add($"{cls.Name}: its allOf combines more than base schemas and one set of properties; it is kept as written, without attributes for those parts.");
                }
            }

            foreach (var (name, propertySchema) in properties)
            {
                cls.Attributes.Add(Attribute(name, propertySchema, required.Contains(name)));
                AddReference(cls, propertySchema, required.Contains(name));
            }

            if (properties.Count > 0 || consumed.ContainsKey("allOf"))
            {
                if (schema.ContainsKey("required"))
                {
                    consumed["required"] = schema["required"]!.DeepClone();
                }

                if (Json.String(schema["type"]) == "object" && properties.Count > 0 && !consumed.ContainsKey("allOf"))
                {
                    consumed["type"] = "object";
                }
            }

            SetSchemaTag(cls, Json.Diff(schema, consumed));

            void AddProperties(JsonObject owner, JsonObject? markConsumed)
            {
                if (owner["properties"] is not JsonObject props)
                {
                    return;
                }

                foreach (var (name, value) in props)
                {
                    properties.Add((name, value as JsonObject ?? []));
                }

                if (markConsumed is not null)
                {
                    markConsumed["properties"] = props.DeepClone();
                }
            }
        }

        private UmlAttribute Attribute(string name, JsonObject schema, bool required)
        {
            var type = SchemaTypes.TypeOf(schema, ClassNameOfSchema);
            var attribute = new UmlAttribute { Name = name, Type = type, Visibility = UmlVisibility.Public };
            var kept = Json.Diff(schema, SchemaTypes.Canonical(type, SchemaRefOfClass));
            if (kept["default"] is { } value)
            {
                attribute.DefaultValue = value.ToJsonString();
                kept.Remove("default");
            }

            if (kept.Count > 0)
            {
                attribute.TaggedValues.Set(OpenApiTags.Property, Json.Compact(kept));
            }

            if (required)
            {
                attribute.TaggedValues.Set(OpenApiTags.Required, "true");
            }

            return attribute;
        }

        /// <summary>A directed association to the class a property refers to (directly or as array items), once per pair.</summary>
        private void AddReference(UmlClass owner, JsonObject schema, bool required)
        {
            var many = Json.String(schema["type"]) == "array";
            var target = Json.String((many ? schema["items"] : schema)?["$ref"]) is { } reference ? SchemaClass(reference) : null;
            // Enumerations are types of attributes, not associated classes.
            if (target is null || IsEnumSchema(target) || target == owner ||
                _model.Relationships.Any(r => r.SourceId == owner.Id && r.TargetId == target.Id))
            {
                return;
            }

            _model.Relationships.Add(new UmlRelationship
            {
                Kind = RelationshipKind.DirectedAssociation,
                SourceId = owner.Id,
                TargetId = target.Id,
                TargetMultiplicity = many ? "*" : required ? "1" : "0..1",
            });
        }

        /// <summary>Classes that are only used as allOf bases are abstract.</summary>
        private void MarkAbstractBases()
        {
            var text = root.ToJsonString();
            foreach (var cls in _bySchema.Values.Where(c => c.Kind == ClassifierKind.Class))
            {
                var isBase = _model.Relationships.Any(r => r.Kind == RelationshipKind.Inheritance && r.TargetId == cls.Id);
                var inheritances = _model.Relationships.Count(r => r.Kind == RelationshipKind.Inheritance && r.TargetId == cls.Id);
                var references = CountOccurrences(text, $"\"{SchemaTypes.SchemaRefPrefix}{_bySchema.First(p => p.Value == cls).Key}\"");
                if (isBase && references == inheritances)
                {
                    cls.Kind = ClassifierKind.AbstractClass;
                }
            }

            static int CountOccurrences(string text, string value)
            {
                var count = 0;
                for (var i = text.IndexOf(value, StringComparison.Ordinal); i >= 0; i = text.IndexOf(value, i + value.Length, StringComparison.Ordinal))
                {
                    count++;
                }

                return count;
            }
        }

        private static void SetSchemaTag(UmlClass cls, JsonObject kept)
        {
            if (kept.Count > 0)
            {
                cls.TaggedValues.Set(OpenApiTags.Schema, Json.Compact(kept));
            }
        }

        #endregion

        #region Endpoints

        private void AddEndpoints(string path, JsonObject item)
        {
            foreach (var key in item.Select(p => p.Key).Where(k => k is not "parameters" && !Methods.Contains(k)))
            {
                warnings.Add($"{path}: the path's own \"{key}\" is not kept; only its operations and parameters are.");
            }

            var shared = (item["parameters"] as JsonArray)?.OfType<JsonObject>().ToList() ?? [];
            foreach (var method in Methods)
            {
                if (item[method] is JsonObject operation)
                {
                    AddEndpoint(path, method, operation, shared);
                }
            }
        }

        private void AddEndpoint(string path, string method, JsonObject operation, List<JsonObject> shared)
        {
            var tag = (operation["tags"] as JsonArray)?.Select(Json.String).FirstOrDefault(t => !string.IsNullOrWhiteSpace(t));
            var api = Interface(tag, path);
            var kept = (JsonObject)operation.DeepClone();

            // An operationId that cannot be an operation name (e.g. get-pets) stays in the tagged value, which the export
            // prefers; so do tags beyond the one the interface stands for.
            var name = Json.String(operation["operationId"]);
            if (name is { Length: > 0 } && name.All(c => char.IsLetterOrDigit(c) || c == '_') && api.Operations.All(o => o.Name != name))
            {
                kept.Remove("operationId");
            }
            else
            {
                name = UniqueOperationName(api, GeneratedName(method, path));
            }

            if (operation["tags"] is JsonArray { Count: 1 })
            {
                kept.Remove("tags");
            }

            // Parameters shared by the path become the operation's own, unless the operation overrides them.
            var own = (operation["parameters"] as JsonArray)?.OfType<JsonObject>().ToList() ?? [];
            var parameters = shared.Where(s => !own.Any(o => SameParameter(Resolve(o), Resolve(s)))).Concat(own).ToList();
            if (parameters.Count > 0)
            {
                kept["parameters"] = new JsonArray([.. parameters.Select(p => p.DeepClone())]);
            }

            var result = new UmlOperation
            {
                Name = name,
                Visibility = UmlVisibility.Public,
                HttpMethod = method.ToUpperInvariant(),
                Path = path,
            };

            foreach (var entry in parameters)
            {
                var parameter = Resolve(entry);
                if (Json.String(parameter["in"]) is "path" or "query" && Json.String(parameter["name"]) is { } parameterName)
                {
                    result.Parameters.Add(new UmlParameter { Name = parameterName, Type = SchemaTypes.TypeOf(parameter["schema"], ClassNameOfSchema) });
                }
            }

            if (Resolve(operation["requestBody"] as JsonObject) is { } body && OpenApiContent.JsonContentSchema(body) is { } bodySchema)
            {
                var bodyName = result.Parameters.Any(p => p.Name == "body") ? "requestBody" : "body";
                result.Parameters.Add(new UmlParameter { Name = bodyName, Type = SchemaTypes.TypeOf(bodySchema, ClassNameOfSchema) });
            }

            if (OpenApiContent.SuccessCode(operation["responses"] as JsonObject) is { } code &&
                OpenApiContent.JsonContentSchema(Resolve((JsonObject)operation["responses"]![code]!)) is { } returned)
            {
                result.ReturnType = SchemaTypes.TypeOf(returned, ClassNameOfSchema);
            }

            if (kept.Count > 0)
            {
                result.TaggedValues.Set(OpenApiTags.Operation, Json.Compact(kept));
            }

            api.Operations.Add(result);
        }

        /// <summary>The «api» interface of a tag, or of the first path segment for operations without a tag.</summary>
        private UmlClass Interface(string? tag, string path)
        {
            var segment = path.Split('/', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault(s => !s.StartsWith('{')) ?? "default";
            var key = tag ?? "\0" + segment;
            if (_interfaces.TryGetValue(key, out var existing))
            {
                return existing;
            }

            var api = new UmlClass
            {
                Kind = ClassifierKind.Interface,
                Stereotype = OpenApiTags.ApiStereotype,
                Name = UniqueName(Naming.Pascal(tag ?? segment) is { Length: > 0 } pascal ? pascal + "Api" : "DefaultApi"),
            };
            if (tag is null)
            {
                api.TaggedValues.Set(OpenApiTags.Untagged, "true");
            }
            else
            {
                api.TaggedValues.Set(OpenApiTags.Tag, tag);
            }

            _interfaces[key] = api;
            _model.Classes.Add(api);
            return api;
        }

        private static string GeneratedName(string method, string path)
        {
            var words = new List<string> { method };
            foreach (var segment in path.Split('/', StringSplitOptions.RemoveEmptyEntries))
            {
                words.Add(segment.StartsWith('{') ? "by " + segment.Trim('{', '}') : segment);
            }

            return Naming.Camel(string.Join(' ', words)) is { Length: > 0 } name ? name : method;
        }

        private static string UniqueOperationName(UmlClass api, string name)
        {
            var unique = name;
            for (var i = 2; api.Operations.Any(o => o.Name == unique); i++)
            {
                unique = name + i;
            }

            return unique;
        }

        private static bool SameParameter(JsonObject a, JsonObject b) =>
            Json.String(a["name"]) == Json.String(b["name"]) && Json.String(a["in"]) == Json.String(b["in"]);

        [return: System.Diagnostics.CodeAnalysis.NotNullIfNotNull(nameof(node))]
        private JsonObject? Resolve(JsonObject? node) => ComponentReferences.Resolve(node, _components);

        #endregion

        #region Names

        private bool IsEnumSchema(UmlClass cls) =>
            _components["schemas"]?[_bySchema.First(p => p.Value == cls).Key] is JsonObject schema && schema.ContainsKey("enum");

        private UmlClass? SchemaClass(string reference) =>
            reference.StartsWith(SchemaTypes.SchemaRefPrefix, StringComparison.Ordinal)
                ? _bySchema.GetValueOrDefault(reference[SchemaTypes.SchemaRefPrefix.Length..])
                : null;

        private string? ClassNameOfSchema(string schemaName) => _bySchema.GetValueOrDefault(schemaName)?.Name;

        private string? SchemaRefOfClass(string className) =>
            _bySchema.FirstOrDefault(p => p.Value.Name == className).Key is { } schemaName ? SchemaTypes.SchemaRefPrefix + schemaName : null;

        private string UniqueName(string name)
        {
            var taken = _bySchema.Values.Select(c => c.Name).Concat(_interfaces.Values.Select(c => c.Name)).ToHashSet(StringComparer.Ordinal);
            var unique = name;
            for (var i = 2; taken.Contains(unique); i++)
            {
                unique = name + i;
            }

            return unique;
        }

        /// <summary>A schema name as a class name that types can refer to: letters, digits, _ and . only.</summary>
        private static string ClassName(string schemaName)
        {
            var chars = schemaName.Select(c => char.IsLetterOrDigit(c) || c is '_' or '.' ? c : '_').ToArray();
            var name = new string(chars);
            return name.Length == 0 ? "Schema" : char.IsDigit(name[0]) ? "_" + name : name;
        }

        private static IEnumerable<string> Strings(JsonNode? node) =>
            node is JsonArray array ? array.Select(Json.String).OfType<string>() : [];

        #endregion
    }
}
