namespace UmlWizard.Core.Model;

/// <summary>
/// Fill and border of an element's shape, set with Format → Shape. Each value that is not set uses the colours of the
/// diagram theme for that kind of element (e.g. blue class headers, yellow notes).
/// </summary>
public sealed class ShapeStyle
{
    public const double DefaultBorderWidth = 1.2;
    public const double MinBorderWidth = 0.5;
    public const double MaxBorderWidth = 8;

    /// <summary>Fill colour as <c>#RRGGBB</c>; <c>null</c> uses the theme's fill for the element.</summary>
    public string? Fill { get; set; }

    /// <summary>Border colour as <c>#RRGGBB</c>; <c>null</c> uses the theme's border colour.</summary>
    public string? Border { get; set; }

    /// <summary>Border thickness at zoom 100 %; <c>null</c> uses <see cref="DefaultBorderWidth"/>.</summary>
    public double? BorderWidth { get; set; }

    /// <summary>True when nothing is set, so the element is drawn with the defaults.</summary>
    public bool IsEmpty => Fill is null && Border is null && BorderWidth is null;

    public ShapeStyle Clone() => (ShapeStyle)MemberwiseClone();
}
