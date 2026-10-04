using System.Text.Json;
using System.Text.Json.Nodes;

namespace UmlWizard.OpenApi;

/// <summary>
/// Brings a Swagger 2.0 or OpenAPI 3.1 description into the OpenAPI 3.0 shape, in place, so the importer reads one
/// form and the export (which writes 3.0) is valid. What 3.0 cannot say is reported.
/// </summary>
internal static class OpenApiNormalizer
{
    public const string ExportVersion = "3.0.3";

    private static readonly string[] Methods = ["get", "put", "post", "delete", "options", "head", "patch", "trace"];

    /// <summary>Normalizes <paramref name="root"/> and returns the version it had, e.g. <c>2.0</c> or <c>3.1.0</c>.</summary>
    /// <exception cref="InvalidDataException">It is not an OpenAPI or Swagger description.</exception>
    public static string Normalize(JsonObject root, IList<string> warnings)
    {
        if (Json.String(root["swagger"]) is { } swagger)
        {
            if (!swagger.StartsWith("2.", StringComparison.Ordinal))
            {
                throw new InvalidDataException($"Swagger {swagger} is not supported; only Swagger 2.0 and OpenAPI 3.x are.");
            }

            FromSwagger2(root, warnings);
            return swagger;
        }

        if (Json.String(root["openapi"]) is not { } version)
        {
            throw new InvalidDataException("The file is not an OpenAPI description: it has no \"openapi\" (or \"swagger\") version.");
        }

        if (version.StartsWith("3.1", StringComparison.Ordinal))
        {
            From31(root, warnings);
        }
        else if (!version.StartsWith("3.0", StringComparison.Ordinal))
        {
            warnings.Add($"OpenAPI {version} is read as OpenAPI 3.0.");
        }

        return version;
    }

    #region OpenAPI 3.1

    private static void From31(JsonObject root, IList<string> warnings)
    {
        var mixed = false;
        Walk(root);
        if (mixed)
        {
            warnings.Add("Some schemas allow several types (e.g. type: [string, integer]); OpenAPI 3.0 has no such schemas, so they are kept as written.");
        }

        void Walk(JsonNode? node)
        {
            switch (node)
            {
                case JsonObject obj:
                    ConvertSchemaKeywords(obj);
                    foreach (var child in obj.Select(p => p.Value).ToList())
                    {
                        Walk(child);
                    }

                    break;
                case JsonArray array:
                    foreach (var item in array.ToList())
                    {
                        Walk(item);
                    }

                    break;
            }
        }

        // 3.1 schemas are JSON Schema 2020-12: rewrite what 3.0 says differently.
        void ConvertSchemaKeywords(JsonObject obj)
        {
            if (obj["type"] is JsonArray types)
            {
                var named = types.Select(Json.String).Where(t => t is not null && t != "null").ToList();
                if (named.Count == 1)
                {
                    obj["type"] = named[0];
                    if (named.Count < types.Count)
                    {
                        obj["nullable"] = true;
                    }
                }
                else
                {
                    mixed = true;
                }
            }

            if (obj.ContainsKey("const") && !obj.ContainsKey("enum"))
            {
                obj["enum"] = new JsonArray(Json.Copy(obj["const"]));
                obj.Remove("const");
            }

            if (obj["examples"] is JsonArray { Count: > 0 } examples && !obj.ContainsKey("example") && obj.ContainsKey("type"))
            {
                obj["example"] = Json.Copy(examples[0]);
                obj.Remove("examples");
            }

            foreach (var (bound, exclusive) in new[] { ("minimum", "exclusiveMinimum"), ("maximum", "exclusiveMaximum") })
            {
                if (obj[exclusive] is JsonValue value && value.GetValueKind() == JsonValueKind.Number)
                {
                    obj[bound] = Json.Copy(value);
                    obj[exclusive] = true;
                }
            }
        }
    }

    #endregion

    #region Swagger 2.0

