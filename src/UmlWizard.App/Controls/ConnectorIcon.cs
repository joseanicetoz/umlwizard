using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;
using UmlWizard.Core.Rendering;

namespace UmlWizard.App.Controls;

/// <summary>
/// The icon of a connector tool, drawn from its <see cref="ConnectorStyle"/>: a line from left to right, dashed when the
/// connector is, with its heads as the canvas draws them (arrow heads at the target on the right, diamonds at the source
/// on the left) and the first letter of its keyword above it (<c>«i»</c> for <c>«include»</c>), so that connectors that
/// only differ by their keyword can be told apart. Drawn in the foreground colour, so it follows the theme and shows
/// when it is disabled; vector, so it is sharp at every scale.
/// </summary>
public sealed class ConnectorIcon : Control
{
    public static readonly StyledProperty<ConnectorStyle?> ConnectorProperty =
        AvaloniaProperty.Register<ConnectorIcon, ConnectorStyle?>(nameof(Connector));

    public static readonly StyledProperty<IBrush?> ForegroundProperty = TextElement.ForegroundProperty.AddOwner<ConnectorIcon>();

    private const double IconWidth = 30;
    private const double IconHeight = 18;
    private const double Thickness = 1.3;

    static ConnectorIcon() => AffectsRender<ConnectorIcon>(ConnectorProperty, ForegroundProperty);

    public ConnectorStyle? Connector
    {
        get => GetValue(ConnectorProperty);
        set => SetValue(ConnectorProperty, value);
    }

    public IBrush? Foreground
    {
        get => GetValue(ForegroundProperty);
        set => SetValue(ForegroundProperty, value);
    }

    protected override Size MeasureOverride(Size availableSize) => new(IconWidth, IconHeight);

    public override void Render(DrawingContext context)
    {
        if (Connector is not { } connector)
        {
            return;
        }

        var brush = Foreground ?? Brushes.Black;
        var pen = new Pen(brush, Thickness, lineCap: PenLineCap.Flat);
        var left = 1.0;
        var right = Bounds.Width - 1;

        // The line sits low when a keyword goes above it, else in the middle.
        var y = Math.Round(connector.Keyword is null ? Bounds.Height / 2 : Bounds.Height - 4) + 0.5;
        var lineStart = left + HeadLength(connector.SourceHead);
        var lineEnd = right - HeadLength(connector.TargetHead);
        var line = connector.Dashed ? new Pen(brush, Thickness, new DashStyle([2.5, 2], 0), PenLineCap.Flat) : pen;
        context.DrawLine(line, new Point(lineStart, y), new Point(lineEnd, y));

        DrawHead(context, connector.TargetHead, new Point(right, y), direction: 1, brush, pen);
        DrawHead(context, connector.SourceHead, new Point(left, y), direction: -1, brush, pen);

        if (connector.Keyword is { Length: > 0 } keyword)
        {
            var letter = keyword.Trim('«', '»', ' ');
            var text = new FormattedText(
                $"«{(letter.Length > 0 ? letter[0] : ' ')}»", CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface(FontFamily.Default, weight: FontWeight.SemiBold), 10.5, brush);
            context.DrawText(text, new Point((Bounds.Width - text.Width) / 2, y - text.Height));
        }
    }

    /// <summary>
    /// Where the line stops short of a head's tip: at the base of a triangle or diamond, so it does not show through a
    /// hollow one; it runs into an open arrow.
    /// </summary>
    private static double HeadLength(ConnectorHead head) => head switch
    {
        ConnectorHead.HollowTriangle => 8.5,
        ConnectorHead.FilledArrow => 7,
        ConnectorHead.HollowDiamond or ConnectorHead.FilledDiamond => 12,
        _ => 0,
    };

    /// <summary>
    /// Draws a head with its tip at <paramref name="tip"/>, the line coming from the left (<paramref name="direction"/> 1)
    /// or from the right (-1); the canvas's heads, scaled to the icon.
    /// </summary>
    private static void DrawHead(DrawingContext context, ConnectorHead head, Point tip, int direction, IBrush brush, IPen pen)
    {
        Point Back(double along, double across) => new(tip.X - direction * along, tip.Y + across);
        switch (head)
        {
            case ConnectorHead.OpenArrow:
                context.DrawGeometry(null, pen, Polyline(false, Back(7, -4), tip, Back(7, 4)));
                break;
            case ConnectorHead.FilledArrow:
                context.DrawGeometry(brush, null, Polyline(true, Back(7, -3.5), tip, Back(7, 3.5)));
                break;
            case ConnectorHead.HollowTriangle:
                context.DrawGeometry(null, pen, Polyline(true, Back(8.5, -5), tip, Back(8.5, 5)));
                break;
            case ConnectorHead.HollowDiamond:
            case ConnectorHead.FilledDiamond:
                var diamond = Polyline(true, tip, Back(6, -4), Back(12, 0), Back(6, 4));
                context.DrawGeometry(head == ConnectorHead.FilledDiamond ? brush : null, pen, diamond);
                break;
        }
    }

    private static StreamGeometry Polyline(bool closed, params Point[] points)
    {
        var geometry = new StreamGeometry();
        using var g = geometry.Open();
        g.BeginFigure(points[0], isFilled: closed);
        foreach (var point in points.Skip(1))
        {
            g.LineTo(point);
        }

        g.EndFigure(closed);
        return geometry;
    }
}
