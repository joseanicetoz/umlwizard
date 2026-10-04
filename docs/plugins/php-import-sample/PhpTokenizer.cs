using UmlWizard.CodeImport;

namespace Acme.UmlWizard.PhpImport;

/// <summary>
/// Splits PHP source into UML Wizard's <see cref="Token"/>s, so that the parser can use
/// <see cref="CLikeTokenizer.Matching"/> and <see cref="CLikeTokenizer.Join"/>. PHP differs enough from the languages
/// <see cref="CLikeTokenizer"/> reads to need its own: HTML outside <c>&lt;?php … ?&gt;</c> and <c>#</c> comments are
/// left out; strings, which may span lines, and heredocs are single tokens; <c>$name</c> and qualified names such as
/// <c>\App\Models\Order</c> are single identifiers.
/// </summary>
internal static class PhpTokenizer
{
    public static List<Token> Tokenize(string text)
    {
        var tokens = new List<Token>();
        var line = 1;
        var i = 0;
        var inCode = false;
        while (i < text.Length)
        {
            if (!inCode)
            {
                // HTML, up to <?php (or <?=)
                var open = text.IndexOf("<?", i, StringComparison.Ordinal);
                if (open < 0)
                {
                    break;
                }

                line += Count(text, i, open, '\n');
                i = open + (string.CompareOrdinal(text, open, "<?php", 0, 5) == 0 ? 5 : 2);
                inCode = true;
                continue;
            }

            var c = text[i];
            var next = i + 1 < text.Length ? text[i + 1] : '\0';
            if (c == '\n')
            {
                line++;
                i++;
            }
            else if (char.IsWhiteSpace(c))
            {
                i++;
            }
            else if (c == '?' && next == '>')
            {
                inCode = false;
                i += 2;
            }
            else if ((c == '/' && next == '/') || (c == '#' && next != '['))
            {
                // A line comment ends at the end of the line, or at ?>.
                while (i < text.Length && text[i] != '\n' && string.CompareOrdinal(text, i, "?>", 0, 2) != 0)
                {
                    i++;
                }
            }
            else if (c == '/' && next == '*')
            {
                var end = text.IndexOf("*/", i + 2, StringComparison.Ordinal);
                end = end < 0 ? text.Length : end + 2;
                line += Count(text, i, end, '\n');
                i = end;
            }
            else if (c is '\'' or '"' or '`' || string.CompareOrdinal(text, i, "<<<", 0, 3) == 0)
            {
                var end = c == '<' ? SkipHeredoc(text, i) : SkipString(text, i);
                tokens.Add(new Token(TokenKind.String, text[i..end], line));
                line += Count(text, i, end, '\n');
                i = end;
            }
            else if (IsNameStart(c) || (c is '$' or '\\' && IsNameStart(next)))
            {
                var start = i;
                i++;
                while (i < text.Length && (IsNamePart(text[i]) || (text[i] == '\\' && i + 1 < text.Length && IsNameStart(text[i + 1]))))
                {
                    i++;
                }

                tokens.Add(new Token(TokenKind.Identifier, text[start..i], line));
            }
            else if (char.IsDigit(c))
            {
                var start = i;
                while (i < text.Length && (char.IsLetterOrDigit(text[i]) || text[i] is '.' or '_'))
                {
                    i++;
                }

                tokens.Add(new Token(TokenKind.Number, text[start..i], line));
            }
            else if (c == ':' && next == ':')
            {
                tokens.Add(new Token(TokenKind.Symbol, "::", line));
                i += 2;
            }
            else if (string.CompareOrdinal(text, i, "...", 0, 3) == 0)
            {
                tokens.Add(new Token(TokenKind.Symbol, "...", line));
                i += 3;
            }
            else
            {
                tokens.Add(new Token(TokenKind.Symbol, c.ToString(), line));
                i++;
            }
        }

        return tokens;
    }

    private static bool IsNameStart(char c) => char.IsLetter(c) || c == '_' || c >= '\x80';

    private static bool IsNamePart(char c) => char.IsLetterOrDigit(c) || c == '_' || c >= '\x80';

    /// <summary>The end of a '…', "…" or `…` string, which may span lines.</summary>
    private static int SkipString(string text, int i)
    {
        var quote = text[i++];
        while (i < text.Length && text[i] != quote)
        {
            i += text[i] == '\\' ? 2 : 1;
        }

        return Math.Min(i + 1, text.Length);
    }

    /// <summary>The end of a heredoc (&lt;&lt;&lt;EOT) or nowdoc (&lt;&lt;&lt;'EOT'): the line that starts with its label.</summary>
    private static int SkipHeredoc(string text, int i)
    {
        var start = i + 3;
        while (start < text.Length && text[start] is ' ' or '\t' or '\'' or '"')
        {
            start++;
        }

        var end = start;
        while (end < text.Length && IsNamePart(text[end]))
        {
            end++;
        }

        var label = text[start..end];
        if (label.Length == 0)
        {
            return i + 3;
        }

        var lineStart = text.IndexOf('\n', end);
        while (lineStart >= 0)
        {
            var j = lineStart + 1;
            while (j < text.Length && text[j] is ' ' or '\t')
            {
                j++;
            }

            if (string.CompareOrdinal(text, j, label, 0, label.Length) == 0 &&
                (j + label.Length >= text.Length || !IsNamePart(text[j + label.Length])))
            {
                return j + label.Length;
            }

            lineStart = text.IndexOf('\n', j);
        }

        return text.Length;
    }

    private static int Count(string text, int start, int end, char c)
    {
        var count = 0;
        for (var i = start; i < end && i < text.Length; i++)
        {
            count += text[i] == c ? 1 : 0;
        }

        return count;
    }
}
