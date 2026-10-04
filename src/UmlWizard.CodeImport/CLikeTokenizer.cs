using System.Text;

namespace UmlWizard.CodeImport;

public enum TokenKind
{
    Identifier,
    Number,
    String,
    Symbol,
}

/// <summary>A word, number, string or character literal, or punctuation of C-like source code.</summary>
public readonly record struct Token(TokenKind Kind, string Text, int Line)
{
    public bool Is(string text) => Text == text && Kind != TokenKind.String;

    public override string ToString() => Text;
}

/// <summary>
/// Splits the source of a C-like language (Java, C++, C#, …) into tokens for a declaration parser: comments are left
/// out, strings (also Java text blocks and C++ raw strings) are single tokens, and punctuation is one character per
/// token except <c>::</c> and <c>...</c>, so that <c>&gt;&gt;</c> closes two lists of type arguments.
/// </summary>
public static class CLikeTokenizer
{
    /// <param name="preprocessor">True for C and C++: lines starting with <c>#</c> are left out and counted.</param>
    public static List<Token> Tokenize(string text, bool preprocessor = false) => Tokenize(text, preprocessor, out _);

    /// <param name="directives">The number of preprocessor directives that were left out.</param>
    public static List<Token> Tokenize(string text, bool preprocessor, out int directives)
    {
        var tokens = new List<Token>();
        directives = 0;
        var line = 1;
        var atLineStart = true;
        var i = 0;
        while (i < text.Length)
        {
            var c = text[i];
            if (c == '\n')
            {
                line++;
                atLineStart = true;
                i++;
                continue;
            }

            if (char.IsWhiteSpace(c))
            {
                i++;
                continue;
            }

            if (preprocessor && atLineStart && c == '#')
            {
                // A directive runs to the end of the line, and on while lines end with a backslash.
                directives++;
                while (i < text.Length && text[i] != '\n')
                {
                    if (text[i] == '\\' && i + 1 < text.Length && text[i + 1] is '\n' or '\r')
                    {
                        i = text.IndexOf('\n', i) is var next && next < 0 ? text.Length : next + 1;
                        line++;
                        continue;
                    }

                    i++;
                }

                continue;
            }

            atLineStart = false;
            if (c == '/' && i + 1 < text.Length && text[i + 1] == '/')
            {
                while (i < text.Length && text[i] != '\n')
                {
                    i++;
                }

                continue;
            }

            if (c == '/' && i + 1 < text.Length && text[i + 1] == '*')
            {
                var end = text.IndexOf("*/", i + 2, StringComparison.Ordinal);
                end = end < 0 ? text.Length : end + 2;
                line += Count(text, i, end, '\n');
                i = end;
                continue;
            }

            if (c == '"' || c == '\'' || IsStringPrefix(text, i))
            {
                var start = i;
                i = SkipString(text, i);
                var literal = text[start..i];
                tokens.Add(new Token(TokenKind.String, literal, line));
                line += Count(literal, 0, literal.Length, '\n');
                continue;
            }

            if (char.IsLetter(c) || c == '_' || c == '$')
            {
                var start = i;
                while (i < text.Length && (char.IsLetterOrDigit(text[i]) || text[i] is '_' or '$'))
                {
                    i++;
                }

                tokens.Add(new Token(TokenKind.Identifier, text[start..i], line));
                continue;
            }

            if (char.IsDigit(c))
            {
                var start = i;
                while (i < text.Length && (char.IsLetterOrDigit(text[i]) || text[i] is '.' or '_' or '\''))
                {
                    i++;
                }

                tokens.Add(new Token(TokenKind.Number, text[start..i], line));
                continue;
            }

            if (c == ':' && i + 1 < text.Length && text[i + 1] == ':')
            {
                tokens.Add(new Token(TokenKind.Symbol, "::", line));
                i += 2;
                continue;
            }

            if (c == '.' && i + 2 < text.Length && text[i + 1] == '.' && text[i + 2] == '.')
            {
                tokens.Add(new Token(TokenKind.Symbol, "...", line));
                i += 3;
                continue;
            }

            tokens.Add(new Token(TokenKind.Symbol, c.ToString(), line));
            i++;
        }

        return tokens;
    }

