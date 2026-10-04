using System.Text;
using UmlWizard.Core.Model;

namespace UmlWizard.Core.Notation;

/// <summary>
/// Formats and parses the textual UML notation used for display and in-line editing:
/// <c>+name: Type = default</c> for attributes and <c>+name(p: T): Return</c> for operations.
/// The keywords <c>static</c> and <c>abstract</c> may prefix the text while editing.
/// </summary>
public static class UmlNotation
{
    public static char ToSymbol(UmlVisibility visibility) => visibility switch
    {
        UmlVisibility.Public => '+',
        UmlVisibility.Private => '-',
        UmlVisibility.Protected => '#',
        _ => '~',
    };

    public static bool TryParseSymbol(char symbol, out UmlVisibility visibility)
    {
        switch (symbol)
        {
            case '+': visibility = UmlVisibility.Public; return true;
            case '-': visibility = UmlVisibility.Private; return true;
            case '#': visibility = UmlVisibility.Protected; return true;
            case '~': visibility = UmlVisibility.Package; return true;
            default: visibility = default; return false;
        }
    }

    public static string Format(UmlAttribute attribute, bool includeVisibility = true)
    {
        var sb = new StringBuilder();
        if (includeVisibility)
        {
            sb.Append(ToSymbol(attribute.Visibility));
        }

        sb.Append(attribute.Name);
        if (!string.IsNullOrWhiteSpace(attribute.Type))
        {
            sb.Append(": ").Append(attribute.Type);
        }

        if (!string.IsNullOrWhiteSpace(attribute.DefaultValue))
        {
            sb.Append(" = ").Append(attribute.DefaultValue);
        }

        return sb.ToString();
    }

    public static string FormatForEditing(UmlAttribute attribute, bool includeVisibility = true) =>
        (attribute.IsStatic ? "static " : string.Empty) + Format(attribute, includeVisibility);

    public static string Format(UmlOperation operation)
    {
        var sb = new StringBuilder();
        sb.Append(ToSymbol(operation.Visibility))
          .Append(operation.Name)
          .Append('(')
          .Append(FormatParameters(operation.Parameters))
          .Append(')');
        if (!string.IsNullOrWhiteSpace(operation.ReturnType))
        {
            sb.Append(": ").Append(operation.ReturnType);
        }

        return sb.ToString();
    }

    public static string FormatForEditing(UmlOperation operation) =>
        (operation.IsAbstract ? "abstract " : string.Empty) +
        (operation.IsStatic ? "static " : string.Empty) +
        Format(operation);

    public static string FormatParameters(IEnumerable<UmlParameter> parameters) =>
        string.Join(", ", parameters.Select(p => string.IsNullOrWhiteSpace(p.Type) ? p.Name : $"{p.Name}: {p.Type}"));

    public static string FormatLifeline(Lifeline lifeline) =>
        string.IsNullOrWhiteSpace(lifeline.Type) ? lifeline.Name : $"{lifeline.Name} : {lifeline.Type}";

    /// <summary>Parses an attribute; returns <c>null</c> when the text contains no name.</summary>
    public static UmlAttribute? ParseAttribute(string text, UmlVisibility defaultVisibility = UmlVisibility.Private)
    {
        var modifiers = ParseModifiers(text, defaultVisibility);
        var Remainder = modifiers.Remainder;
        string? defaultValue = null;
        var equals = IndexOfTopLevel(Remainder, '=');
        if (equals >= 0)
        {
            defaultValue = NullIfEmpty(Remainder[(equals + 1)..]);
            Remainder = Remainder[..equals];
        }

        var (name, type) = SplitNameAndType(Remainder);
        if (name.Length == 0)
        {
            return null;
        }

        return new UmlAttribute
        {
            Name = name,
            Type = type,
            DefaultValue = defaultValue,
            Visibility = modifiers.Visibility,
            IsStatic = modifiers.IsStatic,
        };
    }

