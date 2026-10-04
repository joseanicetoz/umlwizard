using System.Text;
using System.Text.Json.Nodes;
using UmlWizard.Core.Model;

namespace UmlWizard.Ai;

/// <summary>
/// The tools the model may call to change a sequence diagram. Lifelines are referred to by name; messages by their
/// number in time order, which is their order from top to bottom. A new message goes at a position in that order, and
/// <see cref="SequencePlacement"/> makes room for it.
/// </summary>
public sealed class SequenceDiagramTools : DiagramTools
{
    private readonly SequenceDiagramModel _model;

    public SequenceDiagramTools(DiagramDocument document)
        : base(document)
    {
        _model = (SequenceDiagramModel)document.Model;
        Order = [.. _model.Messages.OrderBy(m => document.Layout.Find(m.Id)?.Y ?? 0).Select(m => m.Id)];
    }

    /// <summary>The messages in time order, as the model sees them: the first is message 1.</summary>
    public List<string> Order { get; }

    public override IReadOnlyList<AiTool> Tools => Definitions;

    public override string Guidance => """
        You can change this sequence diagram with your tools. Use them when the user asks for a change. Refer to lifelines
        by the references listed below the diagram and to messages by their number. Message texts are operations, e.g.
        placeOrder(cart), or for replies the value returned. Adding or removing a message renumbers the ones after it.
        """;

    public override void Place() => SequencePlacement.Place(Document, Added, Order);

    public ElementReferences References() =>
        new(_model.Lifelines.Select(l => (l.Id, string.IsNullOrWhiteSpace(l.Name) ? l.Type : l.Name, "lifeline")));

    public override string Outline()
    {
        var references = References();
        var text = new StringBuilder("Lifelines, from left to right (refer to them by these references):\n");
        foreach (var lifeline in _model.Lifelines.OrderBy(l => Document.Layout.Find(l.Id)?.X ?? 0))
        {
            text.Append("- ").Append(references[lifeline.Id]);
            if (!string.IsNullOrWhiteSpace(lifeline.Type))
            {
                text.Append(" : ").Append(lifeline.Type);
            }

            text.Append(" (").Append(SnakeCase(lifeline.Kind.ToString())).Append(")\n");
        }

        if (_model.Lifelines.Count == 0)
        {
            text.Append("(none)\n");
        }

        text.Append("Messages, in time order:\n");
        for (var i = 0; i < Order.Count; i++)
        {
            var message = _model.Messages.Find(m => m.Id == Order[i])!;
            text.Append(i + 1).Append(". ").Append(references[message.SourceId]).Append(" -> ").Append(references[message.TargetId])
                .Append(": ").Append(message.Text).Append(" (").Append(SnakeCase(message.Kind.ToString())).Append(")\n");
        }

        if (Order.Count == 0)
        {
            text.Append("(none)\n");
        }

        return text.ToString();
    }

    public static IReadOnlyList<AiTool> Definitions { get; } =
    [
        Tool("add_lifeline", "Adds a lifeline: an object (participant) or an actor.", """
            {"type": "object", "properties": {
              "name": {"type": "string", "description": "e.g. shop, or customer"},
              "type": {"type": "string", "description": "Optional class of the participant, e.g. ShopController."},
              "kind": {"type": "string", "enum": ["object", "actor"], "description": "Default: object."}},
             "required": ["name"]}
            """),
        Tool("set_lifeline", "Renames a lifeline or changes its type.", """
            {"type": "object", "properties": {
              "lifeline": {"type": "string"}, "new_name": {"type": "string"}, "type": {"type": "string", "description": "Empty to remove it."}},
             "required": ["lifeline"]}
            """),
        Tool("delete_lifeline", "Deletes a lifeline with its messages.", """
            {"type": "object", "properties": {"lifeline": {"type": "string"}}, "required": ["lifeline"]}
            """),
        Tool("add_message", "Adds a message from source to target (the same lifeline for a message to itself).", """
            {"type": "object", "properties": {
              "source": {"type": "string"}, "target": {"type": "string"},
              "text": {"type": "string", "description": "e.g. placeOrder(cart), or the value of a reply."},
              "kind": {"type": "string", "enum": ["synchronous", "asynchronous", "reply", "create"], "description": "Default: synchronous."},
              "position": {"type": "integer", "description": "The number the message gets; the messages from there on move down. Default: after the last."}},
             "required": ["source", "target", "text"]}
            """),
        Tool("set_message", "Changes the text or kind of a message.", """
            {"type": "object", "properties": {
              "number": {"type": "integer"}, "text": {"type": "string"},
              "kind": {"type": "string", "enum": ["synchronous", "asynchronous", "reply", "create"]}},
             "required": ["number"]}
            """),
        Tool("remove_message", "Removes a message; the ones after it are renumbered.", """
            {"type": "object", "properties": {"number": {"type": "integer"}}, "required": ["number"]}
            """),
        NoteTool("lifeline"),
    ];

