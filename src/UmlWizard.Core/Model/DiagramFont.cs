namespace UmlWizard.Core.Model;

/// <summary>
/// Font of diagram text. The style is combined with the UML notation: class names stay bold and
/// abstract elements italic whatever the font style.
/// </summary>
public sealed class DiagramFont
{
    public const double DefaultSize = 13;
    public const double MinSize = 6;
    public const double MaxSize = 72;

    /// <summary>Font family name; <c>null</c> uses the platform's default family.</summary>
    public string? Family { get; set; }

    /// <summary>Size in points at zoom 100 %.</summary>
    public double Size { get; set; } = DefaultSize;

    public bool Bold { get; set; }

    public bool Italic { get; set; }

    public DiagramFont Clone() => (DiagramFont)MemberwiseClone();
}
