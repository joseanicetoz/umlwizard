using SkiaSharp;
using UmlWizard.Core.Model;
using UmlWizard.Core.Notation;

namespace UmlWizard.Core.Rendering;

public sealed class SequenceDiagramRenderer : DiagramRenderer
{
    public const float DefaultTop = 20f;
    public const float ObjectHeadHeight = 40f;
    public const float ActorHeadHeight = 76f;

    private const float HeadMinWidth = 100f;
    private const float MinLifelineLength = 300f;
    private const float BottomMargin = 60f;
    internal const float SelfLoopWidth = 40f;
    public const float SelfLoopHeight = 24f;
    private const float MessageTolerance = 5f;
    private const float BodyTolerance = 8f;

    private protected override void UpdateDiagramLayout(DiagramDocument document)
    {
        var model = (SequenceDiagramModel)document.Model;
        for (var i = 0; i < model.Lifelines.Count; i++)
        {
            var lifeline = model.Lifelines[i];
            var layout = GetLifelineLayout(document.Layout, lifeline, i);
            var head = MeasureHead(lifeline, layout, R.Font(layout.Font ?? document.Layout.Font));
            layout.Width = head.Width;
            layout.Height = head.Height;
        }

        var firstMessageY = model.Lifelines.Count == 0
            ? DefaultTop + ActorHeadHeight + 30
            : model.Lifelines.Max(l => HeadBottom(document.Layout, l)) + 30;
        for (var i = 0; i < model.Messages.Count; i++)
        {
            var message = model.Messages[i];
            if (document.Layout.Find(message.Id) is null)
            {
                document.Layout.Add(new ElementLayout { ElementId = message.Id, Y = firstMessageY + i * 40 });
            }
        }
    }

    private protected override void RenderDiagram(SKCanvas canvas, DiagramDocument document, RenderOptions options)
    {
        var scene = BuildScene(document);

        foreach (var lifeline in scene.Lifelines.Values)
        {
            canvas.DrawLine(lifeline.CenterX, lifeline.Head.Bottom, lifeline.CenterX, scene.Bottom, R.LifelineLine);
        }

        foreach (var lifeline in scene.Lifelines.Values)
        {
            DrawLifeline(canvas, lifeline, options.SelectedId == lifeline.Model.Id, options.HighlightId == lifeline.Model.Id);
        }

        foreach (var message in scene.Messages)
        {
            DrawMessage(canvas, message, options.SelectedId == message.Model.Id);
        }
    }

    private protected override HitInfo? HitTestDiagram(DiagramDocument document, SKPoint point, string? selectedId)
    {
        var scene = BuildScene(document);

        foreach (var lifeline in scene.Lifelines.Values.Reverse())
        {
            if (lifeline.Head.Contains(point))
            {
                return new HitInfo(lifeline.Model.Id, HitPart.LifelineHead);
            }
        }

        foreach (var message in Enumerable.Reverse(scene.Messages))
        {
            if (message.HitRect.Contains(point) || message.TextRect.Contains(point))
            {
                return new HitInfo(message.Model.Id, HitPart.Message);
            }
        }

        foreach (var lifeline in scene.Lifelines.Values.Reverse())
        {
            if (MathF.Abs(point.X - lifeline.CenterX) <= BodyTolerance &&
                point.Y >= lifeline.Head.Bottom && point.Y <= scene.Bottom)
            {
                return new HitInfo(lifeline.Model.Id, HitPart.LifelineBody);
            }
        }

        return null;
    }

    private protected override SKRect? GetDiagramEditRect(DiagramDocument document, string elementId, HitPart part, int index)
    {
        var scene = BuildScene(document);
        if (scene.Lifelines.TryGetValue(elementId, out var lifeline))
        {
            return lifeline.LabelRect;
        }

        var message = scene.Messages.Find(m => m.Model.Id == elementId);
        if (message is null)
        {
            return null;
        }

        var rect = message.TextRect;
        return rect.Width < 140f ? SKRect.Create(message.IsSelf ? rect.Left : rect.MidX - 70f, rect.Top, 140f, rect.Height) : rect;
    }

    private protected override SKRect? GetDiagramElementBounds(DiagramDocument document, string elementId)
    {
        var scene = BuildScene(document);
        if (scene.Lifelines.TryGetValue(elementId, out var lifeline))
        {
            return lifeline.Head;
        }

        // A message is attached to at the middle of its arrow.
        var message = scene.Messages.Find(m => m.Model.Id == elementId);
        return message is null ? null : SKRect.Create(message.HitRect.MidX, message.Y, 0, 0);
    }

    private protected override SKRect GetDiagramContentBounds(DiagramDocument document)
    {
        var scene = BuildScene(document);
        var bounds = SKRect.Empty;
        foreach (var lifeline in scene.Lifelines.Values)
        {
            var rect = SKRect.Create(lifeline.Head.Left, lifeline.Head.Top, lifeline.Head.Width, scene.Bottom - lifeline.Head.Top);
            bounds = bounds.IsEmpty ? rect : SKRect.Union(bounds, rect);
        }

        foreach (var message in scene.Messages)
        {
            bounds = SKRect.Union(bounds, SKRect.Union(message.HitRect, message.TextRect));
        }

        return bounds;
    }

