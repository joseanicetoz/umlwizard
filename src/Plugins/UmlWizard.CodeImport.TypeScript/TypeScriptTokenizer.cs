using UmlWizard.CodeImport;

namespace UmlWizard.CodeImport.TypeScript;

/// <summary>
/// Splits TypeScript into <see cref="Token"/>s for the declaration parser, so it can use
/// <see cref="CLikeTokenizer.Matching"/>: comments are left out; strings, template literals (with the code in their
/// <c>${…}</c>) and regular expressions are single tokens, so that the braces in them do not count; <c>$name</c> and
/// <c>#private</c> are identifiers; <c>=&gt;</c> and <c>...</c> are single symbols, so that an arrow does not close a
/// list of type arguments.
/// </summary>
internal static class TypeScriptTokenizer
{
    /// <summary>Words after which a slash starts a regular expression rather than a division.</summary>
    private static readonly HashSet<string> RegexAfterWords =
        ["return", "typeof", "case", "do", "else", "in", "of", "new", "delete", "void", "throw", "yield", "await", "instanceof"];

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
            else if (c is '\'' or '"' or '`' || (c == '/' && RegexCanStart(tokens)))
            {
                var end = c switch
                {
                    '`' => SkipTemplate(text, i),
                    '/' => SkipRegex(text, i),
                    _ => SkipString(text, i),
                };
                tokens.Add(new Token(TokenKind.String, text[i..end], line));
                line += Count(text, i, end);
                i = end;
            }
            else if (IsNameStart(c) || (c == '#' && IsNameStart(next)))
            {
                var start = i++;
                while (i < text.Length && IsNamePart(text[i]))
                {
                    i++;
                }

                tokens.Add(new Token(TokenKind.Identifier, text[start..i], line));
            }
            else if (char.IsDigit(c) || (c == '.' && char.IsDigit(next)))
            {
                var start = i++;
                while (i < text.Length && (char.IsLetterOrDigit(text[i]) || text[i] is '_' || (text[i] == '.' && i + 1 < text.Length && char.IsDigit(text[i + 1]))))
                {
                    i++;
                }

                tokens.Add(new Token(TokenKind.Number, text[start..i], line));
            }
            else if (c == '=' && next == '>')
            {
                tokens.Add(new Token(TokenKind.Symbol, "=>", line));
                i += 2;
            }
            else if (c == '.' && next == '.' && i + 2 < text.Length && text[i + 2] == '.')
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

    /// <summary>The line a token ends on: a template literal may span lines.</summary>
    public static int EndLine(Token token) => token.Line + token.Text.Count(ch => ch == '\n');

    private static bool IsNameStart(char c) => char.IsLetter(c) || c is '_' or '$';

    private static bool IsNamePart(char c) => char.IsLetterOrDigit(c) || c is '_' or '$';

    /// <summary>A slash starts a regular expression where an operand is expected, i.e. not after a value.</summary>
    private static bool RegexCanStart(List<Token> tokens)
    {
        if (tokens.Count == 0)
        {
            return true;
        }

        var previous = tokens[^1];
        return previous.Kind switch
        {
            TokenKind.Symbol => previous.Text is not (")" or "]" or "}"),
            TokenKind.Identifier => RegexAfterWords.Contains(previous.Text),
            _ => false,
        };
    }

    private static int SkipComment(string text, int i)
    {
        if (text[i + 1] == '/')
        {
            var end = text.IndexOf('\n', i);
            return end < 0 ? text.Length : end;
        }

        var close = text.IndexOf("*/", i + 2, StringComparison.Ordinal);
        return close < 0 ? text.Length : close + 2;
    }

    /// <summary>A '…' or "…" string, which ends at its line.</summary>
    private static int SkipString(string text, int i)
    {
        var quote = text[i++];
        while (i < text.Length && text[i] != quote && text[i] != '\n')
        {
            i += text[i] == '\\' ? 2 : 1;
        }

        return Math.Min(i + 1, text.Length);
    }

    /// <summary>A template literal, with the code of its <c>${…}</c> parts, which may hold strings and templates.</summary>
    private static int SkipTemplate(string text, int i)
    {
        i++;
        while (i < text.Length)
        {
            switch (text[i])
            {
                case '\\':
                    i += 2;
                    break;
                case '`':
                    return i + 1;
                case '$' when i + 1 < text.Length && text[i + 1] == '{':
                    i = SkipCode(text, i + 2);
                    break;
                default:
                    i++;
                    break;
            }
        }

        return text.Length;
    }

    /// <summary>Code up to and past the brace that closes it, e.g. the expression of a template's <c>${…}</c>.</summary>
    private static int SkipCode(string text, int i)
    {
        var depth = 0;
        while (i < text.Length)
        {
            var c = text[i];
            switch (c)
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
                case '\'' or '"':
                    i = SkipString(text, i);
                    break;
                case '`':
                    i = SkipTemplate(text, i);
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

    /// <summary>A regular expression literal with its flags; a slash inside a [character class] does not end it.</summary>
    private static int SkipRegex(string text, int i)
    {
        i++;
        var inClass = false;
        while (i < text.Length && text[i] != '\n')
        {
            var c = text[i];
            if (c == '\\')
            {
                i += 2;
                continue;
            }

            if (c == '[')
            {
                inClass = true;
            }
            else if (c == ']')
            {
                inClass = false;
            }
            else if (c == '/' && !inClass)
            {
                i++;
                while (i < text.Length && char.IsLetter(text[i]))
                {
                    i++;
                }

                return i;
            }

            i++;
        }

        return i;
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