    private static void FromSwagger2(JsonObject root, IList<string> warnings)
    {
        var consumes = Strings(root["consumes"]) ?? ["application/json"];
        var produces = Strings(root["produces"]) ?? ["application/json"];

        var result = new JsonObject { ["openapi"] = ExportVersion };
        foreach (var key in new[] { "info" })
        {
            if (root.ContainsKey(key))
            {
                result[key] = Json.Copy(root[key]);
            }
        }

        if (Servers(root) is { } servers)
        {
            result["servers"] = servers;
        }

        foreach (var (key, value) in root)
        {
            if (key is "tags" or "security" or "externalDocs" || key.StartsWith("x-", StringComparison.Ordinal))
            {
                result[key] = Json.Copy(value);
            }
        }

        var paths = new JsonObject();
        foreach (var (path, item) in root["paths"] as JsonObject ?? [])
        {
            paths[path] = item is JsonObject pathItem ? PathItem(pathItem, path) : Json.Copy(item);
        }

        result["paths"] = paths;

        var components = new JsonObject();
        if (root["definitions"] is JsonObject definitions)
        {
            components["schemas"] = Json.Copy(definitions);
        }

        if (root["parameters"] is JsonObject parameters)
        {
            var converted = new JsonObject();
            foreach (var (name, parameter) in parameters)
            {
                if (parameter is JsonObject p && Json.String(p["in"]) is not ("body" or "formData"))
                {
                    converted[name] = Parameter(p);
                }
                else
                {
                    warnings.Add($"The shared body parameter \"{name}\" is used in place by the operations that refer to it.");
                }
            }

            if (converted.Count > 0)
            {
                components["parameters"] = converted;
            }
        }

        if (root["responses"] is JsonObject responses)
        {
            components["responses"] = Responses(responses, produces);
        }

        if (root["securityDefinitions"] is JsonObject security)
        {
            components["securitySchemes"] = SecuritySchemes(security, warnings);
        }

        if (components.Count > 0)
        {
            result["components"] = components;
        }

        RewriteReferences(result);
        ConvertSchemas(result);

        root.Clear();
        foreach (var (key, value) in result.ToList())
        {
            result.Remove(key);
            root[key] = value;
        }

        JsonObject PathItem(JsonObject item, string path)
        {
            var converted = new JsonObject();
            foreach (var (key, value) in item)
            {
                if (key == "parameters" && value is JsonArray shared)
                {
                    // Body parameters shared by the operations of a path move into each operation's request body.
                    var plain = shared.OfType<JsonObject>().Where(p => !IsBodyLike(Resolve(p))).Select(Parameter).ToList();
                    if (plain.Count > 0)
                    {
                        converted[key] = new JsonArray([.. plain]);
                    }
                }
                else if (Methods.Contains(key) && value is JsonObject operation)
                {
                    var sharedBody = (item["parameters"] as JsonArray)?.OfType<JsonObject>().Where(p => IsBodyLike(Resolve(p))) ?? [];
                    converted[key] = Operation(operation, sharedBody, $"{key.ToUpperInvariant()} {path}");
                }
                else
                {
                    converted[key] = Json.Copy(value);
                }
            }

            return converted;
        }

        JsonObject Operation(JsonObject operation, IEnumerable<JsonObject> sharedBody, string name)
        {
            var opConsumes = Strings(operation["consumes"]) ?? consumes;
            var opProduces = Strings(operation["produces"]) ?? produces;
            var converted = new JsonObject();
            var parameters = new JsonArray();
            JsonObject? body = null;
            var form = new List<JsonObject>();

            foreach (var parameter in sharedBody.Concat((operation["parameters"] as JsonArray)?.OfType<JsonObject>() ?? []))
            {
                var resolved = Resolve(parameter);
                switch (Json.String(resolved["in"]))
                {
                    case "body":
                        body = resolved;
                        break;
                    case "formData":
                        form.Add(resolved);
                        break;
                    default:
                        parameters.Add(parameter.ContainsKey("$ref") ? Json.Copy(parameter) : Parameter(parameter));
                        break;
                }
            }

            foreach (var (key, value) in operation)
            {
                switch (key)
                {
                    case "consumes" or "produces":
                        break;
                    case "schemes":
                        warnings.Add($"{name}: its own schemes ({string.Join(", ", Strings(value) ?? [])}) are not kept; OpenAPI 3.0 lists servers instead.");
                        break;
                    case "parameters":
                        break;
                    case "responses" when value is JsonObject responses:
                        converted["responses"] = Responses(responses, opProduces);
                        break;
                    default:
                        converted[key] = Json.Copy(value);
                        break;
                }
            }

            if (parameters.Count > 0)
            {
                converted["parameters"] = parameters;
            }

            if (body is not null)
            {
                converted["requestBody"] = RequestBody(body, opConsumes);
            }
            else if (form.Count > 0)
            {
                converted["requestBody"] = FormBody(form, opConsumes);
            }

            return Json.Ordered(converted, "tags", "summary", "description", "operationId", "parameters", "requestBody", "responses");
        }

        JsonObject Resolve(JsonObject parameter) =>
            Json.String(parameter["$ref"]) is { } reference && reference.StartsWith("#/parameters/", StringComparison.Ordinal) &&
            root["parameters"]?[reference["#/parameters/".Length..]] is JsonObject target
                ? target
                : parameter;

        static bool IsBodyLike(JsonObject parameter) => Json.String(parameter["in"]) is "body" or "formData";
    }

