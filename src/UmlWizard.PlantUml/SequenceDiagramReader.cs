using System.Text.RegularExpressions;
using UmlWizard.Core.Model;
using UmlWizard.Core.Notation;
using UmlWizard.Core.Rendering;

namespace UmlWizard.PlantUml;

/// <summary>
/// Reads a PlantUML sequence diagram: participants and actors (other kinds, such as <c>database</c>, become objects),
/// synchronous (<c>-&gt;</c>), asynchronous (<c>-&gt;&gt;</c>) and reply (<c>--&gt;</c>, <c>return</c>) messages,
/// <c>create</c>, and notes, in the order they are written. Combined fragments, boxes and references are left out.
/// </summary>
internal sealed partial class SequenceDiagramReader(string name) : DiagramReader(name, DiagramType.Sequence)
{
    private const double StepHeight = 44;

    private readonly Dictionary<string, Lifeline> _byAlias = new(StringComparer.Ordinal);
    private readonly HashSet<string> _toCreate = [];
    private readonly Stack<(Lifeline Caller, Lifeline Callee)> _calls = new();
    private readonly List<(ModelElement Element, bool IsSelf)> _steps = [];
    private SequenceMessage? _lastMessage;

    private SequenceDiagramModel Model => (SequenceDiagramModel)Document.Model;

    protected override bool ReadLine(List<string> lines, ref int i)
    {
        var line = lines[i];
        if (ParticipantLine().Match(line) is { Success: true } participant)
        {
            Declare(participant.Groups["keyword"].Value, participant.Groups["rest"].Value);
            return true;
        }

        if (CreateLine().Match(line) is { Success: true } create)
        {
            var lifeline = Declare(create.Groups["keyword"].Success ? create.Groups["keyword"].Value : "participant", create.Groups["rest"].Value);
            _toCreate.Add(lifeline.Id);
            return true;
        }

        if (SequenceReaderPatterns.Message().Match(line) is { Success: true } message && SequenceReaderPatterns.HasHead(message))
        {
            ReadMessage(message);
            return true;
        }

        if (ReturnLine().Match(line) is { Success: true } returned)
        {
            if (_calls.TryPop(out var call))
            {
                AddMessage(call.Callee, call.Caller, PlantUmlText.Unquote(returned.Groups["text"].Value), MessageKind.Reply);
            }

            return true;
        }

        if (ReadNote(lines, ref i) is { } note)
        {
            AddNote(note);
            return true;
        }

        if (FragmentLine().Match(line) is { Success: true } fragment)
        {
            // else and end belong to the fragment already counted.
            if (fragment.Value.ToLowerInvariant() is "else" or "end")
            {
                return true;
            }

            Lose("fragment", "combined fragment (alt, loop, opt, …) was left out; its messages were kept",
                "combined fragments (alt, loop, opt, …) were left out; their messages were kept");
            return true;
        }

        if (RefLine().Match(line) is { Success: true } reference)
        {
            Lose("ref", "reference (ref over) was left out", "references (ref over) were left out");
            if (!reference.Groups["text"].Success)
            {
                while (i + 1 < lines.Count && !EndRef().IsMatch(lines[++i]))
                {
                }
            }

            return true;
        }

        if (DestroyLine().IsMatch(line))
        {
            Lose("destroy", "destruction (destroy, !!) was left out", "destructions (destroy, !!) were left out");
            return true;
        }

        if (BoxLine().IsMatch(line))
        {
            Lose("box", "box around participants was left out", "boxes around participants were left out");
            return true;
        }

        return IgnoredLine().IsMatch(line);
    }

    protected override void Finish()
    {
        var layout = Document.Layout;
        foreach (var lifeline in Model.Lifelines)
        {
            layout.Add(new ElementLayout { ElementId = lifeline.Id, Y = SequenceDiagramRenderer.DefaultTop });
        }

        // The renderer sizes the heads; they are placed side by side.
        using (var renderer = DiagramRenderer.Create(DiagramType.Sequence))
        {
            renderer.UpdateLayout(Document);
        }

        var x = 40.0;
        foreach (var lifeline in Model.Lifelines)
        {
            var l = layout.Find(lifeline.Id)!;
            l.X = x;
            x += l.Width + 60;
        }

        // Messages and notes one below the other, in the order they are written.
        var headBottom = Model.Lifelines.Select(l => layout.Find(l.Id)!).Select(l => l.Y + l.Height).DefaultIfEmpty(SequenceDiagramRenderer.DefaultTop + 40).Max();
        var y = headBottom + 40;
        foreach (var (element, isSelf) in _steps)
        {
            var l = layout.Find(element.Id) ?? layout.Add(new ElementLayout { ElementId = element.Id });
            l.Y = y;
            if (element is NoteElement note)
            {
                var over = Model.NoteAttachments.Where(a => a.NoteId == note.Id).Select(a => Model.FindLifeline(a.TargetId)).OfType<Lifeline>()
                    .Select(t => layout.Find(t.Id)!).ToList();
                l.X = over.Count > 0 ? over.Min(o => o.X + o.Width / 2) + 10 : 40;
                y += Math.Max(NoteElement.DefaultHeight, note.Text.Split('\n').Length * 18 + 24) + 16;
            }
            else
            {
                y += StepHeight + (isSelf ? 16 : 0);
            }
        }

        // Notes attached to messages sit beside their message.
        foreach (var note in Model.Notes.Where(n => _steps.All(s => s.Element != n)))
        {
            var l = layout.Find(note.Id) ?? layout.Add(new ElementLayout { ElementId = note.Id });
            var message = Model.NoteAttachments.Where(a => a.NoteId == note.Id).Select(a => Model.Messages.Find(m => m.Id == a.TargetId)).OfType<SequenceMessage>().FirstOrDefault();
            if (message is not null)
            {
                l.Y = layout.Find(message.Id)!.Y - 10;
                l.X = Model.Lifelines.Select(t => layout.Find(t.Id)!).Max(t => t.X + t.Width) + 20;
            }
        }

        PlaceNotes();
    }

