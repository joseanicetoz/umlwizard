using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Nodes;

namespace UmlWizard.OpenApi;

/// <summary>Follows local <c>$ref</c>s to components, e.g. <c>#/components/parameters/limit</c>.</summary>
internal static class ComponentReferences
{
    private const string Prefix = "#/components/";

    /// <summary>
    /// The object <paramref name="node"/> refers to, following chains of references; the node itself when it is not a
    /// reference, or when the reference leads outside the document or nowhere.
    /// </summary>
    [return: NotNullIfNotNull(nameof(node))]
    public static JsonObject? Resolve(JsonObject? node, JsonObject components)
    {
        for (var depth = 0; depth < 16 && Json.String(node?["$ref"]) is { } reference; depth++)
        {
            if (!reference.StartsWith(Prefix, StringComparison.Ordinal) ||
                reference[Prefix.Length..].Split('/') is not [var kind, var name] ||
                components[kind]?[Unescape(name)] is not JsonObject target)
            {
                break;
            }

            node = target;
        }

        return node;

        // JSON pointer escapes: ~1 is /, ~0 is ~.
        static string Unescape(string token) => token.Replace("~1", "/", StringComparison.Ordinal).Replace("~0", "~", StringComparison.Ordinal);
    }
}
