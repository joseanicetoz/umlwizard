namespace UmlWizard.Core.Model;

/// <summary>
/// A UML tagged value: a named property of an element that the notation has no place for, e.g. <c>required = true</c>.
/// Imports keep what they cannot map in tagged values so an export can write it back (e.g. <c>openapi.schema</c>).
/// </summary>
public sealed class TaggedValue
{
    public string Name { get; set; } = string.Empty;

    public string Value { get; set; } = string.Empty;
}

/// <summary>Reads and writes tagged values by name.</summary>
public static class TaggedValues
{
    /// <summary>The value of the first tagged value named <paramref name="name"/>, or <c>null</c>.</summary>
    public static string? Get(this IEnumerable<TaggedValue> values, string name) =>
        values.FirstOrDefault(v => v.Name == name)?.Value;

    /// <summary>Sets the tagged value named <paramref name="name"/>; <c>null</c> removes it.</summary>
    public static void Set(this List<TaggedValue> values, string name, string? value)
    {
        var index = values.FindIndex(v => v.Name == name);
        if (value is null)
        {
            if (index >= 0)
            {
                values.RemoveAt(index);
            }
        }
        else if (index >= 0)
        {
            values[index].Value = value;
        }
        else
        {
            values.Add(new TaggedValue { Name = name, Value = value });
        }
    }

    /// <summary>A deep copy, so an edited element does not share its tagged values with the original.</summary>
    public static List<TaggedValue> Copy(this IEnumerable<TaggedValue> values) =>
        values.Select(v => new TaggedValue { Name = v.Name, Value = v.Value }).ToList();
}