    protected override string? Execute(string name, JsonObject args) => name switch
    {
        "add_lifeline" => AddLifeline(args),
        "set_lifeline" => SetLifeline(args),
        "delete_lifeline" => DeleteLifeline(args),
        "add_message" => AddMessage(args),
        "set_message" => SetMessage(args),
        "remove_message" => RemoveMessage(args),
        "add_note" => AddNote(args),
        _ => null,
    };

    private string AddLifeline(JsonObject args)
    {
        var name = Required(args, "name");
        if (_model.Lifelines.Any(l => l.Name == name))
        {
            throw new ToolException($"There is already a lifeline named {name}.");
        }

        var kind = Optional(args, "kind") is { } k ? EnumValue(k, Enum.GetValues<LifelineKind>(), "kind") : LifelineKind.Object;
        var lifeline = new Lifeline { Name = name, Type = Optional(args, "type"), Kind = kind };
        _model.Lifelines.Add(lifeline);
        Document.Layout.Add(new ElementLayout { ElementId = lifeline.Id, Width = 120 });
        Added.Add(lifeline.Id);
        return $"Added the lifeline {name}.";
    }

    private string SetLifeline(JsonObject args)
    {
        var lifeline = Lifeline(Required(args, "lifeline"));
        if (Optional(args, "new_name") is { } name)
        {
            if (_model.Lifelines.Any(l => l != lifeline && l.Name == name))
            {
                throw new ToolException($"There is already a lifeline named {name}.");
            }

            lifeline.Name = name;
        }

        if (args.ContainsKey("type"))
        {
            lifeline.Type = Optional(args, "type");
        }

        return $"The lifeline is now {lifeline.Name}{(lifeline.Type is { } type ? " : " + type : string.Empty)}.";
    }

    private string DeleteLifeline(JsonObject args)
    {
        var lifeline = Lifeline(Required(args, "lifeline"));
        var removed = _model.RemoveElement(lifeline.Id);
        foreach (var id in removed)
        {
            Document.Layout.Remove(id);
            Added.Remove(id);
        }

        var messages = Order.RemoveAll(removed.Contains);
        return $"Deleted {lifeline.Name}" + (messages > 0 ? $" and its {messages} message(s); the messages are renumbered." : ".");
    }

    private string AddMessage(JsonObject args)
    {
        var source = Lifeline(Required(args, "source"));
        var target = Lifeline(Required(args, "target"));
        var kind = Kind(args) ?? MessageKind.Synchronous;
        var position = OptionalNumber(args, "position") ?? Order.Count + 1;
        if (position < 1 || position > Order.Count + 1)
        {
            throw new ToolException($"The position must be from 1 to {Order.Count + 1}.");
        }

        var message = new SequenceMessage { SourceId = source.Id, TargetId = target.Id, Text = Required(args, "text"), Kind = kind };
        _model.Messages.Add(message);
        Document.Layout.Add(new ElementLayout { ElementId = message.Id });
        Order.Insert(position - 1, message.Id);
        Added.Add(message.Id);
        return $"Added message {position}: {source.Name} -> {target.Name}: {message.Text}.";
    }

    private string SetMessage(JsonObject args)
    {
        var message = Message(args);
        if (Optional(args, "text") is { } text)
        {
            message.Text = text;
        }

        message.Kind = Kind(args) ?? message.Kind;
        return $"Message {Order.IndexOf(message.Id) + 1} is now {message.Text} ({SnakeCase(message.Kind.ToString())}).";
    }

    private string RemoveMessage(JsonObject args)
    {
        var message = Message(args);
        var number = Order.IndexOf(message.Id) + 1;
        foreach (var id in _model.RemoveElement(message.Id))
        {
            Document.Layout.Remove(id);
        }

        Order.Remove(message.Id);
        Added.Remove(message.Id);
        return $"Removed message {number}; the messages after it are renumbered.";
    }

    private string AddNote(JsonObject args)
    {
        var target = Optional(args, "attach_to") is { } name ? Lifeline(name) : null;
        return AddNote(Required(args, "text"), target, target?.Name);
    }

    private MessageKind? Kind(JsonObject args) =>
        Optional(args, "kind") is { } kind ? EnumValue(kind, Enum.GetValues<MessageKind>(), "message kind") : null;

    private SequenceMessage Message(JsonObject args)
    {
        var number = OptionalNumber(args, "number") ?? throw new ToolException("The argument number is missing.");
        return number >= 1 && number <= Order.Count
            ? _model.Messages.Find(m => m.Id == Order[number - 1])!
            : throw new ToolException(Order.Count == 0 ? "There are no messages." : $"There is no message {number}; they are numbered 1 to {Order.Count}.");
    }

    private Lifeline Lifeline(string reference) =>
        References().Find(reference, out var problem) is { } id ? _model.FindLifeline(id)! : throw new ToolException(problem!);
}
