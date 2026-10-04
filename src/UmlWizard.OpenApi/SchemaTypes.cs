using System.Text.Json.Nodes;
using UmlWizard.CodeGeneration;

namespace UmlWizard.OpenApi;

/// <summary>
/// Maps OpenAPI schemas to UML type names and back. <see cref="Canonical"/> is exactly what the export writes for a
/// type, and <see cref="TypeOf"/> picks the type whose canonical schema matches, so importing and exporting a schema
/// gives it back (with what the type does not say kept in a tagged value, see <see cref="Json.Diff"/>).
/// </summary>
internal static class SchemaTypes
{
    public const string SchemaRefPrefix = "#/components/schemas/";

    /// <summary>The UML type of a schema, e.g. <c>List&lt;Pet&gt;</c> for an array of <c>$ref: Pet</c>.</summary>
    /// <param name="className">The class name for a schema name; <c>null</c> for an unknown schema.</param>
    public static string TypeOf(JsonNode? node, Func<string, string?> className)
    {
        if (node is not JsonObject schema)
        {
            return "object";
        }

        if (Json.String(schema["$ref"]) is { } reference)
        {
            return reference.StartsWith(SchemaRefPrefix, StringComparison.Ordinal) &&
                   className(reference[SchemaRefPrefix.Length..]) is { } name
                ? name
                : "object";
        }

        var format = Json.String(schema["format"]);
        switch (Json.String(schema["type"]))
        {
            case "string":
                return format switch
                {
                    "date-time" => "DateTime",
                    "date" => "Date",
                    "uuid" => "Guid",
                    _ => "string",
                };
            case "integer":
                return format == "int64" ? "long" : "int";
            case "number":
                return format == "float" ? "float" : "double";
            case "boolean":
                return "bool";
            case "array":
                var item = TypeOf(schema["items"], className);
                return Json.IsTrue(schema["uniqueItems"]) ? $"Set<{item}>" : $"List<{item}>";
            case "object" or null when schema["additionalProperties"] is JsonObject values:
                return $"Map<string, {TypeOf(values, className)}>";
            case "object" or null when Json.IsTrue(schema["additionalProperties"]):
                return "Map<string, object>";
            default:
                return "object";
        }
    }

    /// <summary>
    /// The schema the export writes for a UML type; an empty schema (any value) for <c>object</c>, an empty type or
    /// one that is not known (reported through <paramref name="unknown"/>).
    /// </summary>
    /// <param name="schemaRef">The <c>$ref</c> for a class of the diagram by the name used in a type; <c>null</c> otherwise.</param>
    public static JsonObject Canonical(string? type, Func<string, string?> schemaRef, Action<string>? unknown = null)
    {
        var expression = TypeExpression.Parse(type);
        if (expression is null)
        {
            if (!string.IsNullOrWhiteSpace(type))
            {
                unknown?.Invoke(type.Trim());
            }

            return [];
        }

        return Canonical(expression, schemaRef, unknown);
    }

    private static JsonObject Canonical(TypeExpression type, Func<string, string?> schemaRef, Action<string>? unknown)
    {
        if (type.ArrayRank > 0)
        {
            return new JsonObject
            {
                ["type"] = "array",
                ["items"] = Canonical(type with { ArrayRank = type.ArrayRank - 1 }, schemaRef, unknown),
            };
        }

        if (schemaRef(type.Name) is { } reference)
        {
            return new JsonObject { ["$ref"] = reference };
        }

        JsonObject Argument(int index) =>
            index < type.Arguments.Count ? Canonical(type.Arguments[index], schemaRef, unknown) : [];

        switch (BuiltInTypes.Key(type.Name))
        {
            case "string" or "char":
                return new JsonObject { ["type"] = "string" };
            case "int" or "short" or "byte":
                return new JsonObject { ["type"] = "integer" };
            case "long":
                return new JsonObject { ["type"] = "integer", ["format"] = "int64" };
            case "double" or "decimal":
                return new JsonObject { ["type"] = "number" };
            case "float":
                return new JsonObject { ["type"] = "number", ["format"] = "float" };
            case "bool":
                return new JsonObject { ["type"] = "boolean" };
            case "datetime":
                return new JsonObject
                {
                    ["type"] = "string",
                    ["format"] = type.Name.Equals("date", StringComparison.OrdinalIgnoreCase) ? "date" : "date-time",
                };
            case "guid":
                return new JsonObject { ["type"] = "string", ["format"] = "uuid" };
            case "list":
                return new JsonObject { ["type"] = "array", ["items"] = Argument(0) };
            case "set":
                return new JsonObject { ["type"] = "array", ["uniqueItems"] = true, ["items"] = Argument(0) };
            case "map":
                return new JsonObject { ["type"] = "object", ["additionalProperties"] = Argument(type.Arguments.Count > 1 ? 1 : 0) };
            case "object" or "void":
                return [];
            default:
                unknown?.Invoke(type.Name);
                return [];
        }
    }
}