    /// <summary>A non-body 2.0 parameter: its type keywords move into a schema.</summary>
    private static JsonObject Parameter(JsonObject parameter)
    {
        var converted = new JsonObject();
        var schema = new JsonObject();
        foreach (var (key, value) in parameter)
        {
            if (key is "type" or "format" or "items" or "default" or "enum" or "maximum" or "exclusiveMaximum" or "minimum"
                or "exclusiveMinimum" or "maxLength" or "minLength" or "pattern" or "maxItems" or "minItems" or "uniqueItems" or "multipleOf")
            {
                schema[key] = Json.Copy(value);
            }
            else if (key == "collectionFormat")
            {
                if (Json.String(value) == "multi")
                {
                    converted["explode"] = true;
                }
                else if (Json.String(value) != "csv")
                {
                    converted["style"] = Json.String(value) switch
                    {
                        "ssv" => "spaceDelimited",
                        "pipes" => "pipeDelimited",
                        _ => "form",
                    };
                    converted["explode"] = false;
                }
                else
                {
                    converted["explode"] = false;
                }
            }
            else
            {
                converted[key] = Json.Copy(value);
            }
        }

        if (schema.Count > 0)
        {
            converted["schema"] = schema;
        }

        return converted;
    }

    private static JsonObject RequestBody(JsonObject body, IReadOnlyList<string> consumes)
    {
        var content = new JsonObject();
        foreach (var type in consumes)
        {
            content[type] = new JsonObject { ["schema"] = Json.Copy(body["schema"]) ?? new JsonObject() };
        }

        var converted = new JsonObject();
        if (body.ContainsKey("description"))
        {
            converted["description"] = Json.Copy(body["description"]);
        }

        converted["content"] = content;
        if (Json.IsTrue(body["required"]))
        {
            converted["required"] = true;
        }

        return converted;
    }

    private static JsonObject FormBody(List<JsonObject> fields, IReadOnlyList<string> consumes)
    {
        var properties = new JsonObject();
        var required = new JsonArray();
        foreach (var field in fields)
        {
            var name = Json.String(field["name"]) ?? "field";
            var schema = Parameter(field)["schema"] is JsonObject converted ? (JsonObject)converted.DeepClone() : new JsonObject();
            if (Json.String(schema["type"]) == "file")
            {
                schema["type"] = "string";
                schema["format"] = "binary";
            }

            if (field.ContainsKey("description"))
            {
                schema["description"] = Json.Copy(field["description"]);
            }

            properties[name] = schema;
            if (Json.IsTrue(field["required"]))
            {
                required.Add(name);
            }
        }

        var schemaObject = new JsonObject { ["type"] = "object", ["properties"] = properties };
        if (required.Count > 0)
        {
            schemaObject["required"] = required;
        }

        var type = consumes.FirstOrDefault(c => c.StartsWith("multipart/", StringComparison.Ordinal))
                   ?? consumes.FirstOrDefault(c => c == "application/x-www-form-urlencoded")
                   ?? "application/x-www-form-urlencoded";
        return new JsonObject { ["content"] = new JsonObject { [type] = new JsonObject { ["schema"] = schemaObject } } };
    }

    private static JsonObject Responses(JsonObject responses, IReadOnlyList<string> produces)
    {
        var converted = new JsonObject();
        foreach (var (code, value) in responses)
        {
            if (value is not JsonObject response || response.ContainsKey("$ref"))
            {
                converted[code] = Json.Copy(value);
                continue;
            }

            var result = new JsonObject();
            foreach (var (key, field) in response)
            {
                switch (key)
                {
                    case "schema":
                        var content = new JsonObject();
                        foreach (var type in produces)
                        {
                            content[type] = new JsonObject { ["schema"] = Json.Copy(field) };
                        }

                        result["content"] = content;
                        break;
                    case "headers" when field is JsonObject headers:
                        var convertedHeaders = new JsonObject();
                        foreach (var (name, header) in headers)
                        {
                            convertedHeaders[name] = header is JsonObject h ? Parameter(h) : Json.Copy(header);
                        }

                        result["headers"] = convertedHeaders;
                        break;
                    case "examples":
                        break;
                    default:
                        result[key] = Json.Copy(field);
                        break;
                }
            }

            converted[code] = result;
        }

        return converted;
    }

