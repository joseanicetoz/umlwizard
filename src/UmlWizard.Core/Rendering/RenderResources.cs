using SkiaSharp;
using UmlWizard.Core.Model;

namespace UmlWizard.Core.Rendering;

/// <summary>Paints and typefaces shared by the renderers.</summary>
internal sealed class RenderResources : IDisposable
{
    private readonly List<IDisposable> _owned = [];
    private readonly Dictionary<(string Family, SKFontStyle Style), SKTypeface> _typefaces = [];
    private readonly Dictionary<(string Family, float Size, bool Bold, bool Italic, SKColor Text), FontSet> _fonts = [];
    private readonly Dictionary<SKColor, SKPaint> _fills = [];
    private readonly Dictionary<(SKColor Color, float Width), SKPaint> _strokes = [];
    private readonly Dictionary<(SKColor Color, float Width), SKPaint> _dashes = [];

    public RenderResources(RenderTheme theme)
    {
        Theme = theme;
        Fill = FillPaint(Theme.NodeFill);
        ArrowFill = FillPaint(Theme.Line);
        LabelBackground = FillPaint(Theme.CanvasBackground.WithAlpha(0xE6));
        HandleFill = FillPaint(Theme.Selection);
        Shadow = FillPaint(Theme.Shadow);
        Shadow.MaskFilter = Own(SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 3f));

        Border = StrokePaint(Theme.Border, 1.2f);
        SelectionBorder = StrokePaint(Theme.Selection, 2.2f);
        HighlightBorder = StrokePaint(Theme.Highlight, 2.2f);
        Line = StrokePaint(Theme.Line, 1.3f);
        SelectedLine = StrokePaint(Theme.Selection, 2.2f);
        TextStroke = StrokePaint(Theme.Text, 1f);

