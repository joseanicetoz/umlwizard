namespace UmlWizard.Core.Model;

/// <summary>A UML note (comment): text drawn in a rectangle with a folded top-right corner.</summary>
public sealed class NoteElement : TextElement
{
    public const double DefaultWidth = 160;
    public const double DefaultHeight = 80;

    public NoteElement() => Text = "Note";

    public override (double Width, double Height) DefaultSize => (DefaultWidth, DefaultHeight);
}