    private static JsonObject SecuritySchemes(JsonObject definitions, IList<string> warnings)
    {
        var converted = new JsonObject();
        foreach (var (name, value) in definitions)
        {
            if (value is not JsonObject definition)
            {
                continue;
            }

            switch (Json.String(definition["type"]))
            {
                case "basic":
                    converted[name] = new JsonObject { ["type"] = "http", ["scheme"] = "basic" };
                    break;
                case "oauth2":
                    var flow = Json.String(definition["flow"]) switch
                    {
                        "implicit" => "implicit",
                        "password" => "password",
                        "application" => "clientCredentials",
                        _ => "authorizationCode",
                    };
                    var flowObject = new JsonObject();
                    foreach (var key in new[] { "authorizationUrl", "tokenUrl" })
                    {
                        if (definition.ContainsKey(key))
                        {
                            flowObject[key] = Json.Copy(definition[key]);
                        }
                    }

                    flowObject["scopes"] = Json.Copy(definition["scopes"]) ?? new JsonObject();
                    converted[name] = new JsonObject { ["type"] = "oauth2", ["flows"] = new JsonObject { [flow] = flowObject } };
                    break;
                default:
                    converted[name] = Json.Copy(definition);
                    break;
            }

            if (definition.ContainsKey("description") && converted[name] is JsonObject scheme && !scheme.ContainsKey("description"))
            {
                scheme["description"] = Json.Copy(definition["description"]);
            }
        }

        return converted;
    }

    /// <summary>The server of 2.0's host, basePath and schemes; <c>null</c> when it has none.</summary>
    private static JsonArray? Servers(JsonObject root)
    {
        var host = Json.String(root["host"]);
        var basePath = Json.String(root["basePath"]);
        if (host is null && basePath is null)
        {
            return null;
        }

        var schemes = Strings(root["schemes"]) ?? ["https"];
        var urls = host is null
            ? [basePath!]
            : schemes.Select(s => $"{s}://{host}{basePath}").ToList();
        return new JsonArray([.. urls.Select(u => (JsonNode)new JsonObject { ["url"] = u })]);
    }

    private static void RewriteReferences(JsonNode? node)
    {
        switch (node)
        {
            case JsonObject obj:
                if (Json.String(obj["$ref"]) is { } reference)
                {
                    obj["$ref"] = reference
                        .Replace("#/definitions/", SchemaTypes.SchemaRefPrefix, StringComparison.Ordinal)
                        .Replace("#/parameters/", "#/components/parameters/", StringComparison.Ordinal)
                        .Replace("#/responses/", "#/components/responses/", StringComparison.Ordinal);
                }

                foreach (var child in obj.Select(p => p.Value).ToList())
                {
                    RewriteReferences(child);
                }

                break;
            case JsonArray array:
                foreach (var item in array)
                {
                    RewriteReferences(item);
                }

                break;
        }
    }

    /// <summary>2.0 schema keywords that 3.0 spells differently: file, x-nullable and a discriminator given by name.</summary>
    private static void ConvertSchemas(JsonNode? node)
    {
        switch (node)
        {
            case JsonObject obj:
                if (Json.String(obj["type"]) == "file")
                {
                    obj["type"] = "string";
                    obj["format"] = "binary";
                }

                if (obj.ContainsKey("x-nullable"))
                {
                    obj["nullable"] = Json.Copy(obj["x-nullable"]);
                    obj.Remove("x-nullable");
                }

                if (Json.String(obj["discriminator"]) is { } property)
                {
                    obj["discriminator"] = new JsonObject { ["propertyName"] = property };
                }

                foreach (var child in obj.Select(p => p.Value).ToList())
                {
                    ConvertSchemas(child);
                }

                break;
            case JsonArray array:
                foreach (var item in array)
                {
                    ConvertSchemas(item);
                }

                break;
        }
    }

    private static List<string>? Strings(JsonNode? node) =>
        node is JsonArray array ? array.Select(Json.String).OfType<string>().ToList() : null;

    #endregion
}