    /// <summary>Parses an operation; returns <c>null</c> when the text contains no name.</summary>
    public static UmlOperation? ParseOperation(string text, UmlVisibility defaultVisibility = UmlVisibility.Public)
    {
        var modifiers = ParseModifiers(text, defaultVisibility);
        var Remainder = modifiers.Remainder;
        string name;
        var parameterText = string.Empty;
        string? returnType = null;

        var open = Remainder.IndexOf('(');
        if (open >= 0)
        {
            name = Remainder[..open].Trim();
            var close = FindClosingParenthesis(Remainder, open);
            if (close < 0)
            {
                parameterText = Remainder[(open + 1)..];
            }
            else
            {
                parameterText = Remainder[(open + 1)..close];
                var after = Remainder[(close + 1)..].Trim();
                if (after.StartsWith(':'))
                {
                    returnType = NullIfEmpty(after[1..]);
                }
            }
        }
        else
        {
            (name, returnType) = SplitNameAndType(Remainder);
        }

        if (name.Length == 0)
        {
            return null;
        }

        return new UmlOperation
        {
            Name = name,
            ReturnType = returnType,
            Visibility = modifiers.Visibility,
            IsStatic = modifiers.IsStatic,
            IsAbstract = modifiers.IsAbstract,
            Parameters = ParseParameters(parameterText),
        };
    }

    public static List<UmlParameter> ParseParameters(string text)
    {
        var parameters = new List<UmlParameter>();
        foreach (var part in SplitTopLevel(text, ','))
        {
            var (name, type) = SplitNameAndType(part);
            if (name.Length > 0)
            {
                parameters.Add(new UmlParameter { Name = name, Type = type });
            }
        }

        return parameters;
    }

    /// <summary>Parses <c>name : Type</c>, <c>name</c> or <c>: Type</c>.</summary>
    public static (string Name, string? Type) ParseLifeline(string text) => SplitNameAndType(text);

    private static (string Name, string? Type) SplitNameAndType(string text)
    {
        var colon = IndexOfTopLevel(text, ':');
        return colon < 0
            ? (text.Trim(), null)
            : (text[..colon].Trim(), NullIfEmpty(text[(colon + 1)..]));
    }

    private static (UmlVisibility Visibility, bool IsStatic, bool IsAbstract, string Remainder) ParseModifiers(
        string text, UmlVisibility defaultVisibility)
    {
        var Remainder = text.Trim();
        var visibility = defaultVisibility;
        bool isStatic = false, isAbstract = false;
        while (true)
        {
            if (Remainder.Length > 0 && TryParseSymbol(Remainder[0], out var parsed))
            {
                visibility = parsed;
                Remainder = Remainder[1..].TrimStart();
            }
            else if (StartsWithKeyword(Remainder, "static"))
            {
                isStatic = true;
                Remainder = Remainder["static".Length..].TrimStart();
            }
            else if (StartsWithKeyword(Remainder, "abstract"))
            {
                isAbstract = true;
                Remainder = Remainder["abstract".Length..].TrimStart();
            }
            else
            {
                return (visibility, isStatic, isAbstract, Remainder);
            }
        }
    }

    private static bool StartsWithKeyword(string text, string keyword) =>
        text.StartsWith(keyword, StringComparison.Ordinal) &&
        text.Length > keyword.Length &&
        char.IsWhiteSpace(text[keyword.Length]);

    /// <summary>Index of <paramref name="ch"/> outside of any &lt;&gt;, (), [] or {} nesting.</summary>
    private static int IndexOfTopLevel(string text, char ch)
    {
        var depth = 0;
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (depth == 0 && c == ch)
            {
                return i;
            }

            if (c is '<' or '(' or '[' or '{')
            {
                depth++;
            }
            else if (c is '>' or ')' or ']' or '}')
            {
                depth = Math.Max(0, depth - 1);
            }
        }

        return -1;
    }

    private static IEnumerable<string> SplitTopLevel(string text, char separator)
    {
        var Remainder = text;
        while (Remainder.Length > 0)
        {
            var index = IndexOfTopLevel(Remainder, separator);
            if (index < 0)
            {
                yield return Remainder;
                yield break;
            }

            yield return Remainder[..index];
            Remainder = Remainder[(index + 1)..];
        }
    }

    private static int FindClosingParenthesis(string text, int open)
    {
        var depth = 0;
        for (var i = open; i < text.Length; i++)
        {
            if (text[i] == '(')
            {
                depth++;
            }
            else if (text[i] == ')' && --depth == 0)
            {
                return i;
            }
        }

        return -1;
    }

    private static string? NullIfEmpty(string? text)
    {
        var trimmed = text?.Trim();
        return string.IsNullOrEmpty(trimmed) ? null : trimmed;
    }
}
