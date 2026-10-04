using System.Text;
using System.Text.RegularExpressions;
using UmlWizard.Core.Model;
using UmlWizard.Core.Serialization;
using UmlWizard.PlantUml;

namespace UmlWizard.Ai;

/// <summary>
/// A conversation with the AI about one diagram (the chat panel). The model sees the diagram as PlantUML and changes it
/// with the <see cref="DiagramTools"/> of its type or, for models without tool calling, by answering with the whole
/// revised diagram (<see cref="AiChangeMode.WholeDiagram"/>, taken in by <see cref="DiagramMerge"/>). Changes are made
/// on a working copy: the user sees what changed (<see cref="DescribeChanges"/>) and applies (<see cref="TakeChanges"/>)
/// or discards them; the diagram itself is never touched here. A request that fails or is cancelled leaves the
/// conversation and the copy as they were.
/// </summary>
public sealed partial class AiChatSession
{
    /// <summary>How many rounds of tool calls one message may take before the session stops it.</summary>
    public const int MaxRounds = 10;

    private readonly List<AiMessage> _messages = [];
    private string? _pendingNote;

    public IReadOnlyList<AiMessage> Messages => _messages;

    /// <summary>How the AI changes the diagram; read at each message.</summary>
    public AiChangeMode ChangeMode { get; set; } = AiChangeMode.Tools;

    /// <summary>The diagram with the changes the AI made that are not applied yet; <c>null</c> when there are none.</summary>
    public DiagramDocument? WorkingCopy { get; private set; }

    public bool HasChanges => WorkingCopy is not null;

    /// <summary>Whether the AI can change diagrams of this type: it can change every type, and talk about any.</summary>
    public static bool CanEdit(DiagramType type) => Enum.IsDefined(type);

    /// <summary>
    /// Sends the user's message about <paramref name="diagram"/> (or the working copy, while it has changes) and runs the
    /// tools the model calls, until it answers. Returns its answer.
    /// </summary>
    /// <param name="onText">Gets the text of the answer so far, while it comes; <c>null</c> to wait for the whole answer.</param>
    /// <exception cref="AiException">The service failed.</exception>
    /// <exception cref="OperationCanceledException">The request was cancelled.</exception>
    public async Task<string> SendAsync(
        IAiProvider provider, DiagramDocument diagram, string text, CancellationToken cancellationToken = default, Action<string>? onText = null)
    {
        // Work on copies, kept only when everything went well.
        var working = DiagramSerializer.Clone(WorkingCopy ?? diagram);
        var messages = new List<AiMessage>(_messages) { new AiUserMessage(_pendingNote is null ? text : $"({_pendingNote})\n\n{text}") };
        var whole = ChangeMode == AiChangeMode.WholeDiagram;
        var tools = whole ? null : DiagramTools.For(working);
        Action<string>? shown = onText is null ? null : t => onText(Visible(t));

        Task<AiTurn> Ask(IReadOnlyList<AiTool> available) => shown is null
            ? provider.ChatAsync(Instructions(working, tools, whole), messages, available, cancellationToken)
            : provider.StreamChatAsync(Instructions(working, tools, whole), messages, available, shown, cancellationToken);

        string? answer = null;
        DiagramDocument? changed = null;
        for (var round = 0; round < MaxRounds && answer is null; round++)
        {
            var turn = await Ask(tools?.Tools ?? []);
            messages.Add(turn.ToMessage());
            if (turn.ToolCalls.Count == 0 || tools is null)
            {
                answer = turn.Text ?? string.Empty;
                break;
            }

            messages.Add(new AiToolResults([.. turn.ToolCalls.Select(tools.Run)]));
        }

        if (answer is null)
        {
            // Stopped in the middle of its work: the conversation needs a last answer to stay well-formed.
            answer = $"(Stopped after {MaxRounds} rounds of changes; the changes so far are shown below.)";
            messages.Add(new AiAssistantMessage(answer, []));
        }

        if (tools is { Changed: true })
        {
            tools.Place();
            changed = working;
        }
        else if (whole && AiDiagramGenerator.ExtractPlantUml(answer) is { } plantUml)
        {
            (changed, var problem) = Revise(working, plantUml);
            answer = Visible(answer) is { Length: > 0 } said ? said : "Here is the revised diagram; the changes are shown below.";
            if (problem is not null)
            {
                answer += $"\n\n({problem})";
            }
        }

        if (changed is not null)
        {
            WorkingCopy = changed;
        }

        _messages.Clear();
        _messages.AddRange(messages);
        _pendingNote = null;
        return answer;
    }

