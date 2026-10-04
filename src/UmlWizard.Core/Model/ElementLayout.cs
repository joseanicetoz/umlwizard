namespace UmlWizard.Core.Model;

/// <summary>
/// Position, size, font and shape style of a diagram element in world coordinates.
/// For sequence messages only <see cref="Y"/> is meaningful; relationships and edges only use <see cref="Font"/> and <see cref="Line"/>.
/// </summary>
public sealed class ElementLayout
{
    public string ElementId { get; set; } = string.Empty;

    public double X { get; set; }

    public double Y { get; set; }

    public double Width { get; set; }

    public double Height { get; set; }

    /// <summary>Font of this element; <c>null</c> uses the diagram font.</summary>
    public DiagramFont? Font { get; set; }

    /// <summary>Fill and border of the element's shape; <c>null</c> uses the theme's colours.</summary>
    public ShapeStyle? Style { get; set; }

    /// <summary>Colour and thickness of a connector; <c>null</c> uses the theme's line.</summary>
    public LineStyle? Line { get; set; }

    public ElementLayout Clone()
    {
        var clone = (ElementLayout)MemberwiseClone();
        clone.Font = Font?.Clone();
        clone.Style = Style?.Clone();
        clone.Line = Line?.Clone();
        return clone;
    }
}
