using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using YamlDotNet.Core;
using YamlDotNet.Core.Events;
using YamlDotNet.RepresentationModel;

namespace UmlWizard.OpenApi;

/// <summary>
/// Reads JSON or YAML into a <see cref="JsonNode"/> tree and writes such a tree as JSON or YAML, so the importer and
/// exporter work on one representation whichever format the file has.
/// </summary>
public static class YamlJson
{
    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };

    /// <summary>Parses a JSON or YAML document (JSON when it starts with <c>{</c> or <c>[</c>).</summary>
    /// <exception cref="InvalidDataException">The text is neither valid JSON nor valid YAML.</exception>
    public static JsonNode? Parse(string text)
    {
        var start = text.TrimStart('﻿', ' ', '\t', '\r', '\n');
        try
        {
            if (start.StartsWith('{') || start.StartsWith('['))
            {
                return JsonNode.Parse(start, documentOptions: new JsonDocumentOptions
                {
                    AllowTrailingCommas = true,
                    CommentHandling = JsonCommentHandling.Skip,
                });
            }

            var stream = new YamlStream();
            stream.Load(new StringReader(text));
            return stream.Documents.Count == 0 ? null : ToJson(stream.Documents[0].RootNode);
        }
        catch (Exception ex) when (ex is JsonException or YamlException)
        {
            throw new InvalidDataException(ex.Message, ex);
        }
    }

    public static string ToJson(JsonNode node) => node.ToJsonString(Indented);

    /// <summary>Writes block-style YAML; strings that would read back as another type are quoted.</summary>
    public static string ToYaml(JsonNode node)
    {
        var writer = new StringWriter(CultureInfo.InvariantCulture) { NewLine = "\n" };
        var emitter = new Emitter(writer, EmitterSettings.Default.WithBestWidth(int.MaxValue).WithIndentedSequences());
        emitter.Emit(new StreamStart());
        emitter.Emit(new DocumentStart());
        Emit(emitter, node);
        emitter.Emit(new DocumentEnd(isImplicit: true));
        emitter.Emit(new StreamEnd());
        return writer.ToString();
    }

    private static JsonNode? ToJson(YamlNode node)
    {
        switch (node)
        {
            case YamlMappingNode mapping:
                var obj = new JsonObject();
                foreach (var (key, value) in mapping.Children)
                {
                    obj[key is YamlScalarNode scalarKey ? scalarKey.Value ?? string.Empty : key.ToString()] = ToJson(value);
                }

                return obj;

            case YamlSequenceNode sequence:
                var array = new JsonArray();
                foreach (var item in sequence.Children)
                {
                    array.Add(ToJson(item));
                }

                return array;

            case YamlScalarNode scalar:
                return Scalar(scalar);

            default:
                return null;
        }
    }

    /// <summary>A scalar as the YAML 1.2 core schema reads it: plain <c>true</c>, <c>42</c>, <c>null</c> are not strings.</summary>
    private static JsonNode? Scalar(YamlScalarNode scalar)
    {
        var text = scalar.Value ?? string.Empty;
        var tag = scalar.Tag.IsEmpty ? null : scalar.Tag.Value;
        if (tag == "tag:yaml.org,2002:str" || (tag is null && scalar.Style != ScalarStyle.Plain))
        {
            return JsonValue.Create(text);
        }

        return ResolvePlain(text) switch
        {
            PlainKind.Null => null,
            PlainKind.True => JsonValue.Create(true),
            PlainKind.False => JsonValue.Create(false),
            PlainKind.Integer when long.TryParse(Digits(text), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var l) => JsonValue.Create(l),
            PlainKind.Integer when text.StartsWith("0x", StringComparison.Ordinal) => JsonValue.Create(Convert.ToInt64(text[2..], 16)),
            PlainKind.Integer when text.StartsWith("0o", StringComparison.Ordinal) => JsonValue.Create(Convert.ToInt64(text[2..], 8)),
            PlainKind.Integer or PlainKind.Float when decimal.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) => JsonValue.Create(d),
            PlainKind.Float when double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var f) => JsonValue.Create(f),
            _ => JsonValue.Create(text),
        };

        static string Digits(string text) => text.StartsWith('+') ? text[1..] : text;
    }

    private enum PlainKind
    {
        String,
        Null,
        True,
        False,
        Integer,
        Float,
    }

    /// <summary>What a plain (unquoted) scalar means in the YAML 1.2 core schema.</summary>
    private static PlainKind ResolvePlain(string text)
    {
        switch (text)
        {
            case "" or "~" or "null" or "Null" or "NULL":
                return PlainKind.Null;
            case "true" or "True" or "TRUE":
                return PlainKind.True;
            case "false" or "False" or "FALSE":
                return PlainKind.False;
        }

        if (IsInteger(text))
        {
            return PlainKind.Integer;
        }

        return IsFloat(text) ? PlainKind.Float : PlainKind.String;

        static bool IsInteger(string s)
        {
            if (s.StartsWith("0x", StringComparison.Ordinal))
            {
                return s.Length > 2 && s[2..].All(Uri.IsHexDigit);
            }

            if (s.StartsWith("0o", StringComparison.Ordinal))
            {
                return s.Length > 2 && s[2..].All(c => c is >= '0' and <= '7');
            }

            var digits = s.TrimStart('+', '-');
            return digits.Length > 0 && digits.Length >= s.Length - 1 && digits.All(char.IsAsciiDigit);
        }

        // [-+]?(\.[0-9]+|[0-9]+(\.[0-9]*)?)([eE][-+]?[0-9]+)?, and the infinities / NaN (not representable in JSON).
        static bool IsFloat(string s)
        {
            var i = s.Length > 0 && s[0] is '+' or '-' ? 1 : 0;
            var mantissa = 0;
            var sawDot = false;
            for (; i < s.Length && (char.IsAsciiDigit(s[i]) || (s[i] == '.' && !sawDot)); i++)
            {
                sawDot |= s[i] == '.';
                mantissa += char.IsAsciiDigit(s[i]) ? 1 : 0;
            }

            if (mantissa == 0)
            {
                return false;
            }

            if (i < s.Length && s[i] is 'e' or 'E')
            {
                i++;
                i += i < s.Length && s[i] is '+' or '-' ? 1 : 0;
                var exponent = i;
                while (i < s.Length && char.IsAsciiDigit(s[i]))
                {
                    i++;
                }

                if (i == exponent)
                {
                    return false;
                }
            }

            return i == s.Length;
        }
    }

    private static void Emit(IEmitter emitter, JsonNode? node)
    {
        switch (node)
        {
            case JsonObject obj:
                emitter.Emit(new MappingStart(AnchorName.Empty, TagName.Empty, true, obj.Count == 0 ? MappingStyle.Flow : MappingStyle.Block));
                foreach (var (key, value) in obj)
                {
                    EmitString(emitter, key);
                    Emit(emitter, value);
                }

                emitter.Emit(new MappingEnd());
                break;

            case JsonArray array:
                emitter.Emit(new SequenceStart(AnchorName.Empty, TagName.Empty, true, array.Count == 0 ? SequenceStyle.Flow : SequenceStyle.Block));
                foreach (var item in array)
                {
                    Emit(emitter, item);
                }

                emitter.Emit(new SequenceEnd());
                break;

            case JsonValue value when value.GetValueKind() == JsonValueKind.String:
                EmitString(emitter, value.GetValue<string>());
                break;

            case JsonValue value:
                // Numbers and booleans are written as JSON writes them, which is also their plain YAML form.
                emitter.Emit(new Scalar(AnchorName.Empty, TagName.Empty, value.ToJsonString(), ScalarStyle.Plain, true, false));
                break;

            default:
                emitter.Emit(new Scalar(AnchorName.Empty, TagName.Empty, "null", ScalarStyle.Plain, true, false));
                break;
        }
    }

    /// <summary>A string, quoted when its plain form would read back as null, a boolean or a number.</summary>
    private static void EmitString(IEmitter emitter, string text)
    {
        var plainIsString = ResolvePlain(text) == PlainKind.String;
        emitter.Emit(new Scalar(AnchorName.Empty, TagName.Empty, text, plainIsString ? ScalarStyle.Any : ScalarStyle.DoubleQuoted, plainIsString, true));
    }
}
