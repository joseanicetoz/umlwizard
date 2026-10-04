using System.Text.Json.Nodes;
using UmlWizard.CodeGeneration;
using UmlWizard.Core.Model;

namespace UmlWizard.OpenApi;

public enum OpenApiFormat
{
    Yaml,
    Json,
}

/// <param name="Title">The API's title; the diagram name when <c>null</c>.</param>
/// <param name="Version">The API's version; the imported one, or <c>1.0.0</c>, when <c>null</c>.</param>
public sealed record OpenApiExportOptions(string? Title = null, string? Version = null, OpenApiFormat Format = OpenApiFormat.Yaml);

public sealed record OpenApiExportResult(string Text, IReadOnlyList<string> Warnings);

/// <summary>
/// Writes a class diagram as an OpenAPI 3.0 description. Classes and enumerations become schemas (attributes and
/// association ends are properties, base classes are <c>allOf</c> references); the operations of «api» interfaces
/// that have an HTTP method and path become the endpoints, tagged by interface. What an import kept in tagged values
/// is written back, so a diagram made by <see cref="OpenApiImporter"/> gives its description back.
/// </summary>
public static class OpenApiExporter
{
    public static OpenApiExportResult Export(DiagramDocument document, OpenApiExportOptions options)
    {
        var warnings = new List<string>();
        var root = Build(document, options, warnings);
        var text = options.Format == OpenApiFormat.Json ? YamlJson.ToJson(root) : YamlJson.ToYaml(root);
        return new OpenApiExportResult(text, warnings);
    }

    /// <summary>The title an export uses by default: the diagram's name.</summary>
    public static string DefaultTitle(DiagramDocument document) => document.Name;

    /// <summary>The version an export uses by default: the imported one, or <c>1.0.0</c>.</summary>
    public static string DefaultVersion(DiagramDocument document) =>
        Json.String(Json.ParseObject(document.TaggedValues.Get(OpenApiTags.Info))?["version"]) ?? "1.0.0";

    /// <summary>True for the «api» interfaces whose operations are endpoints.</summary>
    public static bool IsApi(UmlClass cls) =>
        cls.Kind == ClassifierKind.Interface &&
        (cls.Stereotype?.Trim() == OpenApiTags.ApiStereotype || cls.Operations.Any(o => !string.IsNullOrWhiteSpace(o.HttpMethod)));

    internal static JsonObject Build(DiagramDocument document, OpenApiExportOptions options, List<string> warnings) =>
        new Exporter(document, warnings).Build(options);

    private sealed class Exporter
    {
        private static readonly string[] RootKeys30 = ["servers", "security", "tags", "externalDocs"];

        private readonly DiagramDocument _document;
        private readonly List<string> _warnings;
        private readonly ClassDiagramModel _model;
        private readonly CodeModel _code;
        private readonly Dictionary<TypeModel, string> _schemaNames = [];
        private readonly JsonObject _components;
        private readonly HashSet<string> _unknownTypes = new(StringComparer.Ordinal);

        public Exporter(DiagramDocument document, List<string> warnings)
        {
            _document = document;
            _warnings = warnings;
            _model = document.Model as ClassDiagramModel
                ?? throw new InvalidOperationException("Only class diagrams can be exported as OpenAPI.");

            // The code model resolves inheritance and association ends; its own warnings are about generated code.
            _code = CodeModel.Build(_model, multipleInheritance: true, []);
            _components = Json.ParseObject(document.TaggedValues.Get(OpenApiTags.Components)) ?? [];

            var taken = new HashSet<string>(StringComparer.Ordinal);
            foreach (var type in _code.Types.Where(t => !IsApi(t.Class)))
            {
                var name = type.Class.TaggedValues.Get(OpenApiTags.SchemaName) ?? (IsSchemaName(type.Class.Name) ? type.Class.Name : type.Name);
                var unique = name;
                for (var i = 2; !taken.Add(unique); i++)
                {
                    unique = name + i;
                }

                _schemaNames[type] = unique;
            }
        }

