using SkiaSharp;
using UmlWizard.Core.Model;
using UmlWizard.Core.Notation;

namespace UmlWizard.Core.Rendering;

public sealed class ClassDiagramRenderer : DiagramRenderer
{
    private const float PadX = 10f;
    private const float PadY = 6f;
    private const float MinWidth = 120f;
    private const float EdgeTolerance = 6f;

    private protected override void UpdateDiagramLayout(DiagramDocument document)
    {
        var model = (ClassDiagramModel)document.Model;
        for (var i = 0; i < model.Classes.Count; i++)
        {
            var layout = GetLayout(document.Layout, model.Classes[i], i);
            var bounds = Measure(model.Classes[i], layout, document.Layout.Font).Bounds;
            layout.Width = bounds.Width;
            layout.Height = bounds.Height;
        }
    }

    private protected override void RenderDiagram(SKCanvas canvas, DiagramDocument document, RenderOptions options)
    {
        var model = (ClassDiagramModel)document.Model;
        var geometries = MeasureAll(document, model);

        foreach (var relationship in model.Relationships)
        {
            if (TryGetEndpoints(relationship, geometries, out var start, out var end))
            {
                DrawRelationship(canvas, relationship, start, end, FontOf(document, relationship), document.Layout.Find(relationship.Id)?.Line, options.SelectedId == relationship.Id);
            }
        }

        foreach (var cls in model.Classes)
        {
            DrawClass(canvas, cls, geometries[cls.Id], StyleOf(cls, document.Layout.Find(cls.Id)?.Style), options.SelectedId == cls.Id, options.HighlightId == cls.Id);
        }
    }

    private protected override HitInfo? HitTestDiagram(DiagramDocument document, SKPoint point, string? selectedId)
    {
        var model = (ClassDiagramModel)document.Model;
        var geometries = MeasureAll(document, model);

        if (selectedId is not null && geometries.TryGetValue(selectedId, out var selected))
        {
            var handle = HandleRect(selected.Bounds);
            handle.Inflate(3f, 3f);
            if (handle.Contains(point))
            {
                return new HitInfo(selectedId, HitPart.ResizeHandle);
            }
        }

        for (var i = model.Classes.Count - 1; i >= 0; i--)
        {
            var cls = model.Classes[i];
            var g = geometries[cls.Id];
            if (!g.Bounds.Contains(point))
            {
                continue;
            }

            if (g.Header.Contains(point))
            {
                return new HitInfo(cls.Id, HitPart.ClassHeader);
            }

            for (var row = 0; row < g.AttributeRows.Count; row++)
            {
                if (g.AttributeRows[row].Contains(point))
                {
                    return new HitInfo(cls.Id, HitPart.ClassAttribute, row);
                }
            }

            if (g.AttributesArea.Contains(point))
            {
                return new HitInfo(cls.Id, HitPart.ClassAttributesArea);
            }

            for (var row = 0; row < g.OperationRows.Count; row++)
            {
                if (g.OperationRows[row].Contains(point))
                {
                    return new HitInfo(cls.Id, HitPart.ClassOperation, row);
                }
            }

            return new HitInfo(cls.Id, HitPart.ClassOperationsArea);
        }

        for (var i = model.Relationships.Count - 1; i >= 0; i--)
        {
            var relationship = model.Relationships[i];
            if (!TryGetEndpoints(relationship, geometries, out var start, out var end))
            {
                continue;
            }

            if (Geometry.DistanceToSegment(point, start, end) <= EdgeTolerance ||
                (!string.IsNullOrEmpty(relationship.Label) && LabelRect(relationship.Label, start, end, FontOf(document, relationship)).Contains(point)))
            {
                return new HitInfo(relationship.Id, HitPart.Relationship);
            }
        }

        return null;
    }

