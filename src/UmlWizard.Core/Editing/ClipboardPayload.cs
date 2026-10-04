using System.Text.Json;
using UmlWizard.Core.Model;
using UmlWizard.Core.Serialization;

namespace UmlWizard.Core.Editing;

/// <summary>A copied diagram element (model plus layout), exchanged as JSON text via the clipboard.</summary>
public sealed class ClipboardPayload
{
    public const string FormatMarker = "umlwizard/element";

    public string Format { get; set; } = FormatMarker;

    public DiagramType DiagramType { get; set; }

    public UmlClass? Class { get; set; }

    public UmlRelationship? Relationship { get; set; }

    public Lifeline? Lifeline { get; set; }

    public SequenceMessage? Message { get; set; }

    /// <summary>A text box, which can be pasted into a diagram of any type.</summary>
    public TextBoxElement? TextBox { get; set; }

    /// <summary>A node of a state machine, activity, use case or component diagram.</summary>
    public GraphNode? Node { get; set; }

    /// <summary>An edge of such a diagram; it is pasted when both of its nodes exist.</summary>
    public GraphEdge? Edge { get; set; }

    /// <summary>A UML note, which can be pasted into a diagram of any type.</summary>
    public NoteElement? Note { get; set; }

    /// <summary>The copied note or text box, if any.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public TextElement? TextElement => (TextElement?)TextBox ?? Note;

    public ElementLayout? Layout { get; set; }

    public string ToJson() => JsonSerializer.Serialize(this, UmlJson.Options);

    public static bool TryParse(string? text, out ClipboardPayload payload)
    {
        payload = null!;
        if (string.IsNullOrWhiteSpace(text) || !text.Contains(FormatMarker, StringComparison.Ordinal))
        {
            return false;
        }

        try
        {
            var parsed = JsonSerializer.Deserialize<ClipboardPayload>(text, UmlJson.Options);
            if (parsed?.Format != FormatMarker)
            {
                return false;
            }

            payload = parsed;
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