    /// <summary>
    /// Joins tokens into readable code: no space around <c>::</c>, <c>.</c>, brackets and type arguments, one after
    /// commas, and one between words, e.g. <c>std::map&lt;int, Order*&gt;</c> or <c>final List&lt;String&gt;</c>.
    /// </summary>
    public static string Join(IEnumerable<Token> tokens)
    {
        var text = new StringBuilder();
        Token? previous = null;
        foreach (var token in tokens)
        {
            if (previous is { } p && NeedsSpace(p, token))
            {
                text.Append(' ');
            }

            text.Append(token.Text);
            previous = token;
        }

        return text.ToString();

        static bool NeedsSpace(Token previous, Token next) =>
            previous.Is(",") ||
            (previous.Kind != TokenKind.Symbol && next.Kind != TokenKind.Symbol) ||
            (previous.Is("=") || next.Is("=")) ||
            (previous.Is(">") && next.Kind == TokenKind.Identifier) ||
            (previous.Is("*") || previous.Is("&")) && next.Kind == TokenKind.Identifier;
    }

    /// <summary>The index of the token that closes the bracket at <paramref name="open"/> (<c>( [ { &lt;</c>), or the last index.</summary>
    public static int Matching(IReadOnlyList<Token> tokens, int open)
    {
        var (opener, closer) = tokens[open].Text switch
        {
            "(" => ("(", ")"),
            "[" => ("[", "]"),
            "<" => ("<", ">"),
            _ => ("{", "}"),
        };
        var depth = 0;
        for (var i = open; i < tokens.Count; i++)
        {
            if (tokens[i].Is(opener))
            {
                depth++;
            }
            else if (tokens[i].Is(closer) && --depth == 0)
            {
                return i;
            }
            else if (opener == "<" && (tokens[i].Is(";") || tokens[i].Is("{") || tokens[i].Is(")")))
            {
                // Not a list of type arguments after all (e.g. a comparison): stop at the end of the statement.
                return i - 1;
            }
        }

        return tokens.Count - 1;
    }

    private static bool IsStringPrefix(string text, int i)
    {
        // C++ raw strings R"(…)" and encoding prefixes u8"…", L"…"; Java text blocks start with """ and are strings too.
        var j = i;
        while (j < text.Length && j - i < 3 && text[j] is 'u' or 'U' or 'L' or '8')
        {
            j++;
        }

        if (j < text.Length && text[j] == 'R')
        {
            j++;
        }

        return j > i && j < text.Length && text[j] == '"' && (i == 0 || !(char.IsLetterOrDigit(text[i - 1]) || text[i - 1] == '_'));
    }

    private static int SkipString(string text, int i)
    {
        var raw = false;
        while (text[i] != '"' && text[i] != '\'')
        {
            raw |= text[i] == 'R';
            i++;
        }

        var quote = text[i];
        if (raw)
        {
            var open = text.IndexOf('(', i);
            var delimiter = open < 0 ? string.Empty : text[(i + 1)..open];
            var close = open < 0 ? -1 : text.IndexOf(")" + delimiter + "\"", open, StringComparison.Ordinal);
            return close < 0 ? text.Length : close + delimiter.Length + 2;
        }

        if (quote == '"' && i + 2 < text.Length && text[i + 1] == '"' && text[i + 2] == '"')
        {
            var close = text.IndexOf("\"\"\"", i + 3, StringComparison.Ordinal);
            return close < 0 ? text.Length : close + 3;
        }

        i++;
        while (i < text.Length && text[i] != quote && text[i] != '\n')
        {
            i += text[i] == '\\' ? 2 : 1;
        }

        return Math.Min(i + 1, text.Length);
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
