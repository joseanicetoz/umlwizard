using SkiaSharp;
using UmlWizard.Core.Model;

namespace UmlWizard.Core.Rendering;

/// <summary>
/// Draws a diagram with SkiaSharp in world coordinates and answers geometric queries
/// (hit testing, in-line editor placement, content bounds) using the same measurements.
/// </summary>
public abstract class DiagramRenderer : IDisposable
{
    private protected RenderResources R { get; private set; } = new(RenderTheme.Light);

    /// <summary>The user's preferred look of the shapes of this diagram type; <c>null</c> keeps the theme's.</summary>
    public DiagramStyle? Defaults { get; set; }

    /// <summary>The style an element is drawn with: its own, completed with the <see cref="Defaults"/>.</summary>
    private protected ShapeStyle? StyleOf(ModelElement element, ShapeStyle? own) => Defaults is null ? own : Defaults.Apply(element, own);

    /// <summary>The colours the diagram is drawn in; <see cref="RenderTheme.Light"/> until set.</summary>
    public RenderTheme Theme
    {
        get => R.Theme;
        set
        {
            if (value == R.Theme)
            {
                return;
            }

            // Pictures recorded with the old paints keep their own copies, so the paints can go.
            var old = R;
            R = new RenderResources(value);
            old.Dispose();
        }
    }