    /// <summary>The size of each message's label (number, keyword and text) as drawn.</summary>
    internal Dictionary<string, SKSize> MessageLabelSizes(DiagramDocument document) =>
        BuildScene(document).Messages.ToDictionary(m => m.Model.Id, m => m.TextRect.Size);

    /// <summary>Lowest Y a message between the given lifelines may have.</summary>
    public static double MinimumMessageY(DiagramLayout layout, params Lifeline?[] lifelines) =>
        lifelines.Where(l => l is not null).Select(l => HeadBottom(layout, l!)).DefaultIfEmpty(DefaultTop + ObjectHeadHeight).Max() + 20;

    private static double HeadBottom(DiagramLayout layout, Lifeline lifeline)
    {
        var l = layout.Find(lifeline.Id);
        var height = l is { Height: > 0 } ? l.Height : lifeline.Kind == LifelineKind.Actor ? ActorHeadHeight : ObjectHeadHeight;
        return (l?.Y ?? DefaultTop) + height;
    }

    private Scene BuildScene(DiagramDocument document)
    {
        var model = (SequenceDiagramModel)document.Model;
        var lifelines = new Dictionary<string, LifelineGeometry>(model.Lifelines.Count);
        for (var i = 0; i < model.Lifelines.Count; i++)
        {
            var lifeline = model.Lifelines[i];
            var layout = GetLifelineLayout(document.Layout, lifeline, i);
            // An object's name is drawn on its head; an actor's below the figure, on the canvas.
            var font = R.Font(layout.Font ?? document.Layout.Font, lifeline.Kind == LifelineKind.Actor ? null : ShapeColors.CustomFill(StyleOf(lifeline, layout.Style)));
            var head = MeasureHead(lifeline, layout, font);
            var labelRect = lifeline.Kind == LifelineKind.Actor
                ? SKRect.Create(head.Left, head.Bottom - font.LineHeight - 2f, head.Width, font.LineHeight)
                : SKRect.Create(head.Left + 4f, head.MidY - font.LineHeight / 2f, head.Width - 8f, font.LineHeight);
            lifelines[lifeline.Id] = new LifelineGeometry(lifeline, head, head.MidX, labelRect, font, StyleOf(lifeline, layout.Style));
        }

        var ordered = model.Messages
            .Select(m => (Message: m, Layout: document.Layout.Find(m.Id)))
            .Where(x => x.Layout is not null)
            .OrderBy(x => x.Layout!.Y)
            .ToList();

        var messages = new List<MessageGeometry>(ordered.Count);
        foreach (var (message, layout) in ordered)
        {
            if (!lifelines.TryGetValue(message.SourceId, out var source) ||
                !lifelines.TryGetValue(message.TargetId, out var target))
            {
                continue;
            }

            var y = (float)layout!.Y;
            var keyword = ConnectorStyle.Of(message.Kind).Keyword;
            var label = $"{messages.Count + 1}: {(keyword is null ? string.Empty : keyword + " ")}{message.Text}";
            var font = R.Font(layout.Font ?? document.Layout.Font);
            var textWidth = font.Text.MeasureText(label) + 12f;
            var isSelf = source.Model.Id == target.Model.Id;
            SKRect hitRect, textRect;
            if (isSelf)
            {
                hitRect = SKRect.Create(source.CenterX, y - MessageTolerance, SelfLoopWidth + MessageTolerance, SelfLoopHeight + 2 * MessageTolerance);
                textRect = SKRect.Create(source.CenterX + SelfLoopWidth + 4f, y + SelfLoopHeight / 2f - font.LineHeight / 2f, textWidth, font.LineHeight);
            }
            else
            {
                var left = MathF.Min(source.CenterX, target.CenterX);
                var right = MathF.Max(source.CenterX, target.CenterX);
                hitRect = SKRect.Create(left, y - MessageTolerance, right - left, 2 * MessageTolerance);
                textRect = SKRect.Create((left + right) / 2f - textWidth / 2f, y - font.LineHeight - 3f, textWidth, font.LineHeight);
            }

            messages.Add(new MessageGeometry(message, y, source.CenterX, target.CenterX, isSelf, label, hitRect, textRect, font, layout.Line));
        }

        var headBottom = lifelines.Values.Select(l => l.Head.Bottom).DefaultIfEmpty(DefaultTop).Max();
        var lastMessage = messages.Select(m => m.HitRect.Bottom).DefaultIfEmpty(0f).Max();
        var bottom = MathF.Max(headBottom + MinLifelineLength, lastMessage + BottomMargin);
        return new Scene(lifelines, messages, bottom);
    }

