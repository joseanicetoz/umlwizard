using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;

namespace UmlWizard.App.Controls;

/// <summary>
/// Shows a Markdown text (<see cref="MarkdownText"/>) as selectable text blocks: headings, lists, quotes and code blocks.
/// Code blocks are a <c>Border.code</c> and quotes a <c>Border.quote</c>, for the styles of the view that uses it.
/// </summary>
public sealed class MarkdownView : StackPanel
{
    public static readonly StyledProperty<string?> MarkdownProperty = AvaloniaProperty.Register<MarkdownView, string?>(nameof(Markdown));

    private static readonly FontFamily Monospace = new("Consolas, Menlo, monospace");

    static MarkdownView()
    {
        MarkdownProperty.Changed.AddClassHandler<MarkdownView>((view, _) => view.Build());
    }

    public MarkdownView()
    {
        Spacing = 6;
    }

    public string? Markdown
    {
        get => GetValue(MarkdownProperty);
        set => SetValue(MarkdownProperty, value);
    }

    private void Build()
    {
        Children.Clear();
        foreach (var block in MarkdownText.Parse(Markdown))
        {
            Children.Add(block switch
            {
                MarkdownHeading heading => Text(heading.Spans, FontWeight.SemiBold),
                MarkdownListItem item => ListItem(item),
                MarkdownQuote quote => new Border { Classes = { "quote" }, Child = Text(quote.Spans) },
                MarkdownCode code => new Border
                {
                    Classes = { "code" },
                    Child = new SelectableTextBlock { Text = code.Text, FontFamily = Monospace, TextWrapping = TextWrapping.Wrap },
                },
                MarkdownParagraph paragraph => Text(paragraph.Spans),
                _ => new Control(),
            });
        }
    }

    private static Grid ListItem(MarkdownListItem item)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), Margin = new Thickness(item.Depth * 14, 0, 0, 0) };
        grid.Children.Add(new TextBlock { Text = item.Marker, Margin = new Thickness(0, 0, 6, 0), MinWidth = 10 });
        var text = Text(item.Spans);
        Grid.SetColumn(text, 1);
        grid.Children.Add(text);
        return grid;
    }

    private static SelectableTextBlock Text(IReadOnlyList<MarkdownSpan> spans, FontWeight? weight = null)
    {
        var block = new SelectableTextBlock { TextWrapping = TextWrapping.Wrap };
        if (weight is { } w)
        {
            block.FontWeight = w;
        }

        block.Inlines ??= [];
        foreach (var span in spans)
        {
            var run = new Run(span.Text);
            if (span.Style.HasFlag(MarkdownStyle.Bold))
            {
                run.FontWeight = FontWeight.SemiBold;
            }

            if (span.Style.HasFlag(MarkdownStyle.Italic))
            {
                run.FontStyle = FontStyle.Italic;
            }

            if (span.Style.HasFlag(MarkdownStyle.Code))
            {
                run.FontFamily = Monospace;
            }

            block.Inlines.Add(run);
        }

        return block;
    }
}
