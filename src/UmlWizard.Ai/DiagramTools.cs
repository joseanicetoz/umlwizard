using System.Text;
using System.Text.Json.Nodes;
using UmlWizard.Core.Model;

namespace UmlWizard.Ai;

/// <summary>
/// The tools the model may call to change a diagram, run on a working copy (see <see cref="AiChatSession"/>). Elements
/// are referred to by name (see <see cref="ElementReferences"/>); a call that cannot be done changes nothing and returns
/// an error, so the model can correct itself. What was added has no place yet: <see cref="Place"/> places it, without
/// moving the rest.
/// </summary>
public abstract class DiagramTools(DiagramDocument document)
{
    protected DiagramDocument Document { get; } = document;

    /// <summary>The elements added (or moved), in order: they have no place yet.</summary>
    public List<string> Added { get; } = [];

    /// <summary>Whether any call changed the diagram.</summary>
    public bool Changed { get; private set; }

    /// <summary>The tools of this kind of diagram, for the model.</summary>
    public abstract IReadOnlyList<AiTool> Tools { get; }

    /// <summary>How to use the tools, for the system prompt.</summary>
    public abstract string Guidance { get; }

    /// <summary>The tools for a diagram, or <c>null</c> for a diagram the AI cannot change.</summary>
    public static DiagramTools? For(DiagramDocument document) => document.Model switch
    {
        ClassDiagramModel => new ClassDiagramTools(document),
        SequenceDiagramModel => new SequenceDiagramTools(document),
        GraphDiagramModel => new GraphDiagramTools(document),
        _ => null,
    };

    /// <summary>The elements of the diagram with the references the tools take, for the system prompt; <c>null</c> when names suffice.</summary>
    public virtual string? Outline() => null;

    /// <summary>Places what was added, once the model is done.</summary>
    public abstract void Place();

    /// <summary>Runs a call; never throws for bad input, which becomes an error result.</summary>
    public AiToolResult Run(AiToolCall call)
    {
        try
        {
            var message = Execute(call.Name, call.Arguments) ?? throw new ToolException($"There is no tool named {call.Name}.");
            Changed = true;
            return new AiToolResult(call.Id, message);
        }
        catch (ToolException ex)
        {
            return new AiToolResult(call.Id, ex.Message, IsError: true);
        }
    }

    /// <summary>Runs the tool named <paramref name="name"/>; <c>null</c> when there is no such tool.</summary>
    /// <exception cref="ToolException">The call cannot be done.</exception>
    protected abstract string? Execute(string name, JsonObject args);

    /// <summary>Adds a note, attached to <paramref name="target"/> if given.</summary>
    protected string AddNote(string text, ModelElement? target, string? targetName)
    {
        var note = new NoteElement { Text = text };
        Document.Model.Add(note);
        var (width, height) = note.DefaultSize;
        Document.Layout.Add(new ElementLayout { ElementId = note.Id, Width = width, Height = height });
        if (target is not null)
        {
            Document.Model.NoteAttachments.Add(new NoteAttachment { NoteId = note.Id, TargetId = target.Id });
        }

        Added.Add(note.Id);
        return target is null ? "Added the note." : $"Added the note, attached to {targetName}.";
    }

    /// <summary>The tool <c>add_note</c>, with what it may be attached to.</summary>
    protected static AiTool NoteTool(string attachable) => Tool("add_note", $"Adds a note (a UML comment), attached to {attachable} if given.", $$"""
        {"type": "object", "properties": {"text": {"type": "string"}, "attach_to": {"type": "string", "description": "The {{attachable}} it comments on."} }, "required": ["text"]}
        """);

    protected static string Required(JsonObject args, string name) =>
        Optional(args, name) ?? throw new ToolException($"The argument {name} is missing.");

    protected static string? Optional(JsonObject args, string name) =>
        args[name] is JsonValue value && value.TryGetValue<string>(out var text) && !string.IsNullOrWhiteSpace(text) ? text.Trim() : null;

    /// <summary>A whole number, given as a number or as text; <c>null</c> when it is missing.</summary>
    protected static int? OptionalNumber(JsonObject args, string name)
    {
        if (args[name] is not JsonValue value)
        {
            return null;
        }

        if (value.TryGetValue<int>(out var number))
        {
            return number;
        }

        if (value.TryGetValue<double>(out var real) && real == Math.Floor(real))
        {
            return (int)real;
        }

        return value.TryGetValue<string>(out var text) && int.TryParse(text, out number) ? number
            : throw new ToolException($"The argument {name} must be a whole number.");
    }

    protected static AiTool Tool(string name, string description, string schema) =>
        new(name, description, (JsonObject)JsonNode.Parse(schema)!);

    /// <summary>An enumeration value as the tools write it, e.g. <c>InitialState</c> as <c>initial_state</c>.</summary>
    public static string SnakeCase(string name)
    {
        var text = new StringBuilder();
        foreach (var c in name)
        {
            if (char.IsUpper(c) && text.Length > 0)
            {
                text.Append('_');
            }

            text.Append(char.ToLowerInvariant(c));
        }

        return text.ToString();
    }

    /// <summary>The value of <typeparamref name="T"/> written as <paramref name="text"/> in snake case, among <paramref name="allowed"/>.</summary>
    protected static T EnumValue<T>(string text, IEnumerable<T> allowed, string what)
        where T : struct, Enum
    {
        var values = allowed.ToList();
        return values.Cast<T?>().FirstOrDefault(v => SnakeCase(v!.Value.ToString()) == text.Trim().ToLowerInvariant().Replace(' ', '_'))
            ?? throw new ToolException($"Unknown {what} {text}; use {string.Join(", ", values.Select(v => SnakeCase(v.ToString())))}.");
    }

    /// <summary>A call that cannot be done: the message goes back to the model.</summary>
    protected sealed class ToolException(string message) : Exception(message);
}
