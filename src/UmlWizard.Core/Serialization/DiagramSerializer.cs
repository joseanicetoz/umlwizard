using System.Text.Json;
using UmlWizard.Core.Model;

namespace UmlWizard.Core.Serialization;

/// <summary>
/// Serializes a single diagram. Used for undo snapshots, copies of diagrams and importing stand-alone
/// diagram files (the format of schema 1 projects).
/// </summary>
public static class DiagramSerializer
{
    public const string FileExtension = ".diagram.json";

    public static string Serialize(DiagramDocument document) => JsonSerializer.Serialize(document, UmlJson.Options);

    public static DiagramDocument Deserialize(string json)
    {
        DiagramDocument? document;
        try
        {
            document = JsonSerializer.Deserialize<DiagramDocument>(json, UmlJson.Options);
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"The diagram is not valid: {ex.Message}", ex);
        }

        if (document is null)
        {
            throw new InvalidDataException("The diagram is empty.");
        }

        Validate(document);
        return document;
    }

    /// <summary>Checks a deserialized diagram and fills in optional sections.</summary>
    internal static void Validate(DiagramDocument document)
    {
        if (document.Model is null)
        {
            throw new InvalidDataException($"The diagram '{document.Name}' has no model.");
        }

        if (document.Model.DiagramType != document.DiagramType)
        {
            throw new InvalidDataException(
                $"The diagram type '{document.DiagramType}' of '{document.Name}' does not match its model '{document.Model.DiagramType}'.");
        }

        document.Layout ??= new DiagramLayout();
        document.Model.TextBoxes ??= [];
        document.Model.Notes ??= [];
        document.Model.NoteAttachments ??= [];
        if (document.Model is GraphDiagramModel graph)
        {
            graph.Nodes ??= [];
            graph.Edges ??= [];
            graph.Edges.RemoveAll(e => e is null || graph.FindNode(e.SourceId) is null || graph.FindNode(e.TargetId) is null);
        }

        document.Model.RemoveDanglingAttachments();
    }

    /// <summary>Returns an independent deep copy of <paramref name="document"/>.</summary>
    public static DiagramDocument Clone(DiagramDocument document) => Deserialize(Serialize(document));

    public static DiagramDocument Load(string path) => Deserialize(File.ReadAllText(path));
}