        public JsonObject Build(OpenApiExportOptions options)
        {
            var root = new JsonObject { ["openapi"] = OpenApiNormalizer.ExportVersion, ["info"] = Info(options) };

            var extra = _document.TaggedValues
                .Where(t => t.Name.StartsWith(OpenApiTags.RootPrefix, StringComparison.Ordinal))
                .Where(t => t.Name is not (OpenApiTags.Info or OpenApiTags.Components or OpenApiTags.SourceVersion));
            foreach (var tag in extra)
            {
                var key = tag.Name[OpenApiTags.RootPrefix.Length..];
                var value = Json.ParseNode(tag.Value, out var valid);
                if (!valid)
                {
                    _warnings.Add($"The diagram's tagged value {tag.Name} is not valid JSON; it is not written.");
                }
                else if (RootKeys30.Contains(key) || key.StartsWith("x-", StringComparison.Ordinal))
                {
                    root[key] = value;
                }
                else
                {
                    _warnings.Add($"\"{key}\" (from the tagged value {tag.Name}) is not part of OpenAPI 3.0; it is not written.");
                }
            }

            root["paths"] = Paths();

            var components = new JsonObject();
            var schemas = new JsonObject();
            foreach (var type in _code.Types.Where(t => !IsApi(t.Class)))
            {
                schemas[_schemaNames[type]] = type.IsEnum ? EnumSchema(type) : ClassSchema(type);
            }

            if (schemas.Count > 0)
            {
                components["schemas"] = schemas;
            }

            foreach (var (key, value) in _components)
            {
                if (key != "schemas")
                {
                    components[key] = Json.Copy(value);
                }
            }

            if (components.Count > 0)
            {
                root["components"] = components;
            }

            foreach (var type in _unknownTypes.Order(StringComparer.Ordinal))
            {
                _warnings.Add($"{type} is neither a class of the diagram nor a known type; it is written as a schema that allows any value.");
            }

            return root;
        }

        private JsonObject Info(OpenApiExportOptions options)
        {
            var kept = Json.ParseObject(_document.TaggedValues.Get(OpenApiTags.Info)) ?? [];
            var info = new JsonObject { ["title"] = options.Title ?? DefaultTitle(_document) };
            if (!string.IsNullOrWhiteSpace(_document.Description))
            {
                info["description"] = _document.Description;
            }

            info["version"] = options.Version ?? DefaultVersion(_document);
            return Json.Merge(info, kept);
        }

        #region Schemas

        private JsonObject EnumSchema(TypeModel type)
        {
            var kept = Json.ParseObject(type.Class.TaggedValues.Get(OpenApiTags.Schema)) ?? [];
            var valueType = Json.String(kept["type"]) ?? "string";
            var literals = new JsonArray();
            foreach (var literal in type.Class.Attributes)
            {
                literals.Add(valueType is "integer" or "number" && decimal.TryParse(literal.Name, System.Globalization.CultureInfo.InvariantCulture, out var number)
                    ? JsonValue.Create(number)
                    : valueType == "boolean" && bool.TryParse(literal.Name, out var flag) ? JsonValue.Create(flag)
                    : literal.Name == "null" && kept.ContainsKey("nullable") ? null
                    : JsonValue.Create(literal.Name));
            }

            return Json.Merge(new JsonObject { ["type"] = valueType, ["enum"] = literals }, kept);
        }