    public static DiagramRenderer Create(DiagramType type) => type switch
    {
        DiagramType.Class => new ClassDiagramRenderer(),
        DiagramType.Sequence => new SequenceDiagramRenderer(),
        _ when GraphRules.IsGraphDiagram(type) => new GraphDiagramRenderer(),
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, null),
    };

    private const float HandleSize = 9f;
    private const float TextBoxPadding = 4f;
    private const float NotePadding = 8f;
    private const float NoteFold = 12f;
    private const float AttachmentTolerance = 6f;

    /// <summary>
    /// Creates missing layout entries and updates sizes that depend on content (e.g. a class grows with its members).
    /// </summary>
    public void UpdateLayout(DiagramDocument document)
    {
        UpdateDiagramLayout(document);
        var i = 0;
        foreach (var element in document.Model.TextElements())
        {
            var (width, height) = element.DefaultSize;
            var layout = document.Layout.Find(element.Id) ?? document.Layout.Add(new ElementLayout
            {
                ElementId = element.Id,
                X = 40 + i * 20,
                Y = 40 + i * 20,
                Width = width,
                Height = height,
            });
            layout.Width = Math.Max(layout.Width, TextElement.MinWidth);
            layout.Height = Math.Max(layout.Height, TextElement.MinHeight);
            i++;
        }
    }

    /// <summary>
    /// Draws the diagram, then the note attachments, then the notes and text boxes above them, then the connector
    /// being dragged.
    /// </summary>
    public void Render(SKCanvas canvas, DiagramDocument document, RenderOptions options)
    {
        RenderDiagram(canvas, document, options);
        foreach (var attachment in document.Model.NoteAttachments)
        {
            if (TryGetAttachmentLine(document, attachment, out var start, out var end))
            {
                canvas.DrawLine(start, end, options.SelectedId == attachment.Id ? R.SelectedDashedLine : R.DashedLine);
            }
        }

        foreach (var element in document.Model.TextElements())
        {
            DrawTextElement(canvas, document, element, options);
        }

        DrawConnectorPreview(canvas, options);
    }

    /// <summary>Returns the top-most element part under <paramref name="point"/> (world coordinates).</summary>
    public HitInfo? HitTest(DiagramDocument document, SKPoint point, string? selectedId = null)
    {
        if (selectedId is not null && document.Model.FindTextElement(selectedId) is not null)
        {
            var handle = HandleRect(TextElementBounds(document, selectedId));
            handle.Inflate(3f, 3f);
            if (handle.Contains(point))
            {
                return new HitInfo(selectedId, HitPart.ResizeHandle);
            }
        }

        // Notes and text boxes are drawn above the diagram, so they are hit first; the last one is on top.
        foreach (var element in document.Model.TextElements().Reverse())
        {
            if (TextElementBounds(document, element.Id).Contains(point))
            {
                return new HitInfo(element.Id, HitPart.Text);
            }
        }

        foreach (var attachment in Enumerable.Reverse(document.Model.NoteAttachments))
        {
            if (TryGetAttachmentLine(document, attachment, out var start, out var end) &&
                Geometry.DistanceToSegment(point, start, end) <= AttachmentTolerance)
            {
                return new HitInfo(attachment.Id, HitPart.NoteAttachment);
            }
        }

        return HitTestDiagram(document, point, selectedId);
    }

    /// <summary>World rectangle in which to place an in-line text editor for the given element part.</summary>
    public SKRect? GetEditRect(DiagramDocument document, string elementId, HitPart part, int index = -1) =>
        document.Model.FindTextElement(elementId) is not null
            ? TextElementBounds(document, elementId)
            : GetDiagramEditRect(document, elementId, part, index);

    public SKRect GetContentBounds(DiagramDocument document)
    {
        var bounds = GetDiagramContentBounds(document);
        foreach (var element in document.Model.TextElements())
        {
            var rect = TextElementBounds(document, element.Id);
            bounds = bounds.IsEmpty ? rect : SKRect.Union(bounds, rect);
        }

        return bounds;
    }

    /// <summary>
    /// The drawn bounds of an element (e.g. a class grown to fit its members); <c>null</c> when it is not drawn, and an
    /// empty rectangle at a point for relationships and messages.
    /// </summary>
    public SKRect? GetElementBounds(DiagramDocument document, string elementId) =>
        document.Model.FindTextElement(elementId) is not null
            ? TextElementBounds(document, elementId)
            : GetDiagramElementBounds(document, elementId);

    /// <summary>
    /// The lines a note or text box shows: its text broken at line breaks and wrapped to the width available for
    /// text, as drawn with the element's font.
    /// </summary>
    public IReadOnlyList<string> GetTextLines(DiagramDocument document, string elementId)
    {
        if (document.Model.FindTextElement(elementId) is not { } element)
        {
            return [];
        }

        var font = R.Font(document.Layout.FontFor(elementId));
        return TextWrapper.Wrap(element.Text, font.Text, TextArea(element, TextElementBounds(document, elementId)).Width);
    }

    public void Dispose()
    {
        R.Dispose();
        GC.SuppressFinalize(this);
    }

    private protected abstract void UpdateDiagramLayout(DiagramDocument document);

    private protected abstract void RenderDiagram(SKCanvas canvas, DiagramDocument document, RenderOptions options);

    private protected abstract HitInfo? HitTestDiagram(DiagramDocument document, SKPoint point, string? selectedId);

    private protected abstract SKRect? GetDiagramEditRect(DiagramDocument document, string elementId, HitPart part, int index);

    private protected abstract SKRect GetDiagramContentBounds(DiagramDocument document);

    /// <summary>The bounds of the UML elements, without the notes and text boxes.</summary>
    internal SKRect GetUmlContentBounds(DiagramDocument document) => GetDiagramContentBounds(document);

    /// <summary>
    /// Where an attachment line ends on a UML element: its bounds, or an empty rectangle at a point for relationships
    /// and messages; <c>null</c> when the element is not drawn.
    /// </summary>
    private protected abstract SKRect? GetDiagramElementBounds(DiagramDocument document, string elementId);

    /// <summary>The dashed line of an attachment, from the edge of the note to the edge of (or a point on) its target.</summary>
    public bool TryGetAttachmentLine(DiagramDocument document, NoteAttachment attachment, out SKPoint start, out SKPoint end)
    {
        start = end = default;
        if (document.Model.FindNote(attachment.NoteId) is null || GetDiagramElementBounds(document, attachment.TargetId) is not { } target)
        {
            return false;
        }

        var note = TextElementBounds(document, attachment.NoteId);
        start = Geometry.ClipToRect(note, Geometry.Center(target));
        end = Geometry.ClipToRect(target, Geometry.Center(note));
        return true;
    }

    /// <summary>The resize handle at the bottom-right corner of a selected element.</summary>
    private protected static SKRect HandleRect(SKRect bounds) =>
        SKRect.Create(bounds.Right - HandleSize / 2f, bounds.Bottom - HandleSize / 2f, HandleSize, HandleSize);

    private static SKRect TextElementBounds(DiagramDocument document, string id) =>
        document.Layout.Find(id) is { } layout
            ? SKRect.Create(
                (float)layout.X,
                (float)layout.Y,
                (float)Math.Max(layout.Width, TextElement.MinWidth),
                (float)Math.Max(layout.Height, TextElement.MinHeight))
            : SKRect.Empty;

    /// <summary>The part of an element's bounds that text is laid out in; a note keeps its text clear of the fold.</summary>
    private static SKRect TextArea(TextElement element, SKRect bounds) => element is NoteElement
        ? new SKRect(bounds.Left + NotePadding, bounds.Top + NotePadding, bounds.Right - NoteFold - 2f, bounds.Bottom - NotePadding)
        : new SKRect(bounds.Left + TextBoxPadding, bounds.Top + TextBoxPadding, bounds.Right - TextBoxPadding, bounds.Bottom - TextBoxPadding);

    /// <summary>The outline of a note: a rectangle whose top-right corner is folded down.</summary>
    private static SKPath NotePath(SKRect bounds)
    {
        var path = new SKPath();
        path.MoveTo(bounds.Left, bounds.Top);
        path.LineTo(bounds.Right - NoteFold, bounds.Top);
        path.LineTo(bounds.Right, bounds.Top + NoteFold);
        path.LineTo(bounds.Right, bounds.Bottom);
        path.LineTo(bounds.Left, bounds.Bottom);
        path.Close();
        return path;
    }

    private void DrawTextElement(SKCanvas canvas, DiagramDocument document, TextElement element, RenderOptions options)
    {
        var bounds = TextElementBounds(document, element.Id);
        var selected = options.SelectedId == element.Id;
        var style = StyleOf(element, document.Layout.Find(element.Id)?.Style);
        var customFill = ShapeColors.CustomFill(style);
        var border = R.BorderOf(style);
        using var notePath = element is NoteElement ? NotePath(bounds) : null;

        if (notePath is not null)
        {
            using var shadow = new SKPath(notePath);
            shadow.Offset(2f, 3f);
            canvas.DrawPath(shadow, R.Shadow);
            canvas.DrawPath(notePath, R.FillOf(style, R.Theme.NoteFill));

            using var fold = new SKPath();
            fold.MoveTo(bounds.Right - NoteFold, bounds.Top);
            fold.LineTo(bounds.Right - NoteFold, bounds.Top + NoteFold);
            fold.LineTo(bounds.Right, bounds.Top + NoteFold);
            fold.Close();
            canvas.DrawPath(fold, customFill is { } fill ? R.FillOf(null, ShapeColors.Blend(fill, R.Theme.Border, 0.2f)) : R.NoteFold);
            canvas.DrawPath(fold, R.InnerLineOf(style));
        }
        else if (customFill is not null)
        {
            // Text boxes are transparent unless they are given a fill.
            canvas.DrawRect(bounds, R.FillOf(style, R.Theme.CanvasBackground));
        }

        var area = TextArea(element, bounds);
        var font = R.Font(document.Layout.FontFor(element.Id), customFill);
        var paint = font.Text;
        var lines = TextWrapper.Wrap(element.Text, paint, area.Width);
        var textHeight = lines.Count * font.LineHeight;
        var top = element.VerticalAlignment switch
        {
            VerticalTextAlignment.Middle => area.MidY - textHeight / 2f,
            VerticalTextAlignment.Bottom => area.Bottom - textHeight,
            _ => area.Top,
        };

        // Text that does not fit is cut off at the element's edges.
        canvas.Save();
        canvas.ClipRect(bounds);
        foreach (var line in lines)
        {
            var width = paint.MeasureText(line);
            var left = element.HorizontalAlignment switch
            {
                HorizontalTextAlignment.Center => area.MidX - width / 2f,
                HorizontalTextAlignment.Right => area.Right - width,
                _ => area.Left,
            };
            font.DrawText(canvas, line, left, top, paint);
            top += font.LineHeight;
        }

        canvas.Restore();

        if (notePath is not null)
        {
            canvas.DrawPath(notePath, selected ? R.SelectionBorder : border);
        }
        else if (selected)
        {
            canvas.DrawRect(bounds, R.SelectionBorder);
        }
        else if (style is { Border: not null } or { BorderWidth: not null })
        {
            // A text box has no border unless it is given one; then it is drawn, in exports too.
            canvas.DrawRect(bounds, border);
        }
        else if (options.ShowTextBoxOutlines)
        {
            canvas.DrawRect(bounds, R.TextBoxOutline);
        }

        if (selected)
        {
            canvas.DrawRect(HandleRect(bounds), R.HandleFill);
        }
    }

    private protected void DrawConnectorPreview(SKCanvas canvas, RenderOptions options)
    {
        if (options.ConnectorStart is { } start && options.ConnectorEnd is { } end)
        {
            canvas.DrawLine(start, end, R.ConnectorPreview);
            canvas.DrawCircle(end, 3f, R.HandleFill);
        }
    }

    /// <summary>
    /// Draws a connector's head at <paramref name="tip"/>, pointing along <paramref name="direction"/>, in the colour and
    /// thickness of <paramref name="stroke"/> (see <see cref="ConnectorStyle"/>).
    /// </summary>
    private protected void DrawHead(SKCanvas canvas, ConnectorHead head, SKPoint tip, SKPoint direction, SKPaint stroke)
    {
        switch (head)
        {
            case ConnectorHead.OpenArrow:
                DrawOpenArrow(canvas, tip, direction, stroke);
                break;
            case ConnectorHead.FilledArrow:
                R.ArrowFill.Color = stroke.Color;
                DrawFilledTriangle(canvas, tip, direction, R.ArrowFill);
                break;
            case ConnectorHead.HollowTriangle:
                DrawHollowTriangle(canvas, tip, direction, R.Fill, stroke);
                break;
            case ConnectorHead.HollowDiamond:
                DrawDiamond(canvas, tip, direction, R.Fill, stroke);
                break;
            case ConnectorHead.FilledDiamond:
                R.ArrowFill.Color = stroke.Color;
                DrawDiamond(canvas, tip, direction, R.ArrowFill, stroke);
                break;
        }
    }

    private protected static void DrawHollowTriangle(SKCanvas canvas, SKPoint tip, SKPoint direction, SKPaint fill, SKPaint stroke)
    {
        using var path = ArrowPath(tip, direction, 14f, 8f, close: true);
        canvas.DrawPath(path, fill);
        canvas.DrawPath(path, stroke);
    }

    private protected static void DrawFilledTriangle(SKCanvas canvas, SKPoint tip, SKPoint direction, SKPaint fill)
    {
        using var path = ArrowPath(tip, direction, 11f, 5f, close: true);
        canvas.DrawPath(path, fill);
    }

    private protected static void DrawOpenArrow(SKCanvas canvas, SKPoint tip, SKPoint direction, SKPaint stroke)
    {
        using var path = ArrowPath(tip, direction, 11f, 6f, close: false);
        canvas.DrawPath(path, stroke);
    }

    private protected static void DrawDiamond(SKCanvas canvas, SKPoint tip, SKPoint direction, SKPaint fill, SKPaint stroke)
    {
        var d = Geometry.Normalize(direction);
        var n = Geometry.Perpendicular(d);
        var middle = Geometry.Add(tip, Geometry.Scale(d, -9f));
        using var path = new SKPath();
        path.MoveTo(tip);
        path.LineTo(Geometry.Add(middle, Geometry.Scale(n, 6f)));
        path.LineTo(Geometry.Add(tip, Geometry.Scale(d, -18f)));
        path.LineTo(Geometry.Add(middle, Geometry.Scale(n, -6f)));
        path.Close();
        canvas.DrawPath(path, fill);
        canvas.DrawPath(path, stroke);
    }

    /// <summary>Arrow head whose tip is at <paramref name="tip"/>, pointing along <paramref name="direction"/>.</summary>
    private static SKPath ArrowPath(SKPoint tip, SKPoint direction, float length, float halfWidth, bool close)
    {
        var d = Geometry.Normalize(direction);
        var n = Geometry.Perpendicular(d);
        var basePoint = Geometry.Add(tip, Geometry.Scale(d, -length));
        var path = new SKPath();
        path.MoveTo(Geometry.Add(basePoint, Geometry.Scale(n, halfWidth)));
        path.LineTo(tip);
        path.LineTo(Geometry.Add(basePoint, Geometry.Scale(n, -halfWidth)));
        if (close)
        {
            path.Close();
        }

        return path;
    }
}
