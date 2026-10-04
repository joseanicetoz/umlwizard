using System.Text;
using System.Text.RegularExpressions;

namespace UmlWizard.PlantUml;

/// <summary>What an end of a relation line is written as.</summary>
internal enum EndKind
{
    /// <summary>A name or alias: <c>Order</c>.</summary>
    Name,

    /// <summary>A display name in quotes: <c>"Order Line"</c>.</summary>
    Quoted,

    /// <summary>A component: <c>[Web Frontend]</c>.</summary>
    Component,

    /// <summary>A use case: <c>(Place order)</c>.</summary>
    UseCase,

    /// <summary>An actor: <c>:Customer:</c>.</summary>
    Actor,

    /// <summary>An interface drawn as a circle: <c>() "IOrders"</c>.</summary>
    Interface,

    /// <summary>The start or end of a state machine: <c>[*]</c>.</summary>
    StartEnd,

    /// <summary>The start or end of an old-style activity diagram: <c>(*)</c>.</summary>
    ActivityStartEnd,

    /// <summary>A synchronization bar of an old-style activity diagram: <c>===B1===</c>.</summary>
    Bar,
}

/// <summary>An end of a relation line: what it is written as, and its text without brackets or quotes.</summary>
internal readonly record struct End(EndKind Kind, string Text);

/// <summary>
/// An arrow: whether its line is dashed (<c>..</c>) and the heads at each end, e.g. <c>&lt;|</c> and <c></c> for
/// <c>&lt;|--</c>, or <c></c> and <c>&gt;</c> for <c>-up-&gt;</c>.
/// </summary>
internal readonly record struct Arrow(bool Dashed, string LeftHead, string RightHead);

/// <summary>A relation line: <c>Order "1" *-- "1..*" OrderLine : lines &gt;</c>.</summary>
internal sealed record Relation(End Left, string? LeftMultiplicity, Arrow Arrow, string? RightMultiplicity, End Right, string? Label);

/// <summary>What the PlantUML readers and writer share: lines, names, quoting and relation lines.</summary>
internal static partial class PlantUmlText
{
    /// <summary>
    /// The diagrams of a PlantUML text: the lines between each <c>@startuml</c> and <c>@enduml</c>, with the name after
    /// <c>@startuml</c>, if any. A text without <c>@startuml</c> is one diagram.
    /// </summary>
    public static List<(string? Name, List<string> Lines)> Diagrams(string text)
    {
        var diagrams = new List<(string?, List<string>)>();
        var lines = Lines(text);
        if (!lines.Any(l => l.StartsWith("@startuml", StringComparison.OrdinalIgnoreCase)))
        {
            diagrams.Add((null, lines));
            return diagrams;
        }

        List<string>? current = null;
        string? name = null;
        foreach (var line in lines)
        {
            if (line.StartsWith("@startuml", StringComparison.OrdinalIgnoreCase))
            {
                current = [];
                var rest = line["@startuml".Length..].Trim();
                name = rest.Length > 0 && !rest.StartsWith('(') ? Unquote(rest) : null;
            }
            else if (line.StartsWith("@enduml", StringComparison.OrdinalIgnoreCase))
            {
                if (current is not null)
                {
                    diagrams.Add((name, current));
                }

                current = null;
            }
            else
            {
                current?.Add(line);
            }
        }

        if (current is { Count: > 0 })
        {
            diagrams.Add((name, current));
        }

        return diagrams;
    }

    /// <summary>The lines of a text, trimmed, without comments (<c>' …</c> and <c>/' … '/</c>) and blank lines.</summary>
    public static List<string> Lines(string text)
    {
        var result = new List<string>();
        var inComment = false;
        foreach (var raw in text.Replace("\r\n", "\n").Split('\n'))
        {
            var line = raw.Trim();
            if (inComment)
            {
                var end = line.IndexOf("'/", StringComparison.Ordinal);
                if (end < 0)
                {
                    continue;
                }

                inComment = false;
                line = line[(end + 2)..].Trim();
            }

            // Block comments that start (and maybe end) on this line.
            while (line.IndexOf("/'", StringComparison.Ordinal) is var start && start >= 0)
            {
                var end = line.IndexOf("'/", start + 2, StringComparison.Ordinal);
                if (end < 0)
                {
                    line = line[..start].Trim();
                    inComment = true;
                    break;
                }

                line = (line[..start] + line[(end + 2)..]).Trim();
            }

            if (line.Length > 0 && !line.StartsWith('\''))
            {
                result.Add(line);
            }
        }

        return result;
    }

