namespace UmlWizard.Core.Model;

/// <summary>A free text label on a diagram of any type. It is an annotation, not a UML element.</summary>
public sealed class TextBoxElement : TextElement
{
    public const double DefaultWidth = 160;
    public const double DefaultHeight = 60;

    public TextBoxElement() => Text = "Text";

    public override (double Width, double Height) DefaultSize => (DefaultWidth, DefaultHeight);
}
