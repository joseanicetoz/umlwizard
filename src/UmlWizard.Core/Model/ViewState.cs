namespace UmlWizard.Core.Model;

/// <summary>Zoom and scroll position of the diagram editor.</summary>
public sealed class ViewState
{
    public double Zoom { get; set; } = 1.0;

    public double OffsetX { get; set; }

    public double OffsetY { get; set; }
}