    private Lifeline Declare(string keyword, string rest)
    {
        rest = Order().Replace(rest, string.Empty);
        rest = Stereotype().Replace(rest, string.Empty);
        rest = Color().Replace(rest, string.Empty).Trim();
        string alias;
        string display;
        if (DisplayAsAlias().Match(rest) is { Success: true } a)
        {
            (alias, display) = (a.Groups["alias"].Value, PlantUmlText.Unquote(a.Groups["display"].Value));
        }
        else if (AliasAsDisplay().Match(rest) is { Success: true } b)
        {
            (alias, display) = (b.Groups["alias"].Value, PlantUmlText.Unquote(b.Groups["display"].Value));
        }
        else
        {
            alias = PlantUmlText.Unquote(rest);
            display = alias;
        }

        var lifeline = Get(alias, display);
        lifeline.Kind = keyword.Equals("actor", StringComparison.OrdinalIgnoreCase) ? LifelineKind.Actor : LifelineKind.Object;
        if (keyword.ToLowerInvariant() is not ("actor" or "participant"))
        {
            Lose("participant kind", $"participant drawn as a {keyword.ToLowerInvariant()} in PlantUML is shown as an object",
                "participants drawn as boundaries, controls, entities, databases, collections or queues in PlantUML are shown as objects");
        }

        return lifeline;
    }

    /// <summary>The lifeline of an alias, added on first use, as PlantUML adds participants it has not been told about.</summary>
    private Lifeline Get(string alias, string? display = null)
    {
        if (!_byAlias.TryGetValue(alias, out var lifeline))
        {
            lifeline = Model.Lifelines.FirstOrDefault(l => UmlNotation.FormatLifeline(l) == alias) ?? new Lifeline();
            if (!Model.Lifelines.Contains(lifeline))
            {
                Model.Lifelines.Add(lifeline);
                display ??= alias;
            }

            _byAlias[alias] = lifeline;
        }

        if (display is not null)
        {
            var (lifelineName, lifelineType) = UmlNotation.ParseLifeline(display);
            lifeline.Name = lifelineName;
            lifeline.Type = lifelineType;
        }

        return lifeline;
    }

    private void ReadMessage(Match match)
    {
        var left = match.Groups["left"].Value;
        var right = match.Groups["right"].Value;
        if (left is "[" or "]" or "?" || right is "[" or "]" or "?")
        {
            Lose("found", "message from or to outside the diagram ([->, ->]) was left out", "messages from or to outside the diagram ([->, ->]) were left out");
            return;
        }

        var leftHead = match.Groups["lh"].Value;
        var rightHead = match.Groups["rh"].Value;
        var reversed = leftHead is "<" or "<<" or "\\" or "\\\\" or "/" or "//" && rightHead.Length == 0;
        var head = reversed ? leftHead : rightHead;
        var dashed = match.Groups["line"].Value.Contains("--");
        var kind = dashed ? MessageKind.Reply
            : head is ">>" or "<<" or "\\" or "\\\\" or "/" or "//" ? MessageKind.Asynchronous
            : MessageKind.Synchronous;

        var from = Get(PlantUmlText.Unquote(reversed ? right : left));
        var to = Get(PlantUmlText.Unquote(reversed ? left : right));
        var activation = match.Groups["act"].Value + match.Groups["act2"].Value;
        if (activation.Contains("**") || _toCreate.Remove(to.Id))
        {
            kind = MessageKind.Create;
        }

        if (activation.Contains("!!"))
        {
            Lose("destroy", "destruction (destroy, !!) was left out", "destructions (destroy, !!) were left out");
        }

        AddMessage(from, to, PlantUmlText.Unquote(match.Groups["text"].Value), kind);
        if (kind == MessageKind.Synchronous && from != to)
        {
            _calls.Push((from, to));
        }
    }