    private protected override SKRect? GetDiagramEditRect(DiagramDocument document, string elementId, HitPart part, int index)
    {
        var model = (ClassDiagramModel)document.Model;
        var geometries = MeasureAll(document, model);

        if (geometries.TryGetValue(elementId, out var g))
        {
            return part switch
            {
                HitPart.ClassHeader => SKRect.Create(g.Bounds.Left + 4f, g.Header.Bottom - PadY - g.Font.LineHeight, g.Bounds.Width - 8f, g.Font.LineHeight),
                HitPart.ClassAttribute when index >= 0 && index < g.AttributeRows.Count => g.AttributeRows[index],
                HitPart.ClassOperation when index >= 0 && index < g.OperationRows.Count => g.OperationRows[index],
                _ => null,
            };
        }

        var relationship = model.Relationships.Find(r => r.Id == elementId);
        if (relationship is not null && TryGetEndpoints(relationship, geometries, out var start, out var end))
        {
            var rect = LabelRect(relationship.Label ?? string.Empty, start, end, FontOf(document, relationship));
            return rect.Width < 140f ? SKRect.Create(rect.MidX - 70f, rect.Top, 140f, rect.Height) : rect;
        }

        return null;
    }

    private protected override SKRect? GetDiagramElementBounds(DiagramDocument document, string elementId)
    {
        var model = (ClassDiagramModel)document.Model;
        var geometries = MeasureAll(document, model);
        if (geometries.TryGetValue(elementId, out var g))
        {
            return g.Bounds;
        }

        // A relationship is attached to at the middle of its line.
        var relationship = model.Relationships.Find(r => r.Id == elementId);
        return relationship is not null && TryGetEndpoints(relationship, geometries, out var start, out var end)
            ? SKRect.Create((start.X + end.X) / 2f, (start.Y + end.Y) / 2f, 0, 0)
            : null;
    }

    private protected override SKRect GetDiagramContentBounds(DiagramDocument document)
    {
        var model = (ClassDiagramModel)document.Model;
        var bounds = SKRect.Empty;
        foreach (var g in MeasureAll(document, model).Values)
        {
            bounds = bounds.IsEmpty ? g.Bounds : SKRect.Union(bounds, g.Bounds);
        }

        return bounds;
    }

    internal ClassGeometry Measure(UmlClass cls, ElementLayout layout, DiagramFont? diagramFont)
    {
        var font = R.Font(layout.Font ?? diagramFont, ShapeColors.CustomFill(StyleOf(cls, layout.Style)));
        var header = new List<TextLine>();
        var kindStereotype = cls.Kind switch
        {
            ClassifierKind.Interface => "«interface»",
            ClassifierKind.Enumeration => "«enumeration»",
            _ => null,
        };
        if (kindStereotype is not null)
        {
            header.Add(new TextLine(kindStereotype, font.Text, false));
        }

        if (!string.IsNullOrWhiteSpace(cls.Stereotype))
        {
            header.Add(new TextLine($"«{cls.Stereotype.Trim()}»", font.Text, false));
        }

        header.Add(new TextLine(
            string.IsNullOrEmpty(cls.Name) ? " " : cls.Name,
            cls.Kind == ClassifierKind.AbstractClass ? font.BoldItalicText : font.BoldText,
            false));

        var attributes = cls.Attributes
            .Select(a => new TextLine(
                cls.Kind == ClassifierKind.Enumeration ? a.Name : UmlNotation.Format(a),
                font.Text,
                a.IsStatic))
            .ToList();
        var operations = cls.Operations
            .Select(o => new TextLine(UmlNotation.Format(o), o.IsAbstract ? font.ItalicText : font.Text, o.IsStatic, EndpointOf(o)))
            .ToList();

        var required = MinWidth;
        foreach (var line in header.Concat(attributes).Concat(operations))
        {
            required = MathF.Max(required, MathF.Ceiling(PrefixWidth(line, font) + line.Paint.MeasureText(line.Text) + 2 * PadX));
        }

        var width = MathF.Max((float)layout.Width, required);
        var x = (float)layout.X;
        var y = (float)layout.Y;
        var lineHeight = font.LineHeight;

        var headerRect = SKRect.Create(x, y, width, header.Count * lineHeight + 2 * PadY);
        var attributesRect = SKRect.Create(x, headerRect.Bottom, width, attributes.Count * lineHeight + 2 * PadY);
        var operationsRect = SKRect.Create(x, attributesRect.Bottom, width, operations.Count * lineHeight + 2 * PadY);

        return new ClassGeometry(
            SKRect.Create(x, y, width, operationsRect.Bottom - y),
            font,
            headerRect,
            attributesRect,
            operationsRect,
            header,
            attributes,
            operations,
            Rows(attributesRect, attributes.Count),
            Rows(operationsRect, operations.Count));

        List<SKRect> Rows(SKRect area, int count) => Enumerable.Range(0, count)
            .Select(i => SKRect.Create(area.Left + 4f, area.Top + PadY + i * lineHeight, area.Width - 8f, lineHeight))
            .ToList();
    }

