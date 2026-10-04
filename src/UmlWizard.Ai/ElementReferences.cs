namespace UmlWizard.Ai;

/// <summary>
/// The names the model refers to elements by: an element's name, with <c>#2</c>, <c>#3</c>… for later elements of the
/// same name, and for elements without a name their kind and number, e.g. <c>decision#1</c>. Lookups take the reference
/// exactly, else ignoring case, else the plain name when only one element has it.
/// </summary>
public sealed class ElementReferences
{
    private readonly List<(string Id, string Reference, string? Name)> _entries = [];

    /// <param name="elements">The elements in order, with their name (<c>null</c> or empty for none) and kind word.</param>
    public ElementReferences(IEnumerable<(string Id, string? Name, string Kind)> elements)
    {
        var count = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var (id, name, kind) in elements)
        {
            var key = string.IsNullOrWhiteSpace(name) ? kind + "#" : name.Trim();
            var n = count[key] = count.GetValueOrDefault(key) + 1;
            var reference = string.IsNullOrWhiteSpace(name) ? $"{kind}#{n}" : n == 1 ? key : $"{key}#{n}";
            _entries.Add((id, reference, string.IsNullOrWhiteSpace(name) ? null : name.Trim()));
        }
    }

    public string this[string id] => _entries.First(e => e.Id == id).Reference;

    public IEnumerable<string> All => _entries.Select(e => e.Reference);

    /// <summary>The id of the element a reference means; <c>null</c> with a reason when there is none or more than one.</summary>
    public string? Find(string reference, out string? problem)
    {
        problem = null;
        var text = reference.Trim();
        foreach (var match in new Func<(string Id, string Reference, string? Name), bool>[]
                 {
                     e => e.Reference == text,
                     e => string.Equals(e.Reference, text, StringComparison.OrdinalIgnoreCase),
                     e => string.Equals(e.Name, text, StringComparison.OrdinalIgnoreCase),
                 })
        {
            var found = _entries.Where(match).ToList();
            if (found.Count == 1)
            {
                return found[0].Id;
            }

            if (found.Count > 1)
            {
                problem = $"More than one element is named {text}: use {string.Join(" or ", found.Select(f => f.Reference))}.";
                return null;
            }
        }

        problem = $"There is nothing named {text}. The references are: {(_entries.Count == 0 ? "none" : string.Join(", ", All))}.";
        return null;
    }
}