        private JsonObject ClassSchema(TypeModel type)
        {
            var kept = Json.ParseObject(type.Class.TaggedValues.Get(OpenApiTags.Schema));
            var properties = new JsonObject();
            var required = new JsonArray();

            foreach (var attribute in type.Attributes.Where(a => !a.IsImplicit && !a.Attribute.IsStatic).Select(a => a.Attribute))
            {
                if (properties.ContainsKey(attribute.Name))
                {
                    _warnings.Add($"{type.Name}: two attributes are named {attribute.Name}; the second is not written.");
                    continue;
                }

                var property = Json.Merge(Schema(attribute.Type), Json.ParseObject(attribute.TaggedValues.Get(OpenApiTags.Property)));
                if (attribute.DefaultValue is { } defaultValue)
                {
                    property["default"] = Json.ParseNode(defaultValue, out var valid) is var value && valid ? value : JsonValue.Create(defaultValue);
                }

                properties[attribute.Name] = property;
                if (attribute.TaggedValues.Get(OpenApiTags.Required) == "true")
                {
                    required.Add(attribute.Name);
                }
            }

            foreach (var end in type.Ends.Where(e => !properties.ContainsKey(e.Name)))
            {
                var reference = new JsonObject { ["$ref"] = SchemaRef(end.Target) };
                properties[end.Name] = end.IsMany ? new JsonObject { ["type"] = "array", ["items"] = reference } : reference;
                if (!end.IsMany && !end.IsOptional)
                {
                    required.Add(end.Name);
                }
            }

            var own = new JsonObject();
            if (properties.Count > 0)
            {
                own["type"] = "object";
                if (required.Count > 0)
                {
                    own["required"] = required;
                }

                own["properties"] = properties;
            }

            var bases = type.BaseClasses.Concat(type.Interfaces).Where(b => _schemaNames.ContainsKey(b)).ToList();
            if (bases.Count == 0)
            {
                return Json.Merge(own, kept);
            }

            var allOf = new JsonArray([.. bases.Select(b => (JsonNode)new JsonObject { ["$ref"] = SchemaRef(b) })]);
            if (own.Count > 0)
            {
                allOf.Add(own);
            }

            return Json.Merge(new JsonObject { ["allOf"] = allOf }, kept);
        }

        /// <summary>The schema of a UML type (see <see cref="SchemaTypes.Canonical"/>).</summary>
        private JsonObject Schema(string? type) =>
            SchemaTypes.Canonical(type, name => _code.Find(name) is { } t && _schemaNames.ContainsKey(t) ? SchemaRef(t) : null, n => _unknownTypes.Add(n));

        private string SchemaRef(TypeModel type) => SchemaTypes.SchemaRefPrefix + _schemaNames[type];

        private static bool IsSchemaName(string name) =>
            name.Length > 0 && name.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '.' or '-');

        #endregion

        #region Endpoints

        private JsonObject Paths()
        {
            var paths = new JsonObject();
            foreach (var api in _model.Classes.Where(IsApi))
            {
                foreach (var operation in api.Operations)
                {
                    var method = operation.HttpMethod?.Trim().ToLowerInvariant();
                    var path = operation.Path?.Trim();
                    if (string.IsNullOrEmpty(method) || string.IsNullOrEmpty(path))
                    {
                        _warnings.Add($"{api.Name}.{operation.Name} has no HTTP method and path; it is not written as an endpoint.");
                        continue;
                    }

                    if (!path.StartsWith('/'))
                    {
                        path = "/" + path;
                    }

                    if (paths[path] is not JsonObject item)
                    {
                        paths[path] = item = [];
                    }

                    if (item.ContainsKey(method))
                    {
                        _warnings.Add($"{method.ToUpperInvariant()} {path} is defined twice; {api.Name}.{operation.Name} is not written.");
                        continue;
                    }

                    item[method] = Operation(api, operation, path);
                }
            }

            return paths;
        }

