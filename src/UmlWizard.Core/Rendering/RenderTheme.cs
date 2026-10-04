using SkiaSharp;
using UmlWizard.Core.Model;

namespace UmlWizard.Core.Rendering;

/// <summary>
/// The colours diagrams are drawn in. <see cref="Light"/> is the default and is always used for exports;
/// <see cref="Dark"/> is used on the canvas when the application is in dark mode.
/// </summary>
public sealed record RenderTheme
{
    public const float FontSize = 13f;

    /// <summary>Font family used when a diagram font does not name one.</summary>
    public static readonly string DefaultFontFamily =
        OperatingSystem.IsWindows() ? "Segoe UI"
        : OperatingSystem.IsMacOS() ? "Helvetica Neue"
        : "DejaVu Sans";

    public static RenderTheme Light { get; } = new()
    {
        CanvasBackground = SKColors.White,
        Grid = new(0xDD, 0xE1, 0xE7),
        TextBoxOutline = new(0xB8, 0xC0, 0xCC),
        NoteFill = new(0xFF, 0xF8, 0xC5),
        NoteFold = new(0xF2, 0xE6, 0x9E),
        NodeFill = SKColors.White,
        ClassHeader = new(0xE8, 0xEE, 0xF9),
        AbstractHeader = new(0xEE, 0xF0, 0xF4),
        InterfaceHeader = new(0xE4, 0xF3, 0xE8),
        EnumerationHeader = new(0xF2, 0xE9, 0xFB),
        LifelineHead = new(0xE8, 0xEE, 0xF9),
        StateFill = new(0xE8, 0xEE, 0xF9),
        UseCaseFill = new(0xE4, 0xF3, 0xE8),
        ComponentFill = new(0xF2, 0xE9, 0xFB),
        BoundaryFill = new(0xFA, 0xFB, 0xFC),
        DeploymentNodeFill = new(0xF1, 0xF4, 0xF8),
        DeploymentNodeShade = new(0xD9, 0xE0, 0xE9),
        ArtifactFill = new(0xFF, 0xF4, 0xE0),
        Border = new(0x44, 0x54, 0x6A),
        Text = new(0x1F, 0x29, 0x33),
        MutedText = new(0x5F, 0x6B, 0x7A),
        Line = new(0x44, 0x54, 0x6A),
        Selection = new(0x1A, 0x73, 0xE8),
        Highlight = new(0xF2, 0x99, 0x00),
        Shadow = new(0x00, 0x00, 0x00, 0x22),
    };

    /// <summary>
    /// Dark surfaces with light lines and text. The fills keep the hues of <see cref="Light"/> (blue classes, green
    /// interfaces and use cases, purple enumerations and components, amber artifacts) so kinds stay recognisable.
    /// </summary>
    public static RenderTheme Dark { get; } = new()
    {
        CanvasBackground = new(0x1E, 0x21, 0x26),
        Grid = new(0x34, 0x3A, 0x43),
        TextBoxOutline = new(0x5A, 0x63, 0x70),
        NoteFill = new(0x4A, 0x45, 0x28),
        NoteFold = new(0x62, 0x5B, 0x34),
        NodeFill = new(0x26, 0x2A, 0x31),
        ClassHeader = new(0x2B, 0x3A, 0x52),
        AbstractHeader = new(0x34, 0x3A, 0x44),
        InterfaceHeader = new(0x27, 0x40, 0x2F),
        EnumerationHeader = new(0x3A, 0x30, 0x50),
        LifelineHead = new(0x2B, 0x3A, 0x52),
        StateFill = new(0x2B, 0x3A, 0x52),
        UseCaseFill = new(0x27, 0x40, 0x2F),
        ComponentFill = new(0x3A, 0x30, 0x50),
        BoundaryFill = new(0x22, 0x26, 0x2C),
        DeploymentNodeFill = new(0x2C, 0x32, 0x3A),
        DeploymentNodeShade = new(0x3B, 0x43, 0x4E),
        ArtifactFill = new(0x46, 0x38, 0x23),
        Border = new(0x9A, 0xA8, 0xBA),
        Text = new(0xE6, 0xEA, 0xF0),
        MutedText = new(0xA3, 0xAE, 0xBC),
        Line = new(0x9A, 0xA8, 0xBA),
        Selection = new(0x4C, 0x9A, 0xFF),
        Highlight = new(0xFF, 0xB0, 0x20),
        Shadow = new(0x00, 0x00, 0x00, 0x66),
    };