    /// <summary>A name as PlantUML writes it in quotes: line breaks as <c>\n</c>; double quotes, which cannot be escaped, as single ones.</summary>
    public static string Quote(string text) => "\"" + text.Replace("\"", "'").Replace("\r\n", "\n").Replace("\n", "\\n") + "\"";

    /// <summary>A text on one line (a label, a message): line breaks as <c>\n</c>.</summary>
    public static string OneLine(string text) => text.Replace("\r\n", "\n").Replace("\n", "\\n");

    /// <summary>A text as written in PlantUML, without its quotes and with <c>\n</c> as line breaks.</summary>
    public static string Unquote(string text)
    {
        text = text.Trim();
        if (text.Length >= 2 && text[0] == '"' && text[^1] == '"')
        {
            text = text[1..^1];
        }

        return text.Replace("\\n", "\n").Trim();
    }

    /// <summary>A name PlantUML accepts without quotes.</summary>
    public static bool IsIdentifier(string text) => Identifier().IsMatch(text);

    /// <summary>
    /// Reads a relation line, or returns null when the line is not one: two ends, an arrow between them with optional
    /// multiplicities in quotes, and an optional label after a colon.
    /// </summary>
    public static Relation? ParseRelation(string line)
    {
        var i = 0;
        if (ReadEnd(line, ref i) is not { } left)
        {
            return null;
        }

        SkipSpaces(line, ref i);
        var leftMultiplicity = ReadQuoted(line, ref i);
        SkipSpaces(line, ref i);
        if (ReadArrow(line, ref i) is not { } arrow)
        {
            return null;
        }

        SkipSpaces(line, ref i);
        var rightMultiplicity = ReadQuoted(line, ref i);
        SkipSpaces(line, ref i);
        if (ReadEnd(line, ref i) is not { } right)
        {
            return null;
        }

        SkipSpaces(line, ref i);
        string? label = null;
        if (i < line.Length)
        {
            if (line[i] != ':')
            {
                return null;
            }

            label = line[(i + 1)..].Trim();
        }

        return new Relation(left, leftMultiplicity, arrow, rightMultiplicity, right, label);
    }

    /// <summary>Reads the end of a relation at <paramref name="i"/>.</summary>
    private static End? ReadEnd(string line, ref int i)
    {
        SkipSpaces(line, ref i);
        if (i >= line.Length)
        {
            return null;
        }

        var rest = line[i..];
        (EndKind Kind, Regex Pattern)[] patterns =
        [
            (EndKind.StartEnd, StartEndPattern()),
            (EndKind.ActivityStartEnd, ActivityStartEndPattern()),
            (EndKind.Bar, BarPattern()),
            (EndKind.Interface, InterfacePattern()),
            (EndKind.Component, ComponentPattern()),
            (EndKind.UseCase, UseCasePattern()),
            (EndKind.Actor, ActorPattern()),
            (EndKind.Quoted, QuotedPattern()),
            (EndKind.Name, NamePattern()),
        ];
        foreach (var (kind, pattern) in patterns)
        {
            var match = pattern.Match(rest);
            if (match.Success)
            {
                i += match.Length;
                var text = match.Groups["text"].Success ? match.Groups["text"].Value : match.Value;
                return new End(kind, kind == EndKind.Quoted || kind == EndKind.Interface ? Unquote(text) : text.Replace("\\n", "\n").Trim());
            }
        }

        return null;
    }

    private static string? ReadQuoted(string line, ref int i)
    {
        if (i >= line.Length || line[i] != '"')
        {
            return null;
        }

        var end = line.IndexOf('"', i + 1);
        if (end < 0)
        {
            return null;
        }

        var text = line[(i + 1)..end];
        i = end + 1;
        return text;
    }

    /// <summary>Reads an arrow: heads, a line of dashes or dots, and optional styling and direction in it.</summary>
    private static Arrow? ReadArrow(string line, ref int i)
    {
        var match = ArrowPattern().Match(line, i);
        if (!match.Success || match.Index != i || match.Value.All(c => c is not ('-' or '.' or '=')))
        {
            return null;
        }

        i += match.Length;
        var body = match.Groups["line"].Value;
        return new Arrow(body.Contains('.'), match.Groups["left"].Value, match.Groups["right"].Value);
    }

