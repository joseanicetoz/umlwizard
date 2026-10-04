using UmlWizard.Core.Model;

namespace UmlWizard.App.ViewModels.Dialogs;

/// <summary>Enum values for combo boxes.</summary>
public static class EnumValues
{
    public static UmlVisibility[] Visibilities { get; } = Enum.GetValues<UmlVisibility>();

    public static ClassifierKind[] ClassifierKinds { get; } = Enum.GetValues<ClassifierKind>();

    public static RelationshipKind[] RelationshipKinds { get; } = Enum.GetValues<RelationshipKind>();

    public static LifelineKind[] LifelineKinds { get; } = Enum.GetValues<LifelineKind>();

    public static MessageKind[] MessageKinds { get; } = Enum.GetValues<MessageKind>();

    public static DiagramType[] DiagramTypes { get; } = Enum.GetValues<DiagramType>();

    public static HorizontalTextAlignment[] HorizontalTextAlignments { get; } = Enum.GetValues<HorizontalTextAlignment>();

    public static VerticalTextAlignment[] VerticalTextAlignments { get; } = Enum.GetValues<VerticalTextAlignment>();

    /// <summary>The HTTP methods of web API endpoints; the empty entry means the operation is not an endpoint.</summary>
    public static string[] HttpMethods { get; } = ["", "GET", "POST", "PUT", "PATCH", "DELETE", "HEAD", "OPTIONS", "TRACE"];
}