    /// <summary>The working copy with the revised diagram taken in, or why it could not be.</summary>
    private static (DiagramDocument? Revised, string? Problem) Revise(DiagramDocument working, string plantUml)
    {
        try
        {
            var import = PlantUmlImporter.Import(plantUml, working.Name);
            if (import.Documents.Count == 0)
            {
                return (null, "The diagram in the answer could not be read, so nothing was changed.");
            }

            var revised = DiagramMerge.Merge(working, import.Documents[0]);
            return DiagramChanges.Describe(working, revised).Count == 0 ? (null, null) : (revised, null);
        }
        catch (InvalidDataException)
        {
            return (null, "The diagram in the answer could not be read, so nothing was changed.");
        }
        catch (AiException ex)
        {
            return (null, ex.Message);
        }
    }

    /// <summary>The answer without the diagram it may hold (whole-diagram mode), which the preview shows instead.</summary>
    internal static string Visible(string answer)
    {
        var start = answer.IndexOf("@startuml", StringComparison.OrdinalIgnoreCase);
        if (start < 0)
        {
            return answer;
        }

        var end = answer.IndexOf("@enduml", start, StringComparison.OrdinalIgnoreCase);
        var after = end < 0 ? string.Empty : answer[(end + "@enduml".Length)..];
        var before = answer[..start];
        var (said, more) = (Fence().Replace(before, string.Empty).Trim(), Fence().Replace(after, string.Empty, 1).Trim());
        return said.Length > 0 && more.Length > 0 ? said + "\n\n" + more : said + more;
    }

    /// <summary>A code fence around the diagram: at the end of the text before it, or at the start of the text after it.</summary>
    [GeneratedRegex(@"(```[a-zA-Z]*\s*$)|(^\s*```)")]
    private static partial Regex Fence();

    /// <summary>The changes not applied yet, against the diagram as it is.</summary>
    public IReadOnlyList<string> DescribeChanges(DiagramDocument diagram) =>
        WorkingCopy is null ? [] : DiagramChanges.Describe(diagram, WorkingCopy);

    /// <summary>The working copy, to apply to the diagram; the AI is told, with the next message.</summary>
    public DiagramDocument? TakeChanges()
    {
        var copy = WorkingCopy;
        WorkingCopy = null;
        if (copy is not null)
        {
            _pendingNote = "The user applied your changes.";
        }

        return copy;
    }

    /// <summary>Drops the changes not applied; the AI is told, with the next message.</summary>
    public void Discard()
    {
        if (WorkingCopy is not null)
        {
            WorkingCopy = null;
            _pendingNote = "The user discarded your changes; the diagram is as it was before them.";
        }
    }

    /// <summary>The system prompt: the task, the rules, how to change the diagram, and the diagram as it is now.</summary>
    internal static string Instructions(DiagramDocument diagram, DiagramTools? tools, bool whole)
    {
        var text = new StringBuilder("""
            You are the assistant of UML Wizard, a UML designer. You talk with the user about the diagram below: you answer
            questions about it, explain it, review it and suggest improvements. Keep answers short and concrete, in the
            language the user writes in. Write Markdown: short paragraphs, lists, **bold**, `code`; no tables.

            """);
        if (whole)
        {
            text.Append($"""

                When the user asks for a change, answer with one or two sentences on what you changed, then the whole revised
                diagram in one @startuml … @enduml block, in the PlantUML of the diagram below. Keep the names of the elements
                you do not change exactly as they are: UML Wizard matches them by name, keeps their place, and places what is
                new. Leave out what should be removed. Do not position or style anything. Include the block only when you
                change the diagram. The PlantUML UML Wizard reads for this kind of diagram:
                {AiDiagramGenerator.SyntaxOf(diagram.DiagramType)}
                Your changes are shown to the user, who applies or discards them, so say what you changed rather than claiming
                it is done.

                """);
        }
        else if (tools is not null)
        {
            text.Append('\n').Append(tools.Guidance).Append("""

                Do not position anything: UML Wizard places what you add. Your changes are shown to the user, who applies or
                discards them, so say what you changed rather than claiming it is done. If a tool reports an error, correct
                the call or explain.

                """);
        }

        text.Append("\nThe diagram \"").Append(diagram.Name).Append('"');
        if (!string.IsNullOrWhiteSpace(diagram.Description))
        {
            text.Append(", described as: ").Append(diagram.Description.Trim());
        }

        text.Append(", in PlantUML:\n\n").Append(PlantUmlExporter.Export(diagram).Text);
        if (!whole && tools?.Outline() is { } outline)
        {
            text.Append("\n\n").Append(outline);
        }

        return text.ToString();
    }
}
