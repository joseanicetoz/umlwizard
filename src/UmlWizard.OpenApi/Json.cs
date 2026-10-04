using System.Text.Json;
using System.Text.Json.Nodes;

namespace UmlWizard.OpenApi;

/// <summary>
/// Helpers for the JSON trees of OpenAPI descriptions. <see cref="Diff"/> and <see cref="Merge"/> make round trips
/// lossless: an import keeps in a tagged value only what the export does not write by itself (the difference to the
/// <em>canonical</em> form of the UML element), and the export merges that back.
/// </summary>
internal static class Json
{
    public static string? String(JsonNode? node) =>
        node is JsonValue value && value.GetValueKind() == JsonValueKind.String ? value.GetValue<string>() : null;

    public static bool IsTrue(JsonNode? node) => node is JsonValue value && value.GetValueKind() == JsonValueKind.True;

    /// <summary>A copy detached from its parent, so it can be added to another tree.</summary>
    public static JsonNode? Copy(JsonNode? node) => node?.DeepClone();

    /// <summary>What <paramref name="original"/> has that <paramref name="canonical"/> does not (objects compared recursively).</summary>
    public static JsonObject Diff(JsonObject original, JsonObject canonical)
    {
        var result = new JsonObject();
        foreach (var (key, value) in original)
        {
            if (!canonical.TryGetPropertyValue(key, out var produced))
            {
                result[key] = Copy(value);
            }
            else if (value is JsonObject originalChild && produced is JsonObject producedChild)
            {
                var child = Diff(originalChild, producedChild);
                if (child.Count > 0)
                {
                    result[key] = child;
                }
            }
            else if (!JsonNode.DeepEquals(value, produced))
            {
                result[key] = Copy(value);
            }
        }

        return result;
    }

    /// <summary>
    /// <paramref name="canonical"/>, then the keys of <paramref name="kept"/> it does not have (objects merged
    /// recursively). The canonical form wins on conflicts: it is what the diagram says now.
    /// </summary>
    public static JsonObject Merge(JsonObject canonical, JsonObject? kept)
    {
        var result = (JsonObject)canonical.DeepClone();
        foreach (var (key, value) in kept ?? [])
        {
            if (!result.TryGetPropertyValue(key, out var existing))
            {
                result[key] = Copy(value);
            }
            else if (existing is JsonObject existingChild && value is JsonObject keptChild)
            {
                result[key] = Merge(existingChild, keptChild);
            }
        }

        return result;
    }

    /// <summary>The object in a tagged value; <c>null</c> when it is missing or not a JSON object.</summary>
    public static JsonObject? ParseObject(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            return JsonNode.Parse(json) as JsonObject;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Any JSON value in a tagged value; <c>null</c> when it is not valid JSON.</summary>
    public static JsonNode? ParseNode(string? json, out bool valid)
    {
        valid = false;
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            var node = JsonNode.Parse(json);
            valid = true;
            return node;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Compact JSON for a tagged value.</summary>
    public static string Compact(JsonNode node) => node.ToJsonString();

    /// <summary>A new object with <paramref name="first"/> keys in that order, then the others as they were.</summary>
    public static JsonObject Ordered(JsonObject obj, params string[] first)
    {
        var result = new JsonObject();
        foreach (var key in first.Where(obj.ContainsKey))
        {
            result[key] = Copy(obj[key]);
        }

        foreach (var (key, value) in obj.Where(p => !first.Contains(p.Key)))
        {
            result[key] = Copy(value);
        }

        return result;
    }
}
