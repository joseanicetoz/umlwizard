using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using UmlWizard.Core.Model;

namespace UmlWizard.Core.Serialization;

public static class UmlJson
{
    public static JsonSerializerOptions Options { get; } = CreateOptions();

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            AllowOutOfOrderMetadataProperties = true,
            TypeInfoResolver = new DefaultJsonTypeInfoResolver { Modifiers = { OmitEmptyTaggedValues } },
        };
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        return options;
    }

    /// <summary>Leaves out empty tagged value lists, so files of elements without any stay as they were.</summary>
    private static void OmitEmptyTaggedValues(JsonTypeInfo typeInfo)
    {
        foreach (var property in typeInfo.Properties.Where(p => p.PropertyType == typeof(List<TaggedValue>)))
        {
            property.ShouldSerialize = (_, value) => value is List<TaggedValue> { Count: > 0 };
        }
    }

    /// <summary>Writes through a temporary file so a failed save never truncates the existing file.</summary>
    internal static void WriteAllTextAtomic(string path, string content)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var tempPath = path + ".tmp";
        File.WriteAllText(tempPath, content);
        File.Move(tempPath, path, overwrite: true);
    }
}
