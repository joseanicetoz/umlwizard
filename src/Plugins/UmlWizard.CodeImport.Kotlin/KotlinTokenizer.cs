using UmlWizard.CodeImport;

namespace UmlWizard.CodeImport.Kotlin;

/// <summary>
/// Splits Kotlin into <see cref="Token"/>s for the declaration parser, so it can use
/// <see cref="CLikeTokenizer.Matching"/>: comments (block comments nest) are left out; strings, raw strings and
/// character literals are single tokens, with the code of their <c>${…}</c> templates, so that the braces in them do
/// not count; a <c>`quoted name`</c> is an identifier; <c>-&gt;</c> is a single symbol, so that a lambda's arrow does not
/// close a list of type arguments.
/// </summary>
internal static class KotlinTokenizer
{
    public static List<Token> Tokenize(string text)
    {
        var tokens = new List<Token>();
        var line = 1;
        var i = 0;
        while (i < text.Length)
        {
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
            else if (c == '/' && next is '/' or '*')
            {
                var end = SkipComment(text, i);
                line += Count(text, i, end);
                i = end;
            }
            else if (c is '"' or '\'')
            {
                var end = c == '\'' ? SkipChar(text, i) : SkipString(text, i);
                tokens.Add(new Token(TokenKind.String, text[i..end], line));
                line += Count(text, i, end);
                i = end;
            }
            else if (c == '`')
            {
                var end = text.IndexOf('`', i + 1);
                end = end < 0 ? text.Length : end;
                tokens.Add(new Token(TokenKind.Identifier, text[(i + 1)..end], line));
                i = Math.Min(end + 1, text.Length);
            }
            else if (char.IsLetter(c) || c == '_')
            {
                var start = i++;
                while (i < text.Length && (char.IsLetterOrDigit(text[i]) || text[i] == '_'))
                {
                    i++;
                }

                tokens.Add(new Token(TokenKind.Identifier, text[start..i], line));
            }
            else if (char.IsDigit(c))
            {
                var start = i++;
                while (i < text.Length && (char.IsLetterOrDigit(text[i]) || text[i] == '_' || (text[i] == '.' && i + 1 < text.Length && char.IsDigit(text[i + 1]))))
                {
                    i++;
                }

                tokens.Add(new Token(TokenKind.Number, text[start..i], line));
            }
            else if (c == '-' && next == '>')
            {
                tokens.Add(new Token(TokenKind.Symbol, "->", line));
                i += 2;
            }
            else
            {
                tokens.Add(new Token(TokenKind.Symbol, c.ToString(), line));
                i++;
            }
        }

        return tokens;
    }

    /// <summary>The line a token ends on: a raw string may span lines.</summary>
    public static int EndLine(Token token) => token.Line + token.Text.Count(ch => ch == '\n');

    /// <summary>A line comment, or a block comment, which may hold other block comments.</summary>
    private static int SkipComment(string text, int i)
    {
        if (text[i + 1] == '/')
        {
            var end = text.IndexOf('\n', i);
            return end < 0 ? text.Length : end;
        }

        var depth = 0;
        while (i < text.Length)
        {
            if (text[i] == '/' && i + 1 < text.Length && text[i + 1] == '*')
            {
                depth++;
                i += 2;
            }
            else if (text[i] == '*' && i + 1 < text.Length && text[i + 1] == '/')
            {
                i += 2;
                if (--depth == 0)
                {
                    return i;
                }
            }
            else
            {
                i++;
            }
        }

        return text.Length;
    }

    /// <summary>A "string" or a """raw string""", with the code of its <c>${…}</c> templates.</summary>
    private static int SkipString(string text, int i)
    {
        var raw = string.CompareOrdinal(text, i, "\"\"\"", 0, 3) == 0;
        i += raw ? 3 : 1;
        while (i < text.Length)
        {
            var c = text[i];
            if (raw && string.CompareOrdinal(text, i, "\"\"\"", 0, 3) == 0)
            {
                // A raw string may end with more quotes, which belong to it.
                i += 3;
                while (i < text.Length && text[i] == '"')
                {
                    i++;
                }

                return i;
            }

            if (!raw && c == '"')
            {
                return i + 1;
            }

            if (!raw && c == '\n')
            {
                return i;
            }

            if (!raw && c == '\\')
            {
                i += 2;
            }
            else if (c == '$' && i + 1 < text.Length && text[i + 1] == '{')
            {
                i = SkipCode(text, i + 2);
            }
            else
            {
                i++;
            }
        }

        return text.Length;
    }

    /// <summary>A character literal: 'a', '\n', 'A'.</summary>
    private static int SkipChar(string text, int i)
    {
        i++;
        while (i < text.Length && text[i] != '\'' && text[i] != '\n')
        {
            i += text[i] == '\\' ? 2 : 1;
        }

        return Math.Min(i + 1, text.Length);
    }

    /// <summary>The code of a template, up to and past the brace that closes it.</summary>
    private static int SkipCode(string text, int i)
    {
        var depth = 0;
        while (i < text.Length)
        {
            switch (text[i])
            {
                case '{':
                    depth++;
                    i++;
                    break;
                case '}' when depth == 0:
                    return i + 1;
                case '}':
                    depth--;
                    i++;
                    break;
                case '"':
                    i = SkipString(text, i);
                    break;
                case '\'':
                    i = SkipChar(text, i);
                    break;
                case '/' when i + 1 < text.Length && text[i + 1] is '/' or '*':
                    i = SkipComment(text, i);
                    break;
                default:
                    i++;
                    break;
            }
        }

        return text.Length;
    }

    private static int Count(string text, int start, int end)
    {
        var count = 0;
        for (var i = start; i < end && i < text.Length; i++)
        {
            count += text[i] == '\n' ? 1 : 0;
        }

        return count;
    }
}