        var dash = Own(SKPathEffect.CreateDash([6f, 4f], 0f));
        DashedLine = StrokePaint(Theme.Line, 1.3f);
        DashedLine.PathEffect = dash;
        SelectedDashedLine = StrokePaint(Theme.Selection, 2.2f);
        SelectedDashedLine.PathEffect = dash;
        LifelineLine = StrokePaint(Theme.MutedText, 1f);
        LifelineLine.PathEffect = Own(SKPathEffect.CreateDash([5f, 5f], 0f));
        ConnectorPreview = StrokePaint(Theme.Selection, 1.5f);
        ConnectorPreview.PathEffect = Own(SKPathEffect.CreateDash([4f, 3f], 0f));
        TextBoxOutline = StrokePaint(Theme.TextBoxOutline, 1f);
        TextBoxOutline.PathEffect = Own(SKPathEffect.CreateDash([4f, 3f], 0f));
        NoteFold = FillPaint(Theme.NoteFold);
    }

    /// <summary>The colours the paints were made with.</summary>
    public RenderTheme Theme { get; }

    public SKPaint Fill { get; }
    public SKPaint ArrowFill { get; }
    public SKPaint LabelBackground { get; }
    public SKPaint HandleFill { get; }
    public SKPaint Shadow { get; }
    public SKPaint Border { get; }
    public SKPaint SelectionBorder { get; }
    public SKPaint HighlightBorder { get; }
    public SKPaint Line { get; }
    public SKPaint SelectedLine { get; }
    public SKPaint DashedLine { get; }
    public SKPaint SelectedDashedLine { get; }
    public SKPaint LifelineLine { get; }
    public SKPaint TextStroke { get; }
    public SKPaint ConnectorPreview { get; }
    public SKPaint TextBoxOutline { get; }
    public SKPaint NoteFold { get; }

    /// <summary>
    /// Text paints for <paramref name="font"/>; <c>null</c> is the default font. Text drawn on a custom
    /// <paramref name="background"/> (a <see cref="ShapeStyle.Fill"/>) is dark or light, whichever reads better on it.
    /// </summary>
    public FontSet Font(DiagramFont? font, SKColor? background = null)
    {
        var family = string.IsNullOrWhiteSpace(font?.Family) ? RenderTheme.DefaultFontFamily : font.Family.Trim();
        var size = (float)Math.Clamp(font?.Size ?? DiagramFont.DefaultSize, DiagramFont.MinSize, DiagramFont.MaxSize);
        var colors = background is { } fill ? ShapeColors.TextColorsOn(fill) : (Text: Theme.Text, Muted: Theme.MutedText);
        var key = (family, size, font?.Bold ?? false, font?.Italic ?? false, colors.Text);
        if (!_fonts.TryGetValue(key, out var set))
        {
            set = new FontSet(this, family, size, key.Item3, key.Item4, colors.Text, colors.Muted);
            _fonts[key] = set;
        }

        return set;
    }

    /// <summary>A fill paint of the element's own colour, else of <paramref name="fallback"/>.</summary>
    public SKPaint FillOf(ShapeStyle? style, SKColor fallback) => FillPaint(ShapeColors.CustomFill(style) ?? fallback, cached: true);

    /// <summary>The element's border: <see cref="Border"/> unless its style sets a colour or a thickness.</summary>
    public SKPaint BorderOf(ShapeStyle? style)
    {
        if (style is null || (style.Border is null && style.BorderWidth is null))
        {
            return Border;
        }

        return StrokeOf(ShapeColors.Parse(style.Border) ?? Theme.Border, ShapeColors.BorderWidth(style));
    }

    /// <summary>
    /// The solid and dashed paints of a connector: the theme's line unless its style sets a colour or thickness. A
    /// selected connector is drawn in the selection colour, at least as thick as its line. Dashes grow with thickness.
    /// </summary>
    public (SKPaint Solid, SKPaint Dashed) LinesOf(LineStyle? style, bool selected)
    {
        if (style is null || style.IsEmpty)
        {
            return selected ? (SelectedLine, SelectedDashedLine) : (Line, DashedLine);
        }

        var width = (float)Math.Clamp(style.Width ?? LineStyle.DefaultWidth, LineStyle.MinWidth, LineStyle.MaxWidth);
        var color = ShapeColors.Parse(style.Color) ?? Theme.Line;
        if (selected)
        {
            (color, width) = (Theme.Selection, Math.Max(width, SelectedLine.StrokeWidth));
        }

        if (!_dashes.TryGetValue((color, width), out var dashed))
        {
            var scale = Math.Max(1f, width / (float)LineStyle.DefaultWidth);
            dashed = StrokePaint(color, width);
            dashed.PathEffect = Own(SKPathEffect.CreateDash([6f * scale, 4f * scale], 0f));
            _dashes[(color, width)] = dashed;
        }

        return (StrokeOf(color, width), dashed);
    }

    /// <summary>
    /// Lines inside an element (compartment dividers, the edges of a 3-D node's front face, a note's fold): in its border
    /// colour, but always of the default thickness, so a thick border only thickens the outline.
    /// </summary>
    public SKPaint InnerLineOf(ShapeStyle? style) =>
        ShapeColors.Parse(style?.Border) is { } color ? StrokeOf(color, (float)ShapeStyle.DefaultBorderWidth) : Border;

    /// <summary>A stroke paint of the given colour and width, shared by every element that uses it.</summary>
    public SKPaint StrokeOf(SKColor color, float width)
    {
        if (!_strokes.TryGetValue((color, width), out var paint))
        {
            paint = StrokePaint(color, width);
            _strokes[(color, width)] = paint;
        }

        return paint;
    }

    public void Dispose()
    {
        for (var i = _owned.Count - 1; i >= 0; i--)
        {
            _owned[i].Dispose();
        }

        _owned.Clear();
        _fonts.Clear();
        _fills.Clear();
        _strokes.Clear();
        _dashes.Clear();
        _typefaces.Clear();
    }

    private T Own<T>(T disposable) where T : IDisposable
    {
        _owned.Add(disposable);
        return disposable;
    }

    private SKTypeface Typeface(string family, bool bold, bool italic)
    {
        var style = (bold, italic) switch
        {
            (true, true) => SKFontStyle.BoldItalic,
            (true, false) => SKFontStyle.Bold,
            (false, true) => SKFontStyle.Italic,
            _ => SKFontStyle.Normal,
        };
        if (!_typefaces.TryGetValue((family, style), out var typeface))
        {
            typeface = Own(SKTypeface.FromFamilyName(family, style)
                ?? SKTypeface.FromFamilyName(RenderTheme.DefaultFontFamily, style)
                ?? SKTypeface.FromFamilyName(null, style));
            _typefaces[(family, style)] = typeface;
        }

        return typeface;
    }

    private SKPaint TextPaint(SKTypeface typeface, float size, SKColor color) => Own(new SKPaint
    {
        Typeface = typeface,
        TextSize = size,
        Color = color,
        IsAntialias = true,
        SubpixelText = true,
    });

    private SKPaint FillPaint(SKColor color, bool cached = false)
    {
        if (cached && _fills.TryGetValue(color, out var shared))
        {
            return shared;
        }

        var paint = Own(new SKPaint
        {
            Color = color,
            Style = SKPaintStyle.Fill,
            IsAntialias = true,
        });
        if (cached)
        {
            _fills[color] = paint;
        }

        return paint;
    }

    private SKPaint StrokePaint(SKColor color, float width) => Own(new SKPaint
    {
        Color = color,
        Style = SKPaintStyle.Stroke,
        StrokeWidth = width,
        IsAntialias = true,
    });

    /// <summary>
    /// The paints of one font. <see cref="Text"/> has the font's own style; the other paints add the bold or
    /// italic that UML notation requires (e.g. class names, abstract operations).
    /// </summary>
    internal sealed class FontSet
    {
        private const float SmallRatio = 11f / 13f;

        private readonly SKPaint _underline;

        public FontSet(RenderResources owner, string family, float size, bool bold, bool italic, SKColor text, SKColor muted)
        {
            _underline = text == owner.Theme.Text ? owner.TextStroke : owner.StrokeOf(text, 1f);
            Text = owner.TextPaint(owner.Typeface(family, bold, italic), size, text);
            BoldText = owner.TextPaint(owner.Typeface(family, true, italic), size, text);
            ItalicText = owner.TextPaint(owner.Typeface(family, bold, true), size, text);
            BoldItalicText = owner.TextPaint(owner.Typeface(family, true, true), size, text);
            SmallText = owner.TextPaint(owner.Typeface(family, bold, italic), size * SmallRatio, muted);

            // 18 px rows for the default 13 pt font.
            LineHeight = MathF.Ceiling(size * 18f / 13f);
        }

        public SKPaint Text { get; }
        public SKPaint BoldText { get; }
        public SKPaint ItalicText { get; }
        public SKPaint BoldItalicText { get; }
        public SKPaint SmallText { get; }

        public float LineHeight { get; }

        /// <summary>Baseline that vertically centres a single line of text in a row starting at <paramref name="top"/>.</summary>
        public float Baseline(SKPaint paint, float top)
        {
            var metrics = paint.FontMetrics;
            return top + (LineHeight - (metrics.Descent - metrics.Ascent)) / 2f - metrics.Ascent;
        }

        public void DrawCenteredText(SKCanvas canvas, string text, float centerX, float top, SKPaint paint, bool underline = false)
        {
            var width = paint.MeasureText(text);
            DrawText(canvas, text, centerX - width / 2f, top, paint, underline);
        }

        public void DrawText(SKCanvas canvas, string text, float left, float top, SKPaint paint, bool underline = false)
        {
            var baseline = Baseline(paint, top);
            canvas.DrawText(text, left, baseline, paint);
            if (underline)
            {
                var width = paint.MeasureText(text);
                canvas.DrawLine(left, baseline + 2f, left + width, baseline + 2f, _underline);
            }
        }
    }
}
