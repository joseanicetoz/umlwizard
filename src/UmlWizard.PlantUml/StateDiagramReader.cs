using System.Text.RegularExpressions;
using UmlWizard.Core.Model;
using UmlWizard.Core.Rendering;

namespace UmlWizard.PlantUml;

/// <summary>
/// Reads a PlantUML state diagram: states, choices, <c>[*]</c> (the initial state before an arrow, the final state after
/// one) and transitions with their labels. Composite states are flattened: their states are kept, beside the composite
/// state, and each <c>[*]</c> inside one is its own initial or final state.
/// </summary>
internal sealed partial class StateDiagramReader(string name) : DiagramReader(name, DiagramType.StateMachine)
{
    private readonly Dictionary<string, GraphNode> _byAlias = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _notes = new(StringComparer.Ordinal);
    private readonly Dictionary<string, (GraphNode? Initial, GraphNode? Final)> _pseudostates = [];
    private readonly List<string> _scopes = [string.Empty];
    private string? _lastState;

    private GraphDiagramModel Model => (GraphDiagramModel)Document.Model;

    protected override bool ReadLine(List<string> lines, ref int i)
    {
        var line = lines[i];
        if (line == "}")
        {
            if (_scopes.Count > 1)
            {
                _scopes.RemoveAt(_scopes.Count - 1);
            }

            return true;
        }

        if (line is "--" or "||")
        {
            Lose("concurrent", "concurrent region separator (-- or ||) was left out", "concurrent region separators (-- or ||) were left out");
            return true;
        }

        if (ReadNote(lines, ref i) is { } note)
        {
            var targets = note.Targets.Select(t => _byAlias.TryGetValue(t, out var n) ? n.Id : null).OfType<string>().ToList();
            if (note.ToLast && _lastState is not null)
            {
                targets.Add(_lastState);
            }

            var added = AddNote(note.Text, targets);
            if (note.Alias is { } alias)
            {
                _notes[alias] = added.Id;
            }

            return true;
        }

        if (StateLine().Match(line) is { Success: true } declaration)
        {
            ReadState(declaration);
            return true;
        }

        if (PlantUmlText.ParseRelation(line) is { } relation)
        {
            return ReadTransition(relation);
        }

        // State : description, text shown inside a state.
        if (DescriptionLine().Match(line) is { Success: true } description && _byAlias.ContainsKey(description.Groups["state"].Value))
        {
            Lose("description", "line of a state's description (State : text) was left out", "lines of states' descriptions (State : text) were left out");
            return true;
        }

        return false;
    }

    protected override void Finish()
    {
        GraphDiagramLayout.Arrange(Document, new Dictionary<string, string>(), LeftToRight);
        PlaceNotes();
    }

    private void ReadState(Match declaration)
    {
        var rest = declaration.Groups["rest"].Value.Trim();
        var opens = rest.EndsWith('{');
        if (opens)
        {
            rest = rest[..^1].Trim();
        }

        string? stereotype = null;
        if (Stereotype().Match(rest) is { Success: true } s)
        {
            stereotype = s.Groups["text"].Value.Trim().ToLowerInvariant();
            rest = rest.Remove(s.Index, s.Length).Trim();
        }

        var colon = rest.IndexOf(':');
        if (colon >= 0 && !rest.StartsWith('"'))
        {
            Lose("description", "line of a state's description (State : text) was left out", "lines of states' descriptions (State : text) were left out");
            rest = rest[..colon].Trim();
        }

        rest = Color().Replace(rest, string.Empty).Trim();
        var (alias, display) = rest switch
        {
            _ when DisplayAsAlias().Match(rest) is { Success: true } a => (a.Groups["alias"].Value, PlantUmlText.Unquote(a.Groups["display"].Value)),
            _ when AliasAsDisplay().Match(rest) is { Success: true } b => (b.Groups["alias"].Value, PlantUmlText.Unquote(b.Groups["display"].Value)),
            _ => (PlantUmlText.Unquote(rest), PlantUmlText.Unquote(rest)),
        };

        switch (stereotype)
        {
            case "start":
                _byAlias[alias] = Pseudostate(initial: true);
                return;
            case "end":
                _byAlias[alias] = Pseudostate(initial: false);
                return;
            case "history" or "history*":
                Lose("history", "history state was left out", "history states were left out");
                return;
        }

        var node = Get(alias, display);
        if (stereotype is "choice" or "fork" or "join" or "entrypoint" or "exitpoint" or "inputpin" or "outputpin" or "expansioninput" or "expansionoutput")
        {
            node.Kind = GraphNodeKind.Choice;
            node.Name = null;
            if (stereotype is not "choice")
            {
                Lose("pseudostate", $"<<{stereotype}>> pseudostate is shown as a choice", "fork, join, entry and exit pseudostates are shown as choices");
            }
        }

        _lastState = node.Id;
        if (opens)
        {
            Lose("composite", "composite state was flattened: its states are shown beside it", "composite states were flattened: their states are shown beside them");
            _scopes.Add(node.Id);
        }
    }

