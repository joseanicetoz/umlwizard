using SkiaSharp;
using UmlWizard.Core.Model;

namespace UmlWizard.Core.Rendering;

/// <summary>Colours of <see cref="ShapeStyle"/>s: parsing, and keeping text readable on a custom fill.</summary>
public static class ShapeColors
{
    /// <summary>Parses <c>#RRGGBB</c> (or <c>#AARRGGBB</c>); <c>null</c> for anything else.</summary>
    public static SKColor? Parse(string? text) =>
        !string.IsNullOrWhiteSpace(text) && text.TrimStart().StartsWith('#') && SKColor.TryParse(text.Trim(), out var color) ? color : null;

    /// <summary>The colour as <c>#RRGGBB</c>, the form <see cref="ShapeStyle"/> stores.</summary>
    public static string ToHex(SKColor color) => $"#{color.Red:X2}{color.Green:X2}{color.Blue:X2}";

    /// <summary>The custom fill of <paramref name="style"/>, or <c>null</c> when it has none (or it is not a colour).</summary>
    public static SKColor? CustomFill(ShapeStyle? style) => Parse(style?.Fill);

    /// <summary>The border thickness of a style, within the allowed range.</summary>
    public static float BorderWidth(ShapeStyle? style) =>
        (float)Math.Clamp(style?.BorderWidth ?? ShapeStyle.DefaultBorderWidth, ShapeStyle.MinBorderWidth, ShapeStyle.MaxBorderWidth);

    /// <summary>The themes' dark or light text colours, whichever has more contrast with <paramref name="fill"/>.</summary>
    public static (SKColor Text, SKColor Muted) TextColorsOn(SKColor fill) =>
        ContrastRatio(fill, RenderTheme.Light.Text) >= ContrastRatio(fill, RenderTheme.Dark.Text)
            ? (RenderTheme.Light.Text, RenderTheme.Light.MutedText)
            : (RenderTheme.Dark.Text, RenderTheme.Dark.MutedText);

    /// <summary>The WCAG contrast ratio of two colours, from 1 (the same) to 21 (black on white).</summary>
    public static double ContrastRatio(SKColor a, SKColor b)
    {
        var (la, lb) = (Luminance(a), Luminance(b));
        return (Math.Max(la, lb) + 0.05) / (Math.Min(la, lb) + 0.05);

        static double Luminance(SKColor c) => 0.2126 * Linear(c.Red) + 0.7152 * Linear(c.Green) + 0.0722 * Linear(c.Blue);

        static double Linear(byte channel)
        {
            var v = channel / 255.0;
            return v <= 0.03928 ? v / 12.92 : Math.Pow((v + 0.055) / 1.055, 2.4);
        }
    }

    /// <summary>Mixes <paramref name="amount"/> of <paramref name="other"/> into <paramref name="color"/>, e.g. to shade a custom fill.</summary>
    public static SKColor Blend(SKColor color, SKColor other, float amount) => new(
        (byte)(color.Red + (other.Red - color.Red) * amount),
        (byte)(color.Green + (other.Green - color.Green) * amount),
        (byte)(color.Blue + (other.Blue - color.Blue) * amount));
}