    private Dictionary<string, ClassGeometry> MeasureAll(DiagramDocument document, ClassDiagramModel model)
    {
        var geometries = new Dictionary<string, ClassGeometry>(model.Classes.Count);
        for (var i = 0; i < model.Classes.Count; i++)
        {
            var cls = model.Classes[i];
            geometries[cls.Id] = Measure(cls, GetLayout(document.Layout, cls, i), document.Layout.Font);
        }

        return geometries;
    }

    private static ElementLayout GetLayout(DiagramLayout layout, UmlClass cls, int index) =>
        layout.Find(cls.Id) ?? layout.Add(new ElementLayout
        {
            ElementId = cls.Id,
            X = 40 + index % 5 * 220,
            Y = 40 + index / 5 * 220,
        });

    private void DrawClass(SKCanvas canvas, UmlClass cls, ClassGeometry g, ShapeStyle? style, bool selected, bool highlighted)
    {
        var bounds = g.Bounds;
        var shadow = bounds;
        shadow.Offset(2f, 3f);
        canvas.DrawRect(shadow, R.Shadow);
        // By default the header is tinted by kind and the compartments are plain; a custom fill colours the whole class.
        var border = R.BorderOf(style);
        canvas.DrawRect(bounds, R.FillOf(style, R.Theme.NodeFill));
        canvas.DrawRect(g.Header, R.FillOf(style, R.Theme.ClassFill(cls.Kind)));

        var top = g.Header.Top + PadY;
        foreach (var line in g.HeaderLines)
        {
            g.Font.DrawCenteredText(canvas, line.Text, bounds.MidX, top, line.Paint);
            top += g.Font.LineHeight;
        }

        var divider = R.InnerLineOf(style);
        canvas.DrawLine(bounds.Left, g.Header.Bottom, bounds.Right, g.Header.Bottom, divider);
        canvas.DrawLine(bounds.Left, g.AttributesArea.Bottom, bounds.Right, g.AttributesArea.Bottom, divider);

        for (var i = 0; i < g.AttributeLines.Count; i++)
        {
            var line = g.AttributeLines[i];
            g.Font.DrawText(canvas, line.Text, bounds.Left + PadX, g.AttributeRows[i].Top, line.Paint, line.Underline);
        }

        for (var i = 0; i < g.OperationLines.Count; i++)
        {
            var line = g.OperationLines[i];
            var left = bounds.Left + PadX;
            if (line.Prefix is not null)
            {
                g.Font.DrawText(canvas, line.Prefix, left, g.OperationRows[i].Top, g.Font.SmallText);
                left += PrefixWidth(line, g.Font);
            }

            g.Font.DrawText(canvas, line.Text, left, g.OperationRows[i].Top, line.Paint, line.Underline);
        }

        canvas.DrawRect(bounds, selected ? R.SelectionBorder : highlighted ? R.HighlightBorder : border);
        if (selected)
        {
            canvas.DrawRect(HandleRect(bounds), R.HandleFill);
        }
    }

    private void DrawRelationship(
        SKCanvas canvas, UmlRelationship relationship, SKPoint start, SKPoint end, RenderResources.FontSet font, LineStyle? style, bool selected)
    {
        var connector = ConnectorStyle.Of(relationship.Kind);
        var (solid, dash) = R.LinesOf(style, selected);
        canvas.DrawLine(start, end, connector.Dashed ? dash : solid);

        var forward = new SKPoint(end.X - start.X, end.Y - start.Y);
        var backward = new SKPoint(-forward.X, -forward.Y);
        DrawHead(canvas, connector.TargetHead, end, forward, solid);
        DrawHead(canvas, connector.SourceHead, start, backward, solid);

        if (!string.IsNullOrEmpty(relationship.Label))
        {
            var rect = LabelRect(relationship.Label, start, end, font);
            canvas.DrawRect(rect, R.LabelBackground);
            font.DrawCenteredText(canvas, relationship.Label, rect.MidX, rect.Top, font.Text);
        }

        DrawMultiplicity(canvas, relationship.SourceMultiplicity, start, forward, font);
        DrawMultiplicity(canvas, relationship.TargetMultiplicity, end, backward, font);
    }