    private static SKRect MeasureHead(Lifeline lifeline, ElementLayout layout, RenderResources.FontSet font)
    {
        var text = LabelOf(lifeline);
        var required = MathF.Max(HeadMinWidth, MathF.Ceiling(font.Text.MeasureText(text) + 20f));
        var width = MathF.Max((float)layout.Width, required);

        // The default heights fit an 18 px text row; larger fonts make the head taller.
        var height = lifeline.Kind == LifelineKind.Actor
            ? ActorHeadHeight - 18f + font.LineHeight
            : MathF.Max(ObjectHeadHeight, font.LineHeight + 22f);
        return SKRect.Create((float)layout.X, (float)layout.Y, width, height);
    }

    private static ElementLayout GetLifelineLayout(DiagramLayout layout, Lifeline lifeline, int index) =>
        layout.Find(lifeline.Id) ?? layout.Add(new ElementLayout
        {
            ElementId = lifeline.Id,
            X = 40 + index * 180,
            Y = DefaultTop,
        });

    private static string LabelOf(Lifeline lifeline)
    {
        var text = UmlNotation.FormatLifeline(lifeline);
        return string.IsNullOrWhiteSpace(text) ? " " : text;
    }

    private void DrawLifeline(SKCanvas canvas, LifelineGeometry g, bool selected, bool highlighted)
    {
        var head = g.Head;
        var label = LabelOf(g.Model);
        var outline = R.BorderOf(g.Style);
        var border = selected ? R.SelectionBorder : highlighted ? R.HighlightBorder : outline;

        if (g.Model.Kind == LifelineKind.Actor)
        {
            // The fill is the head's; the border colour and thickness are the figure's lines.
            var cx = head.MidX;
            var top = head.Top + 4f;
            canvas.DrawCircle(cx, top + 8f, 8f, R.FillOf(g.Style, R.Theme.NodeFill));
            canvas.DrawCircle(cx, top + 8f, 8f, outline);
            canvas.DrawLine(cx, top + 16f, cx, top + 36f, outline);
            canvas.DrawLine(cx - 13f, top + 23f, cx + 13f, top + 23f, outline);
            canvas.DrawLine(cx, top + 36f, cx - 11f, top + 50f, outline);
            canvas.DrawLine(cx, top + 36f, cx + 11f, top + 50f, outline);
            g.Font.DrawCenteredText(canvas, label, cx, g.LabelRect.Top, g.Font.BoldText);
            if (selected || highlighted)
            {
                canvas.DrawRoundRect(head, 4f, 4f, border);
            }

            return;
        }

        var shadow = head;
        shadow.Offset(2f, 3f);
        canvas.DrawRect(shadow, R.Shadow);
        canvas.DrawRect(head, R.FillOf(g.Style, R.Theme.LifelineHead));
        canvas.DrawRect(head, border);
        g.Font.DrawCenteredText(canvas, label, head.MidX, g.LabelRect.Top, g.Font.Text, underline: !string.IsNullOrWhiteSpace(g.Model.Name));
    }

    private void DrawMessage(SKCanvas canvas, MessageGeometry g, bool selected)
    {
        var connector = ConnectorStyle.Of(g.Model.Kind);
        var (solid, dash) = R.LinesOf(g.Line, selected);
        var linePaint = connector.Dashed ? dash : solid;

        SKPoint tip, direction;
        if (g.IsSelf)
        {
            using var path = new SKPath();
            path.MoveTo(g.X1, g.Y);
            path.LineTo(g.X1 + SelfLoopWidth, g.Y);
            path.LineTo(g.X1 + SelfLoopWidth, g.Y + SelfLoopHeight);
            path.LineTo(g.X1, g.Y + SelfLoopHeight);
            canvas.DrawPath(path, linePaint);
            tip = new SKPoint(g.X1, g.Y + SelfLoopHeight);
            direction = new SKPoint(-1, 0);
            g.Font.DrawText(canvas, g.Label, g.TextRect.Left + 6f, g.TextRect.Top, g.Font.Text);
        }
        else
        {
            canvas.DrawLine(g.X1, g.Y, g.X2, g.Y, linePaint);
            tip = new SKPoint(g.X2, g.Y);
            direction = new SKPoint(g.X2 - g.X1, 0);
            canvas.DrawRect(g.TextRect, R.LabelBackground);
            g.Font.DrawCenteredText(canvas, g.Label, g.TextRect.MidX, g.TextRect.Top, g.Font.Text);
        }

        DrawHead(canvas, connector.TargetHead, tip, direction, solid);
    }

    private sealed record LifelineGeometry(Lifeline Model, SKRect Head, float CenterX, SKRect LabelRect, RenderResources.FontSet Font, ShapeStyle? Style);

    private sealed record MessageGeometry(
        SequenceMessage Model,
        float Y,
        float X1,
        float X2,
        bool IsSelf,
        string Label,
        SKRect HitRect,
        SKRect TextRect,
        RenderResources.FontSet Font,
        LineStyle? Line);

    private sealed record Scene(Dictionary<string, LifelineGeometry> Lifelines, List<MessageGeometry> Messages, float Bottom);
}
