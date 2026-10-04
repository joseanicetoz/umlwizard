using System.Text;
using System.Text.RegularExpressions;

namespace UmlWizard.App.Controls;

/// <summary>How a run of Markdown text looks.</summary>
[Flags]
public enum MarkdownStyle
{
    None = 0,
    Bold = 1,
    Italic = 2,
    Code = 4,
}

/// <summary>A run of text in one style.</summary>
public sealed record MarkdownSpan(string Text, MarkdownStyle Style = MarkdownStyle.None);

/// <summary>A block of a Markdown text: a paragraph, heading, list item, quote or code block.</summary>
public abstract record MarkdownBlock;

public sealed record MarkdownParagraph(IReadOnlyList<MarkdownSpan> Spans) : MarkdownBlock;

public sealed record MarkdownHeading(int Level, IReadOnlyList<MarkdownSpan> Spans) : MarkdownBlock;

/// <param name="Marker">The bullet, or the number with its dot, e.g. "2.".</param>
/// <param name="Depth">0 for the outer list, 1 for a list in it, …</param>
public sealed record MarkdownListItem(string Marker, int Depth, IReadOnlyList<MarkdownSpan> Spans) : MarkdownBlock;

public sealed record MarkdownQuote(IReadOnlyList<MarkdownSpan> Spans) : MarkdownBlock;

public sealed record MarkdownCode(string Text) : MarkdownBlock;

/// <summary>
/// Reads the Markdown that AI services answer in, the common part of it: paragraphs, headings, bulleted and numbered
/// lists, quotes, fenced code blocks, and <c>**bold**</c>, <c>*italic*</c>, <c>`code`</c> and links in the text. Other
/// Markdown (tables, images, HTML) stays as written.
/// </summary>
public static partial class MarkdownText
{
    public static IReadOnlyList<MarkdownBlock> Parse(string? markdown)
    {
        var blocks = new List<MarkdownBlock>();
        var paragraph = new List<string>();
        var lines = (markdown ?? string.Empty).ReplaceLineEndings("\n").Split('\n');

        void EndParagraph()
        {
            if (paragraph.Count > 0)
            {
                blocks.Add(new MarkdownParagraph(Spans(string.Join(" ", paragraph))));
                paragraph.Clear();
            }
        }

        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            var trimmed = line.Trim();
            if (trimmed.StartsWith("```", StringComparison.Ordinal))
            {
                EndParagraph();
                var code = new List<string>();
                for (i++; i < lines.Length && !lines[i].Trim().StartsWith("```", StringComparison.Ordinal); i++)
                {
                    code.Add(lines[i]);
                }

                blocks.Add(new MarkdownCode(string.Join("\n", code)));
                continue;
            }

            if (trimmed.Length == 0)
            {
                EndParagraph();
                continue;
            }

            if (Heading().Match(trimmed) is { Success: true } heading)
            {
                EndParagraph();
                blocks.Add(new MarkdownHeading(heading.Groups[1].Length, Spans(heading.Groups[2].Value.TrimEnd('#', ' '))));
                continue;
            }

            if (ListItem().Match(line) is { Success: true } item)
            {
                EndParagraph();
                var depth = item.Groups[1].Value.Replace("\t", "    ").Length / 2;
                var marker = item.Groups[2].Value is "-" or "*" or "+" ? "•" : item.Groups[2].Value.TrimEnd(')', '.') + ".";
                blocks.Add(new MarkdownListItem(marker, Math.Min(depth, 4), Spans(item.Groups[3].Value)));
                continue;
            }

            if (trimmed.StartsWith('>'))
            {
                EndParagraph();
                blocks.Add(new MarkdownQuote(Spans(trimmed.TrimStart('>', ' '))));
                continue;
            }

            if (Rule().IsMatch(trimmed))
            {
                EndParagraph();
                continue;
            }

            // A line that goes on a list item, indented under it, belongs to it.
            if (paragraph.Count == 0 && blocks.Count > 0 && blocks[^1] is MarkdownListItem last && line.StartsWith("  ", StringComparison.Ordinal))
            {
                blocks[^1] = last with { Spans = [.. last.Spans, new MarkdownSpan(" "), .. Spans(trimmed)] };
                continue;
            }

            paragraph.Add(trimmed);
        }

        EndParagraph();
        return blocks;
    }

    /// <summary>The runs of a line of text, with its emphasis, code and links.</summary>
    public static IReadOnlyList<MarkdownSpan> Spans(string text)
    {
        var spans = new List<MarkdownSpan>();
        var plain = new StringBuilder();
        var style = MarkdownStyle.None;

        void Flush()
        {
            if (plain.Length > 0)
            {
                spans.Add(new MarkdownSpan(plain.ToString(), style));
                plain.Clear();
            }
        }

        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (c == '\\' && i + 1 < text.Length && "\\`*_[]#".Contains(text[i + 1]))
            {
                plain.Append(text[++i]);
                continue;
            }

            if (c == '`' && text.IndexOf('`', i + 1) is var close and > 0)
            {
                Flush();
                spans.Add(new MarkdownSpan(text[(i + 1)..close], style | MarkdownStyle.Code));
                i = close;
                continue;
            }

            if ((c == '*' || c == '_') && i + 1 < text.Length && text[i + 1] == c && Toggles(text, i, 2, style.HasFlag(MarkdownStyle.Bold)))
            {
                Flush();
                style ^= MarkdownStyle.Bold;
                i++;
                continue;
            }

            if ((c == '*' || c == '_') && Toggles(text, i, 1, style.HasFlag(MarkdownStyle.Italic)))
            {
                Flush();
                style ^= MarkdownStyle.Italic;
                continue;
            }

            if (c == '[' && Link().Match(text, i) is { Success: true, Index: var at } link && at == i)
            {
                Flush();
                spans.Add(new MarkdownSpan(link.Groups[1].Value, style));
                i += link.Length - 1;
                continue;
            }

            plain.Append(c);
        }

        Flush();
        return spans;
    }

    /// <summary>
    /// Whether the marker of <paramref name="length"/> at <paramref name="i"/> opens (before a word, with a closing one
    /// further on) or closes (after a word) emphasis; an underscore inside a word, as in snake_case, does neither.
    /// </summary>
    private static bool Toggles(string text, int i, int length, bool open)
    {
        var before = i > 0 ? text[i - 1] : ' ';
        var after = i + length < text.Length ? text[i + length] : ' ';
        if (text[i] == '_' && char.IsLetterOrDigit(before) && char.IsLetterOrDigit(after))
        {
            return false;
        }

        if (open)
        {
            return !char.IsWhiteSpace(before);
        }

        var marker = text.Substring(i, length);
        return !char.IsWhiteSpace(after) && text.IndexOf(marker, i + length, StringComparison.Ordinal) > i + length;
    }

    [GeneratedRegex(@"^(#{1,6})\s+(.*)$")]
    private static partial Regex Heading();

    [GeneratedRegex(@"^(\s*)([-*+]|\d{1,3}[.)])\s+(.*)$")]
    private static partial Regex ListItem();

    [GeneratedRegex(@"^(-{3,}|\*{3,}|_{3,})$")]
    private static partial Regex Rule();

    [GeneratedRegex(@"\[([^\]]+)\]\(([^)\s]+)\)")]
    private static partial Regex Link();
}
