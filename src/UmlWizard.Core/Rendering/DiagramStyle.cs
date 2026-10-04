using SkiaSharp;
using UmlWizard.Core.Model;

namespace UmlWizard.Core.Rendering;

/// <summary>
/// The default look of the shapes of one diagram type, as the user prefers it: a fill per kind of element, and the
/// border colour and thickness of every shape. Each part that is not set keeps the theme's; an element's own
/// <see cref="ShapeStyle"/> overrides it part by part. Fills are colours (<c>#RRGGBB</c>) the same in both themes, as
/// the colours of a <see cref="ShapeStyle"/> are, and text on them turns dark or light, whichever reads better.
/// </summary>
public sealed record DiagramStyle
{
    /// <summary>Fills by <see cref="FillSlot.Key"/>, as <c>#RRGGBB</c>.</summary>
    public Dictionary<string, string> Fills { get; init; } = [];

    /// <summary>The border colour of every shape (not of text boxes, which have none), as <c>#RRGGBB</c>.</summary>
    public string? Border { get; init; }

    /// <summary>The border thickness of every shape, at zoom 100 %.</summary>
    public double? BorderWidth { get; init; }

    public bool IsEmpty => Fills.Count == 0 && Border is null && BorderWidth is null;

    /// <summary>The preferred fill of an element, or <c>null</c> when it keeps the theme's.</summary>
    public string? FillOf(ModelElement element) =>
        FillSlot.KeyOf(element) is { } key && Fills.TryGetValue(key, out var fill) && ShapeColors.Parse(fill) is not null ? fill : null;

    /// <summary>
    /// The style an element is drawn with: its own <paramref name="own"/> style, with the parts it does not set taken
    /// from these preferences. <c>null</c> when neither sets anything.
    /// </summary>
    public ShapeStyle? Apply(ModelElement element, ShapeStyle? own)
    {
        var hasBorder = element is not TextBoxElement;
        var fill = own?.Fill ?? FillOf(element);
        var border = own?.Border ?? (hasBorder && ShapeColors.Parse(Border) is not null ? Border : null);
        var width = own?.BorderWidth ?? (hasBorder ? BorderWidth : null);
        return fill is null && border is null && width is null
            ? null
            : new ShapeStyle { Fill = fill, Border = border, BorderWidth = width };
    }
}

/// <summary>A kind of element whose fill can be preferred, within a diagram type, with the theme's default for it.</summary>
/// <param name="Key">Names it in <see cref="DiagramStyle.Fills"/>, e.g. <c>Interface</c>.</param>
/// <param name="Default">The theme's fill for it.</param>
public sealed record FillSlot(string Key, Func<RenderTheme, SKColor> Default)
{
    public const string Note = "Note";

    /// <summary>The kinds of element of a diagram type whose fill can be preferred, in the order they are offered.</summary>
    public static IReadOnlyList<FillSlot> For(DiagramType type) => type switch
    {
        DiagramType.Class =>
        [
            new("Class", t => t.ClassHeader), new("AbstractClass", t => t.AbstractHeader),
            new("Interface", t => t.InterfaceHeader), new("Enumeration", t => t.EnumerationHeader), NoteSlot,
        ],
        DiagramType.Sequence => [new("Lifeline", t => t.LifelineHead), NoteSlot],
        DiagramType.StateMachine => [new("State", t => t.StateFill), new("Choice", t => t.NodeFill), NoteSlot],
        DiagramType.Activity => [new("Action", t => t.StateFill), new("Decision", t => t.NodeFill), NoteSlot],
        DiagramType.UseCase => [new("UseCase", t => t.UseCaseFill), new("SystemBoundary", t => t.BoundaryFill), NoteSlot],
        DiagramType.Component => [new("Component", t => t.ComponentFill), new("Interface", t => t.NodeFill), NoteSlot],
        DiagramType.Deployment =>
        [
            new("Node", t => t.DeploymentNodeFill), new("Device", t => t.DeploymentNodeFill),
            new("ExecutionEnvironment", t => t.DeploymentNodeFill), new("Artifact", t => t.ArtifactFill),
            new("Component", t => t.ComponentFill), NoteSlot,
        ],
        _ => [],
    };

    private static readonly FillSlot NoteSlot = new(Note, t => t.NoteFill);

    /// <summary>The <see cref="Key"/> of the kind of an element, or <c>null</c> when its fill cannot be preferred.</summary>
    public static string? KeyOf(ModelElement element) => element switch
    {
        UmlClass cls => cls.Kind switch
        {
            ClassifierKind.Interface => "Interface",
            ClassifierKind.Enumeration => "Enumeration",
            ClassifierKind.AbstractClass => "AbstractClass",
            _ => "Class",
        },
        Lifeline { Kind: LifelineKind.Object } => "Lifeline",
        GraphNode node => node.Kind switch
        {
            GraphNodeKind.State => "State",
            GraphNodeKind.Choice => "Choice",
            GraphNodeKind.Action => "Action",
            GraphNodeKind.Decision => "Decision",
            GraphNodeKind.UseCase => "UseCase",
            GraphNodeKind.SystemBoundary => "SystemBoundary",
            GraphNodeKind.Component => "Component",
            GraphNodeKind.Interface => "Interface",
            GraphNodeKind.Node => "Node",
            GraphNodeKind.Device => "Device",
            GraphNodeKind.ExecutionEnvironment => "ExecutionEnvironment",
            GraphNodeKind.Artifact => "Artifact",
            _ => null,
        },
        NoteElement => Note,
        _ => null,
    };
}
