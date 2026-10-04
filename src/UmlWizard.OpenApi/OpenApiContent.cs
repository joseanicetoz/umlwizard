using System.Text.Json.Nodes;

namespace UmlWizard.OpenApi;

/// <summary>What the importer and the exporter both read from request bodies and responses.</summary>
internal static class OpenApiContent
{
    /// <summary>The schema of the JSON content (or the only content) of a request body or response.</summary>
    public static JsonObject? JsonContentSchema(JsonObject bodyOrResponse)
    {
        if (bodyOrResponse["content"] is not JsonObject content)
        {
            return null;
        }

        var media = content["application/json"] as JsonObject
                    ?? content.Where(p => p.Key.Contains("json", StringComparison.OrdinalIgnoreCase)).Select(p => p.Value).OfType<JsonObject>().FirstOrDefault()
                    ?? (content.Count == 1 ? content.First().Value as JsonObject : null);
        return media?["schema"] as JsonObject;
    }

    /// <summary>The key of the first 2xx response (the lowest code, or <c>2XX</c>); <c>null</c> when there is none.</summary>
    public static string? SuccessCode(JsonObject? responses) =>
        responses?.Where(p => p.Key.StartsWith('2') && p.Value is JsonObject).Select(p => p.Key).Order(StringComparer.Ordinal).FirstOrDefault();

    /// <summary>True when two UML types are written the same, ignoring spaces (<c>Map&lt;string,int&gt;</c>).</summary>
    public static bool SameType(string? a, string? b) =>
        string.Concat((a ?? "object").Where(c => !char.IsWhiteSpace(c))) == string.Concat((b ?? "object").Where(c => !char.IsWhiteSpace(c)));
}
