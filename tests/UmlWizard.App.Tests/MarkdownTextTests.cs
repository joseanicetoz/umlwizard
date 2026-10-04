using UmlWizard.App.Controls;

namespace UmlWizard.App.Tests;

public sealed class MarkdownTextTests
{
    [Fact]
    public void Blocks_AreParagraphsHeadingsListsQuotesAndCode()
    {
        var blocks = MarkdownText.Parse("""
            ### Review

            The model is **mostly** fine,
            with two issues:

            1. `Order` has no *multiplicity*.
            2. Cycles:
               - A → B
               - B → A
            > Keep it small.

            ```plantuml
            class A
              -x : int
            ```
            ---
            """);

        Assert.Collection(blocks,
            b => Assert.Equal(3, Assert.IsType<MarkdownHeading>(b).Level),
            b => Assert.Equal("The model is mostly fine, with two issues:", Text(((MarkdownParagraph)b).Spans)),
            b => Assert.Equal(("1.", 0), (((MarkdownListItem)b).Marker, ((MarkdownListItem)b).Depth)),
            b => Assert.Equal("Cycles:", Text(((MarkdownListItem)b).Spans)),
            b => Assert.Equal(("•", 1, "A → B"), (((MarkdownListItem)b).Marker, ((MarkdownListItem)b).Depth, Text(((MarkdownListItem)b).Spans))),
            b => Assert.Equal("B → A", Text(((MarkdownListItem)b).Spans)),
            b => Assert.Equal("Keep it small.", Text(((MarkdownQuote)b).Spans)),
            b => Assert.Equal("class A\n  -x : int", ((MarkdownCode)b).Text));
    }

    [Fact]
    public void Spans_CarryBoldItalicCodeAndLinks_ButNotUnderscoresInNames()
    {
        Assert.Equal(
            [
                new MarkdownSpan("Use "),
                new MarkdownSpan("snake_case", MarkdownStyle.Code),
                new MarkdownSpan(" or order_total, "),
                new MarkdownSpan("very", MarkdownStyle.Bold),
                new MarkdownSpan(" "),
                new MarkdownSpan("nice", MarkdownStyle.Italic),
                new MarkdownSpan(", see the manual; 2 * 3 * 4 = 24."),
            ],
            Merge(MarkdownText.Spans("Use `snake_case` or order_total, **very** *nice*, see [the manual](https://example.com); 2 * 3 * 4 = 24.")));
    }

    [Fact]
    public void NothingAtAll_IsNoBlocks() => Assert.Empty(MarkdownText.Parse(null));

    private static string Text(IEnumerable<MarkdownSpan> spans) => string.Concat(spans.Select(s => s.Text));

    /// <summary>Neighbouring spans of the same style as one, as they read.</summary>
    private static List<MarkdownSpan> Merge(IReadOnlyList<MarkdownSpan> spans)
    {
        var merged = new List<MarkdownSpan>();
        foreach (var span in spans)
        {
            if (merged.Count > 0 && merged[^1].Style == span.Style && span.Style != MarkdownStyle.Code && merged[^1].Style != MarkdownStyle.Code)
            {
                merged[^1] = merged[^1] with { Text = merged[^1].Text + span.Text };
            }
            else
            {
                merged.Add(span);
            }
        }

        return merged;
    }
}
