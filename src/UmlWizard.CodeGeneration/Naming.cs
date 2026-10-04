using System.Text;

namespace UmlWizard.CodeGeneration;

/// <summary>Turns names from the diagram into identifiers that follow a language's conventions.</summary>
public static class Naming
{
    /// <summary>
    /// The words of a name: "orderLines", "OrderLines", "order_lines" and "order lines" all give ["order", "lines"].
    /// Characters that cannot be part of an identifier separate words.
    /// </summary>
    public static IReadOnlyList<string> Words(string name)
    {
        var words = new List<string>();
        var current = new StringBuilder();
        for (var i = 0; i < name.Length; i++)
        {
            var c = name[i];
            if (!char.IsLetterOrDigit(c))
            {
                Flush();
                continue;
            }

            // A capital starts a word after a lower-case letter or digit ("orderLine"), and before a lower-case letter
            // after capitals ("HTTPServer" gives "HTTP", "Server").
            if (char.IsUpper(c) && current.Length > 0 &&
                (!char.IsUpper(name[i - 1]) || (i + 1 < name.Length && char.IsLower(name[i + 1]))))
            {
                Flush();
            }

            current.Append(c);
        }

        Flush();
        return words;

        void Flush()
        {
            if (current.Length > 0)
            {
                words.Add(current.ToString());
                current.Clear();
            }
        }
    }

    public static string Pascal(string name) => Join(Words(name), Capitalize, Capitalize);

    public static string Camel(string name) => Join(Words(name), w => w.ToLowerInvariant(), Capitalize);

    public static string Snake(string name) => string.Join('_', Words(name).Select(w => w.ToLowerInvariant()));

    public static string UpperSnake(string name) => string.Join('_', Words(name).Select(w => w.ToUpperInvariant()));

    /// <summary>
    /// The name with the characters an identifier cannot contain removed, and "_" in front when it starts with a
    /// digit; "Unnamed" when nothing is left. The case is kept: "Order Line" gives "OrderLine".
    /// </summary>
    public static string Identifier(string name)
    {
        var id = new string(name.Where(c => char.IsLetterOrDigit(c) || c == '_').ToArray());
        return id.Length == 0 ? "Unnamed" : char.IsDigit(id[0]) ? "_" + id : id;
    }

    /// <summary>An identifier made from one of the case functions; "value" when the name has no letters or digits.</summary>
    public static string Cased(string name, Func<string, string> casing)
    {
        var id = casing(name);
        return id.Length == 0 ? "value" : char.IsDigit(id[0]) ? "_" + id : id;
    }

    /// <summary>A simple English plural for collection fields: "item" → "items", "address" → "addresses", "entry" → "entries".</summary>
    public static string Plural(string word)
    {
        if (word.Length == 0)
        {
            return word;
        }

        if (word.EndsWith('s') || word.EndsWith('x') || word.EndsWith('z') || word.EndsWith("ch") || word.EndsWith("sh"))
        {
            return word + "es";
        }

        return word.Length > 1 && word.EndsWith('y') && !"aeiou".Contains(word[^2]) ? word[..^1] + "ies" : word + "s";
    }

    /// <summary>The parts of a namespace such as "Shop.Domain" or "shop::domain", without empty parts.</summary>
    public static IReadOnlyList<string> NamespaceParts(string? ns) =>
        string.IsNullOrWhiteSpace(ns)
            ? []
            : ns.Split(["::", ".", "/", "\\"], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(Identifier)
                .ToList();

    private static string Capitalize(string word) =>
        word.Length == 0 ? word : char.ToUpperInvariant(word[0]) + word[1..].ToLowerInvariant();

    private static string Join(IReadOnlyList<string> words, Func<string, string> first, Func<string, string> rest)
    {
        var sb = new StringBuilder();
        for (var i = 0; i < words.Count; i++)
        {
            sb.Append(i == 0 ? first(words[i]) : rest(words[i]));
        }

        return sb.ToString();
    }
}
