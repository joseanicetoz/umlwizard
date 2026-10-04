using Avalonia.Data.Converters;
using Material.Icons;
using UmlWizard.App.Resources;
using UmlWizard.Core.Model;

namespace UmlWizard.App.ViewModels;

/// <summary>How a diagram type is presented: its name, the prefix of new diagram names and its icon.</summary>
public sealed record DiagramTypeInfo(DiagramType Type, string DisplayName, string NamePrefix, MaterialIconKind Icon)
{
    /// <summary>Every diagram type, in the order they are offered.</summary>
    public static IReadOnlyList<DiagramTypeInfo> All { get; } =
    [
        new(DiagramType.Class, Strings.DiagramType_Class, Strings.DiagramName_Class, MaterialIconKind.SitemapOutline),
        new(DiagramType.Sequence, Strings.DiagramType_Sequence, Strings.DiagramName_Sequence, MaterialIconKind.SwapHorizontal),
        new(DiagramType.StateMachine, Strings.DiagramType_StateMachine, Strings.DiagramName_StateMachine, MaterialIconKind.StateMachine),
        new(DiagramType.Activity, Strings.DiagramType_Activity, Strings.DiagramName_Activity, MaterialIconKind.TransitConnectionVariant),
        new(DiagramType.UseCase, Strings.DiagramType_UseCase, Strings.DiagramName_UseCase, MaterialIconKind.AccountOutline),
        new(DiagramType.Component, Strings.DiagramType_Component, Strings.DiagramName_Component, MaterialIconKind.PuzzleOutline),
        new(DiagramType.Deployment, Strings.DiagramType_Deployment, Strings.DiagramName_Deployment, MaterialIconKind.ServerNetwork),
    ];

    public static DiagramTypeInfo For(DiagramType type) => All.First(i => i.Type == type);

    /// <summary>Shows a <see cref="DiagramType"/> by its display name, e.g. in a combo box.</summary>
    public static IValueConverter NameConverter { get; } =
        new FuncValueConverter<DiagramType, string>(type => For(type).DisplayName);
}