    private static void SkipSpaces(string line, ref int i)
    {
        while (i < line.Length && char.IsWhiteSpace(line[i]))
        {
            i++;
        }
    }

    [GeneratedRegex(@"^[A-Za-z_][A-Za-z0-9_]*$")]
    private static partial Regex Identifier();

    [GeneratedRegex(@"^\[\*\]")]
    private static partial Regex StartEndPattern();

    [GeneratedRegex(@"^\(\*(?:top)?\)")]
    private static partial Regex ActivityStartEndPattern();

    [GeneratedRegex(@"^={2,}\s*(?<text>[^=]+?)\s*={2,}")]
    private static partial Regex BarPattern();

    [GeneratedRegex(@"^\(\)\s*(?<text>""[^""]*""|[A-Za-z_][A-Za-z0-9_.]*)")]
    private static partial Regex InterfacePattern();

    [GeneratedRegex(@"^\[(?<text>[^\]\*][^\]]*)\]")]
    private static partial Regex ComponentPattern();

    [GeneratedRegex(@"^\((?<text>[^)*][^)]*)\)")]
    private static partial Regex UseCasePattern();

    [GeneratedRegex(@"^:(?<text>[^:]+):")]
    private static partial Regex ActorPattern();

    [GeneratedRegex(@"^(?<text>""[^""]*"")")]
    private static partial Regex QuotedPattern();

    [GeneratedRegex(@"^[A-Za-z0-9_$\u0080-￿]+(?:\.[A-Za-z0-9_$\u0080-￿]+)*")]
    private static partial Regex NamePattern();

    /// <summary>
    /// An arrow: a head (<c>&lt;|</c>, <c>&lt;</c>, <c>*</c>, <c>o</c>, …), a line of <c>-</c>, <c>.</c> or <c>=</c> in which
    /// styling (<c>[#red]</c>) and a direction (<c>up</c>, <c>left</c>, <c>l</c>, …) may sit, and a head.
    /// </summary>
    [GeneratedRegex(@"(?<left><\||<<|<|\*|o(?=[-.])|#|x(?=[-.])|\}|\+|\^|0\)|\(0|\()?(?<line>[-.=]+(?:\[[^\]]*\])?(?:left|right|up|down|le|ri|up|do|l|r|u|d)?(?:\[[^\]]*\])?[-.=]*)(?<right>\|>|>>|>|\*|(?<=[-.])o|#|(?<=[-.])x|\{|\+|\^|\(0|0\)|\))?")]
    private static partial Regex ArrowPattern();
}

/// <summary>
/// Gives each element of a diagram a PlantUML alias made from its name (<c>Order</c>, <c>Order_Line</c>), unique in
/// the diagram, so that relation lines can refer to it.
/// </summary>
internal sealed class Aliases
{
    private readonly Dictionary<string, string> _byId = [];
    private readonly HashSet<string> _taken = new(StringComparer.OrdinalIgnoreCase);

    public string this[string id] => _byId[id];

    public bool Contains(string id) => _byId.ContainsKey(id);

    /// <summary>The alias of an element, made on first use from its name (or <paramref name="fallback"/>).</summary>
    public string Get(string id, string? name, string fallback = "E")
    {
        if (_byId.TryGetValue(id, out var alias))
        {
            return alias;
        }

        var text = new StringBuilder();
        foreach (var c in name ?? string.Empty)
        {
            text.Append(char.IsAsciiLetterOrDigit(c) || c == '_' ? c : char.IsWhiteSpace(c) || c is '-' or '.' ? '_' : '\0');
        }

        var baseAlias = text.ToString().Replace("\0", string.Empty).Trim('_');
        if (baseAlias.Length == 0)
        {
            baseAlias = fallback;
        }

        if (char.IsDigit(baseAlias[0]))
        {
            baseAlias = "_" + baseAlias;
        }

        alias = baseAlias;
        for (var i = 2; !_taken.Add(alias); i++)
        {
            alias = baseAlias + i;
        }

        _byId[id] = alias;
        return alias;
    }
}
