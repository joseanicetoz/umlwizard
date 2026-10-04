using System.Text.Json.Serialization;

namespace UmlWizard.Core.Model;

/// <summary>
/// A diagram element that shows free text: a text box or a note. Position, size and font are in the element's
/// <see cref="ElementLayout"/>; the text wraps to the width and is aligned inside the element.
/// </summary>
public abstract class TextElement : ModelElement
{
    public const double MinWidth = 30;
    public const double MinHeight = 20;

    /// <summary>The text; line breaks start new lines.</summary>
    public string Text { get; set; } = string.Empty;

    public HorizontalTextAlignment HorizontalAlignment { get; set; } = HorizontalTextAlignment.Left;

    public VerticalTextAlignment VerticalAlignment { get; set; } = VerticalTextAlignment.Top;

    /// <summary>
    /// The stacking order among the notes and text boxes of the diagram; higher ones are drawn on top, and on a tie
    /// notes lie under text boxes. Not written while it is 0, so older files keep their look.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public int ZIndex { get; set; }

    /// <summary>The size a new element of this kind gets.</summary>
    [JsonIgnore]
    public abstract (double Width, double Height) DefaultSize { get; }
}