        private JsonObject Operation(UmlClass api, UmlOperation operation, string path)
        {
            var kept = Json.ParseObject(operation.TaggedValues.Get(OpenApiTags.Operation)) ?? [];
            var result = new JsonObject();

            // Tags and operationId the import could not express in the diagram are kept, and win.
            if (kept["tags"] is { } tags)
            {
                result["tags"] = Json.Copy(tags);
            }
            else if (api.TaggedValues.Get(OpenApiTags.Untagged) != "true")
            {
                result["tags"] = new JsonArray(api.TaggedValues.Get(OpenApiTags.Tag) ?? TagOf(api.Name));
            }

            foreach (var key in new[] { "summary", "description" }.Where(kept.ContainsKey))
            {
                result[key] = Json.Copy(kept[key]);
            }

            result["operationId"] = Json.Copy(kept["operationId"]) ?? operation.Name;

            var body = BodyParameter(operation);
            var parameters = Parameters(operation, path, body, kept["parameters"] as JsonArray);
            if (parameters.Count > 0)
            {
                result["parameters"] = parameters;
            }

            if (body is not null)
            {
                result["requestBody"] = RequestBody(body, kept["requestBody"] as JsonObject);
            }

            result["responses"] = Responses(operation.ReturnType, kept["responses"] as JsonObject);

            foreach (var (key, value) in kept)
            {
                if (!result.ContainsKey(key) && key is not ("parameters" or "requestBody" or "responses"))
                {
                    result[key] = Json.Copy(value);
                }
            }

            return result;
        }

        /// <summary>The parameter that is the request body: one named body or requestBody, else the first of a class type.</summary>
        private UmlParameter? BodyParameter(UmlOperation operation) =>
            operation.Parameters.FirstOrDefault(p => p.Name is "body" or "requestBody")
            ?? operation.Parameters.FirstOrDefault(p =>
                !(operation.Path ?? string.Empty).Contains('{' + p.Name + '}', StringComparison.Ordinal) &&
                TypeExpression.Parse(p.Type) is { } t && _code.Find(t.Name) is { IsEnum: false } target && _schemaNames.ContainsKey(target));

        /// <summary>
        /// The imported parameters that are still parameters of the operation (header and cookie parameters always, as
        /// the diagram does not show them), updated to their types, then the new ones.
        /// </summary>
        private JsonArray Parameters(UmlOperation operation, string path, UmlParameter? body, JsonArray? kept)
        {
            var result = new JsonArray();
            var written = new HashSet<string>(StringComparer.Ordinal);
            foreach (var entry in kept?.OfType<JsonObject>() ?? [])
            {
                var resolved = ComponentReferences.Resolve(entry, _components);
                var name = Json.String(resolved["name"]);
                var location = Json.String(resolved["in"]);
                if (location is not ("path" or "query"))
                {
                    result.Add(Json.Copy(entry));
                    continue;
                }

                var parameter = operation.Parameters.FirstOrDefault(p => p.Name == name && p != body);
                if (parameter is null || !written.Add(parameter.Name))
                {
                    continue;
                }

                var schema = resolved["schema"] as JsonObject;
                if (OpenApiContent.SameType(SchemaTypes.TypeOf(schema, ClassNameOfSchema), parameter.Type))
                {
                    result.Add(Json.Copy(entry));
                }
                else
                {
                    var updated = (JsonObject)resolved.DeepClone();
                    var previous = SchemaTypes.Canonical(SchemaTypes.TypeOf(schema, ClassNameOfSchema), SchemaRefOfClassName);
                    updated["schema"] = Json.Merge(Schema(parameter.Type), schema is null ? null : Json.Diff(schema, previous));
                    result.Add(updated);
                }
            }

            foreach (var parameter in operation.Parameters.Where(p => p != body && !written.Contains(p.Name)))
            {
                var inPath = path.Contains('{' + parameter.Name + '}', StringComparison.Ordinal);
                var added = new JsonObject { ["name"] = parameter.Name, ["in"] = inPath ? "path" : "query" };
                if (inPath)
                {
                    added["required"] = true;
                }

                added["schema"] = Schema(parameter.Type);
                result.Add(added);
                written.Add(parameter.Name);
            }

            // Every name in braces must be a path parameter.
            foreach (var name in PathParameters(path).Where(n => !written.Contains(n) && body?.Name != n))
            {
                if (!result.OfType<JsonObject>().Any(p => Json.String(ComponentReferences.Resolve(p, _components)["name"]) == name))
                {
                    _warnings.Add($"{operation.HttpMethod?.ToUpperInvariant()} {path}: {{{name}}} is not a parameter of {operation.Name}; it is written as a string path parameter.");
                    result.Add(new JsonObject { ["name"] = name, ["in"] = "path", ["required"] = true, ["schema"] = new JsonObject { ["type"] = "string" } });
                }
            }

            return result;
        }