    private static void DrawMultiplicity(SKCanvas canvas, string? text, SKPoint anchor, SKPoint direction, RenderResources.FontSet font)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        var d = Geometry.Normalize(direction);
        var n = Geometry.Perpendicular(d);
        var position = Geometry.Add(anchor, Geometry.Add(Geometry.Scale(d, 16f), Geometry.Scale(n, 11f)));
        font.DrawCenteredText(canvas, text, position.X, position.Y - font.LineHeight / 2f, font.SmallText);
    }

    private static SKRect LabelRect(string label, SKPoint start, SKPoint end, RenderResources.FontSet font)
    {
        var width = MathF.Max(40f, font.Text.MeasureText(label) + 12f);
        var mid = new SKPoint((start.X + end.X) / 2f, (start.Y + end.Y) / 2f);
        return SKRect.Create(mid.X - width / 2f, mid.Y - font.LineHeight - 3f, width, font.LineHeight);
    }

    /// <summary>How long a relationship's line must be for its label and its multiplicities to fit side by side.</summary>
    internal float TextLength(DiagramDocument document, UmlRelationship relationship)
    {
        var font = FontOf(document, relationship);
        var label = string.IsNullOrEmpty(relationship.Label) ? 0f : MathF.Max(40f, font.Text.MeasureText(relationship.Label) + 12f);
        float End(string? multiplicity) => string.IsNullOrWhiteSpace(multiplicity) ? 0f : 16f + font.SmallText.MeasureText(multiplicity) / 2f + 6f;
        return label + 2 * MathF.Max(End(relationship.SourceMultiplicity), End(relationship.TargetMultiplicity));
    }

    private RenderResources.FontSet FontOf(DiagramDocument document, UmlRelationship relationship) =>
        R.Font(document.Layout.FontFor(relationship.Id));

    private static bool TryGetEndpoints(
        UmlRelationship relationship,
        Dictionary<string, ClassGeometry> geometries,
        out SKPoint start,
        out SKPoint end)
    {
        if (relationship.SourceId == relationship.TargetId ||
            !geometries.TryGetValue(relationship.SourceId, out var source) ||
            !geometries.TryGetValue(relationship.TargetId, out var target))
        {
            start = end = default;
            return false;
        }

        start = Geometry.ClipToRect(source.Bounds, Geometry.Center(target.Bounds));
        end = Geometry.ClipToRect(target.Bounds, Geometry.Center(source.Bounds));
        return true;
    }

    /// <summary>
    /// The «METHOD path» of a web API endpoint, drawn small before its signature; <c>null</c> for other operations.
    /// </summary>
    private static string? EndpointOf(UmlOperation operation) =>
        string.IsNullOrWhiteSpace(operation.HttpMethod)
            ? null
            : $"«{operation.HttpMethod.Trim().ToUpperInvariant()} {operation.Path?.Trim()}".TrimEnd() + "»";

    private static float PrefixWidth(TextLine line, RenderResources.FontSet font) =>
        line.Prefix is null ? 0f : font.SmallText.MeasureText(line.Prefix) + 6f;

    /// <param name="Prefix">Text drawn small before <paramref name="Text"/>, e.g. an endpoint’s «GET /orders».</param>
    internal readonly record struct TextLine(string Text, SKPaint Paint, bool Underline, string? Prefix = null);

    internal sealed record ClassGeometry(
        SKRect Bounds,
        RenderResources.FontSet Font,
        SKRect Header,
        SKRect AttributesArea,
        SKRect OperationsArea,
        IReadOnlyList<TextLine> HeaderLines,
        IReadOnlyList<TextLine> AttributeLines,
        IReadOnlyList<TextLine> OperationLines,
        IReadOnlyList<SKRect> AttributeRows,
        IReadOnlyList<SKRect> OperationRows);
}
