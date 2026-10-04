namespace UmlWizard.Core.Model;

/// <summary>
/// Colour and thickness of a connector (relationship, message or edge), set with Format → Line. Each value that is not
/// set uses the diagram theme's line colour and the default thickness. Arrowheads and diamonds follow the line.
/// </summary>
public sealed class LineStyle
{
    public const double DefaultWidth = 1.3;
    public const double MinWidth = 0.5;
    public const double MaxWidth = 8;

    /// <summary>Line colour as <c>#RRGGBB</c>; <c>null</c> uses the theme's line colour.</summary>
    public string? Color { get; set; }

    /// <summary>Line thickness at zoom 100 %; <c>null</c> uses <see cref="DefaultWidth"/>.</summary>
    public double? Width { get; set; }

    /// <summary>True when nothing is set, so the line is drawn with the defaults.</summary>
    public bool IsEmpty => Color is null && Width is null;

    public LineStyle Clone() => (LineStyle)MemberwiseClone();
}
