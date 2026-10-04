namespace UmlWizard.Core.Model;

/// <summary>Rendering information of a diagram, kept separate from the UML model.</summary>
public sealed class DiagramLayout
{
    public ViewState View { get; set; } = new();

    /// <summary>Font of elements without their own <see cref="ElementLayout.Font"/>; <c>null</c> uses the default font.</summary>
    public DiagramFont? Font { get; set; }

    public List<ElementLayout> Elements { get; set; } = [];

    public ElementLayout? Find(string elementId) => Elements.Find(e => e.ElementId == elementId);

    /// <summary>The font an element is drawn with: its own font, else the diagram font (<c>null</c> is the default font).</summary>
    public DiagramFont? FontFor(string elementId) => Find(elementId)?.Font ?? Font;

    public ElementLayout Add(ElementLayout layout)
    {
        Elements.Add(layout);
        return layout;
    }

    public void Remove(string elementId) => Elements.RemoveAll(e => e.ElementId == elementId);
}