        private JsonObject RequestBody(UmlParameter body, JsonObject? kept)
        {
            var resolved = kept is null ? null : ComponentReferences.Resolve(kept, _components);
            var schema = resolved is null ? null : OpenApiContent.JsonContentSchema(resolved);
            if (kept is not null && schema is not null && OpenApiContent.SameType(SchemaTypes.TypeOf(schema, ClassNameOfSchema), body.Type))
            {
                return (JsonObject)kept.DeepClone();
            }

            var result = resolved is null ? new JsonObject { ["required"] = true } : (JsonObject)resolved.DeepClone();
            if (result["content"] is not JsonObject content)
            {
                result["content"] = content = [];
            }

            var mediaType = content.Select(p => p.Key).FirstOrDefault(k => k.Contains("json", StringComparison.OrdinalIgnoreCase)) ?? "application/json";
            var media = content[mediaType] as JsonObject ?? [];
            media["schema"] = Schema(body.Type);
            content[mediaType] = Json.Copy(media);
            return Json.Ordered(result, "description", "content", "required");
        }

        private JsonObject Responses(string? returnType, JsonObject? kept)
        {
            var isVoid = string.IsNullOrWhiteSpace(returnType) || returnType.Trim() == "void";
            var responses = kept is null ? new JsonObject() : (JsonObject)kept.DeepClone();
            var success = OpenApiContent.SuccessCode(responses);

            if (success is null)
            {
                var added = isVoid
                    ? new JsonObject { ["description"] = "No Content" }
                    : new JsonObject
                    {
                        ["description"] = "OK",
                        ["content"] = new JsonObject { ["application/json"] = new JsonObject { ["schema"] = Schema(returnType) } },
                    };
                var ordered = new JsonObject { [isVoid ? "204" : "200"] = added };
                foreach (var (code, value) in responses)
                {
                    ordered[code] = Json.Copy(value);
                }

                return ordered;
            }

            var response = ComponentReferences.Resolve((JsonObject)responses[success]!, _components);
            var schema = OpenApiContent.JsonContentSchema(response);
            var current = schema is null ? null : SchemaTypes.TypeOf(schema, ClassNameOfSchema);
            if (isVoid ? schema is null : OpenApiContent.SameType(current, returnType))
            {
                return responses;
            }

            var updated = (JsonObject)response.DeepClone();
            if (isVoid)
            {
                updated.Remove("content");
            }
            else
            {
                if (updated["content"] is not JsonObject content)
                {
                    updated["content"] = content = [];
                }

                var mediaType = content.Select(p => p.Key).FirstOrDefault(k => k.Contains("json", StringComparison.OrdinalIgnoreCase)) ?? "application/json";
                var media = content[mediaType] as JsonObject ?? [];
                media["schema"] = Schema(returnType);
                content[mediaType] = Json.Copy(media);
            }

            responses[success] = updated;
            return responses;
        }

        private static IEnumerable<string> PathParameters(string path)
        {
            for (var start = path.IndexOf('{'); start >= 0; start = path.IndexOf('{', start + 1))
            {
                var end = path.IndexOf('}', start);
                if (end > start)
                {
                    yield return path[(start + 1)..end];
                }
            }
        }

        /// <summary>The tag of an interface made in the diagram: its name without a trailing "Api".</summary>
        private static string TagOf(string interfaceName) =>
            interfaceName.Length > 3 && interfaceName.EndsWith("Api", StringComparison.Ordinal) ? interfaceName[..^3] : interfaceName;

        private string? ClassNameOfSchema(string schemaName) =>
            _schemaNames.FirstOrDefault(p => p.Value == schemaName).Key?.Class.Name;

        private string? SchemaRefOfClassName(string className) =>
            _code.Find(className) is { } t && _schemaNames.ContainsKey(t) ? SchemaRef(t) : null;

        #endregion
    }
}
