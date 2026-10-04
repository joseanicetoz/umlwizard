using SkiaSharp;

namespace UmlWizard.Core.Rendering;

/// <summary>Interaction state drawn on top of the diagram.</summary>
public sealed class RenderOptions
{
    public string? SelectedId { get; init; }

    /// <summary>Element highlighted as a potential connector target.</summary>
    public string? HighlightId { get; init; }

    public SKPoint? ConnectorStart { get; init; }

    public SKPoint? ConnectorEnd { get; init; }

    /// <summary>Draws a faint outline around text boxes so they can be found while editing; off for exports.</summary>
    public bool ShowTextBoxOutlines { get; init; }
}
