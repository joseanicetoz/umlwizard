using SkiaSharp;

namespace UmlWizard.Core.Rendering;

/// <summary>Breaks text into lines that fit a width: at line breaks, between words, and inside words that are too long.</summary>
internal static class TextWrapper
{
    public static List<string> Wrap(string? text, SKPaint paint, float maxWidth)
    {
        var lines = new List<string>();
        maxWidth = Math.Max(maxWidth, 1f);
        foreach (var paragraph in (text ?? string.Empty).Replace("\r\n", "\n").Split('\n'))
        {
            var line = string.Empty;
            foreach (var word in paragraph.Split(' '))
            {
                var candidate = line.Length == 0 ? word : line + " " + word;
                if (paint.MeasureText(candidate) <= maxWidth)
                {
                    line = candidate;
                    continue;
                }

                if (line.Length > 0)
                {
                    lines.Add(line);
                }

                line = word;
                while (line.Length > 1 && paint.MeasureText(line) > maxWidth)
                {
                    var fits = Math.Max(1, (int)paint.BreakText(line, maxWidth));
                    lines.Add(line[..fits]);
                    line = line[fits..];
                }
            }

            lines.Add(line);
        }

        return lines;
    }
}