    /// <summary>
    /// The fill the theme gives an element's shape when it has no <see cref="ShapeStyle.Fill"/> of its own: the header
    /// of a class, the head of a lifeline, the body of a node or note. <c>null</c> for text boxes, which have none.
    /// </summary>
    public SKColor? DefaultFill(ModelElement element) => element switch
    {
        UmlClass cls => ClassFill(cls.Kind),
        Lifeline lifeline => lifeline.Kind == LifelineKind.Actor ? NodeFill : LifelineHead,
        GraphNode node => GraphNodeFill(node.Kind),
        NoteElement => NoteFill,
        _ => null,
    };

    /// <summary>The border colour an element's shape has by default; <c>null</c> for text boxes, which have none.</summary>
    public SKColor? DefaultBorder(ModelElement element) => element is TextBoxElement ? null : Border;

    /// <summary>The header colour of a classifier.</summary>
    public SKColor ClassFill(ClassifierKind kind) => kind switch
    {
        ClassifierKind.Interface => InterfaceHeader,
        ClassifierKind.Enumeration => EnumerationHeader,
        ClassifierKind.AbstractClass => AbstractHeader,
        _ => ClassHeader,
    };

    /// <summary>The fill of a node of a state machine, activity, use case, component or deployment diagram.</summary>
    public SKColor GraphNodeFill(GraphNodeKind kind) => kind switch
    {
        GraphNodeKind.State or GraphNodeKind.Action => StateFill,
        GraphNodeKind.UseCase => UseCaseFill,
        GraphNodeKind.Component => ComponentFill,
        GraphNodeKind.Artifact => ArtifactFill,
        GraphNodeKind.SystemBoundary => BoundaryFill,
        GraphNodeKind.Node or GraphNodeKind.Device or GraphNodeKind.ExecutionEnvironment => DeploymentNodeFill,
        // Pseudostates and fork/join bars are solid, in the line colour.
        GraphNodeKind.InitialState or GraphNodeKind.InitialNode or GraphNodeKind.ForkJoin => Line,
        _ => NodeFill,
    };

    public required SKColor CanvasBackground { get; init; }
    public required SKColor Grid { get; init; }
    public required SKColor TextBoxOutline { get; init; }
    public required SKColor NoteFill { get; init; }
    public required SKColor NoteFold { get; init; }
    public required SKColor NodeFill { get; init; }
    public required SKColor ClassHeader { get; init; }
    public required SKColor AbstractHeader { get; init; }
    public required SKColor InterfaceHeader { get; init; }
    public required SKColor EnumerationHeader { get; init; }
    public required SKColor LifelineHead { get; init; }
    public required SKColor StateFill { get; init; }
    public required SKColor UseCaseFill { get; init; }
    public required SKColor ComponentFill { get; init; }
    public required SKColor BoundaryFill { get; init; }
    public required SKColor DeploymentNodeFill { get; init; }
    public required SKColor DeploymentNodeShade { get; init; }
    public required SKColor ArtifactFill { get; init; }
    public required SKColor Border { get; init; }
    public required SKColor Text { get; init; }
    public required SKColor MutedText { get; init; }
    public required SKColor Line { get; init; }
    public required SKColor Selection { get; init; }
    public required SKColor Highlight { get; init; }
    public required SKColor Shadow { get; init; }
}