    private bool ReadTransition(Relation relation)
    {
        // A dotted line from a note attaches it.
        foreach (var (note, other) in new[] { (relation.Left, relation.Right), (relation.Right, relation.Left) })
        {
            if (note.Kind == EndKind.Name && _notes.TryGetValue(note.Text, out var noteId))
            {
                if (other.Kind is EndKind.Name or EndKind.Quoted && _byAlias.TryGetValue(other.Text, out var target))
                {
                    Model.NoteAttachments.Add(new NoteAttachment { NoteId = noteId, TargetId = target.Id });
                }

                return true;
            }
        }

        var reversed = relation.Arrow.LeftHead.StartsWith('<') && relation.Arrow.RightHead.Length == 0;
        var (from, to) = reversed ? (relation.Right, relation.Left) : (relation.Left, relation.Right);
        if (End(from, source: true) is not { } source || End(to, source: false) is not { } target2)
        {
            return true;
        }

        var label = relation.Label?.Replace("\\n", "\n").Trim();
        Model.Edges.Add(new GraphEdge
        {
            Kind = GraphEdgeKind.Transition,
            SourceId = source.Id,
            TargetId = target2.Id,
            Label = string.IsNullOrEmpty(label) ? null : label,
        });
        return true;
    }

    /// <summary>The node an end of a transition is: [*] is the scope's initial state as a source, its final state as a target.</summary>
    private GraphNode? End(End end, bool source)
    {
        switch (end.Kind)
        {
            case EndKind.StartEnd:
                return Pseudostate(initial: source);
            case EndKind.Component when end.Text is "H" or "H*":
                Lose("history", "history state was left out", "history states were left out");
                return null;
            case EndKind.Name or EndKind.Quoted:
                return Get(end.Text, end.Text);
            default:
                return null;
        }
    }

    private GraphNode Pseudostate(bool initial)
    {
        var scope = _scopes[^1];
        var (first, last) = _pseudostates.GetValueOrDefault(scope);
        var node = initial ? first : last;
        if (node is null)
        {
            node = new GraphNode { Kind = initial ? GraphNodeKind.InitialState : GraphNodeKind.FinalState };
            Model.Nodes.Add(node);
            _pseudostates[scope] = initial ? (node, last) : (first, node);
        }

        return node;
    }

    private GraphNode Get(string alias, string display)
    {
        if (_byAlias.TryGetValue(alias, out var node))
        {
            if (display != alias && node.Kind == GraphNodeKind.State)
            {
                node.Name = display;
            }

            return node;
        }

        node = new GraphNode { Kind = GraphNodeKind.State, Name = display };
        Model.Nodes.Add(node);
        _byAlias[alias] = node;
        return node;
    }

    [GeneratedRegex(@"^state\s+(?<rest>.+)$", RegexOptions.IgnoreCase)]
    private static partial Regex StateLine();

    [GeneratedRegex(@"^(?<state>[\w.]+)\s*:\s*(?<text>.*)$")]
    private static partial Regex DescriptionLine();

    [GeneratedRegex(@"<<\s*(?<text>[^>]*)>>")]
    private static partial Regex Stereotype();

    [GeneratedRegex(@"\s+#\S+")]
    private static partial Regex Color();

    [GeneratedRegex(@"^(?<display>""[^""]+"")\s+as\s+(?<alias>[\w.]+)$")]
    private static partial Regex DisplayAsAlias();

    [GeneratedRegex(@"^(?<alias>[\w.]+)\s+as\s+(?<display>""[^""]+"")$")]
    private static partial Regex AliasAsDisplay();
}