    private void AddMessage(Lifeline from, Lifeline to, string text, MessageKind kind)
    {
        var message = new SequenceMessage { SourceId = from.Id, TargetId = to.Id, Text = text, Kind = kind };
        Model.Messages.Add(message);
        _steps.Add((message, from == to));
        _lastMessage = message;
    }

    private void AddNote(NoteSpec spec)
    {
        if (spec.ToLast && _lastMessage is not null)
        {
            // note left / note right just after a message comments on it.
            AddNote(spec.Text, [_lastMessage.Id]);
            return;
        }

        var targets = spec.Targets.Select(t => Get(t).Id).ToList();
        var note = AddNote(spec.Text, targets);
        _steps.Add((note, false));
    }

    [GeneratedRegex(@"^(?<keyword>participant|actor|boundary|control|entity|database|collections|queue)\s+(?<rest>.+)$", RegexOptions.IgnoreCase)]
    private static partial Regex ParticipantLine();

    [GeneratedRegex(@"^create\s+(?:(?<keyword>participant|actor|boundary|control|entity|database|collections|queue)\s+)?(?<rest>.+)$", RegexOptions.IgnoreCase)]
    private static partial Regex CreateLine();

    [GeneratedRegex(@"^return(?:\s+(?<text>.*))?$", RegexOptions.IgnoreCase)]
    private static partial Regex ReturnLine();

    [GeneratedRegex(@"^(?:alt|else|opt|loop|par|par2|break|critical|group|end)\b(?!\s*(?:box|ref|note))", RegexOptions.IgnoreCase)]
    private static partial Regex FragmentLine();

    [GeneratedRegex(@"^ref\s+over\b[^:]*(?::\s*(?<text>.*))?$", RegexOptions.IgnoreCase)]
    private static partial Regex RefLine();

    [GeneratedRegex(@"^end\s*ref$", RegexOptions.IgnoreCase)]
    private static partial Regex EndRef();

    [GeneratedRegex(@"^destroy\b", RegexOptions.IgnoreCase)]
    private static partial Regex DestroyLine();

    [GeneratedRegex(@"^(?:box\b.*|end\s*box)$", RegexOptions.IgnoreCase)]
    private static partial Regex BoxLine();

    [GeneratedRegex(@"^(?:activate|deactivate|autonumber|autoactivate|hide|show|==.*==|\.\.\..*|\|\|\|?|\|\|\d+\|\||delay\b.*|newpage\b.*|skinparam\b.*)", RegexOptions.IgnoreCase)]
    private static partial Regex IgnoredLine();

    [GeneratedRegex(@"\s+order\s+-?\d+", RegexOptions.IgnoreCase)]
    private static partial Regex Order();

    [GeneratedRegex(@"\s*<<[^>]*>>")]
    private static partial Regex Stereotype();

    [GeneratedRegex(@"\s+#\S+")]
    private static partial Regex Color();

    [GeneratedRegex(@"^(?<display>""[^""]+"")\s+as\s+(?<alias>[\w.$]+)$")]
    private static partial Regex DisplayAsAlias();

    [GeneratedRegex(@"^(?<alias>[\w.$]+)\s+as\s+(?<display>""[^""]+""|.+)$")]
    private static partial Regex AliasAsDisplay();
}

/// <summary>The message line of a sequence diagram, also used to recognise one.</summary>
internal static partial class SequenceReaderPatterns
{
    /// <summary>
    /// <c>A -&gt; B : text</c>, with the arrows PlantUML has: <c>-&gt;</c>, <c>-&gt;&gt;</c>, <c>--&gt;</c>, <c>&lt;-</c>, half
    /// arrows (<c>-\</c>, <c>-/</c>), lost and found ends (<c>o</c>, <c>x</c>, <c>[</c>, <c>]</c>), colours
    /// (<c>-[#red]&gt;</c>) and activation marks (<c>++</c>, <c>--</c>, <c>**</c>, <c>!!</c>).
    /// </summary>
    [GeneratedRegex(@"^(?<left>""[^""]+""|[\w.$]+|\[|\]|\?)\s*(?<lh><<|<|\\\\|\\|//|/)?(?<lx>[ox])?(?<line>-{1,2}(?:\[[^\]]*\])?-?)(?<rh>>>|>|\\\\|\\|//|/)?(?<rx>[ox])?\s*(?<right>""[^""]+""|[\w.$]+|\[|\]|\?)\s*(?<act>\+\+|--|\*\*|!!)?\s*(?<act2>\+\+|--)?\s*(?::\s*(?<text>.*))?$")]
    public static partial Regex Message();

    /// <summary>The line is a message: it matches <see cref="Message"/> and its arrow has a head (A -- B is not one).</summary>
    public static bool IsMessage(string line) => Message().Match(line) is { Success: true } match && HasHead(match);

    public static bool HasHead(Match match) => match.Groups["lh"].Length + match.Groups["rh"].Length + match.Groups["lx"].Length + match.Groups["rx"].Length > 0;
}
