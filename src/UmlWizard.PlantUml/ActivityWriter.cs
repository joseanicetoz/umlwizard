using UmlWizard.Core.Model;

namespace UmlWizard.PlantUml;

/// <summary>
/// Writes an activity diagram in PlantUML's activity syntax, which is structured where the diagram is a graph: a
/// decision with several outgoing flows becomes <c>if … elseif … else … endif</c>, up to the node where the branches
/// meet again (the nearest node all of them lead to); a fork becomes <c>fork … fork again … end fork</c>; a loop becomes
/// <c>while … endwhile</c> (the decision at its top) or <c>repeat … repeat while</c> (the decision at its end). Guards
/// (<c>[guest]</c>) are the branches' labels. Flows the syntax cannot express are reported.
/// </summary>
internal sealed class ActivityWriter(GraphDiagramModel model, DiagramDocument document, Action<string> line, List<string> warnings)
{
    private readonly HashSet<string> _emitted = [];
    private readonly HashSet<string> _loops = [];
    private readonly HashSet<GraphEdge> _written = [];
    private HashSet<GraphEdge> _back = [];
    private Dictionary<string, int> _order = [];
    private ILookup<string, GraphEdge> _forward = null!;
    private ILookup<string, GraphEdge> _backInto = null!;
    private int _depth;

    public void Write()
    {
        var edges = model.Edges.Where(e => model.FindNode(e.SourceId) is not null && model.FindNode(e.TargetId) is not null).ToList();
        FindBackEdges(edges);
        _forward = edges.Where(e => !_back.Contains(e)).OrderBy(e => X(e.TargetId)).ToLookup(e => e.SourceId);
        _backInto = _back.ToLookup(e => e.TargetId);

        // Each flow from where it starts: initial nodes first, then nodes nothing leads to.
        var hasIncoming = edges.Where(e => !_back.Contains(e)).Select(e => e.TargetId).ToHashSet();
        var starts = model.Nodes.Where(n => n.Kind == GraphNodeKind.InitialNode)
            .Concat(model.Nodes.Where(n => !hasIncoming.Contains(n.Id)))
            .Concat(model.Nodes)
            .Distinct();
        var first = true;
        foreach (var start in starts.Where(n => !_emitted.Contains(n.Id)))
        {
            if (!first)
            {
                // A flow of its own, not connected to the one before.
                line("detach");
            }

            first = false;
            Emit(start.Id, stop: null);
        }

        foreach (var edge in edges.Where(e => !_written.Contains(e)))
        {
            warnings.Add($"The flow from {Describe(edge.SourceId)} to {Describe(edge.TargetId)} was not exported: " +
                "PlantUML's activity syntax only has structured flows (if, fork, while, repeat).");
        }

        WriteFloatingNotes();
    }

    /// <summary>Writes the flow from <paramref name="id"/> until it reaches <paramref name="stop"/> or ends.</summary>
    private void Emit(string? id, string? stop)
    {
        while (id is not null && id != stop)
        {
            if (_backInto[id].Any() && _loops.Add(id) && TryLoop(id, out var next))
            {
                id = next;
                continue;
            }

            if (!_emitted.Add(id))
            {
                // A flow into a node written before (not the end of a branch): the syntax has no jumps.
                return;
            }

            var node = model.FindNode(id)!;
            switch (node.Kind)
            {
                case GraphNodeKind.InitialNode:
                    Line("start");
                    break;
                case GraphNodeKind.ActivityFinal:
                    Line("stop");
                    return;
                case GraphNodeKind.Action:
                    WriteAction(node);
                    break;
            }

            var outgoing = _forward[id].ToList();
            if (outgoing.Count == 0)
            {
                return;
            }

            if (outgoing.Count == 1)
            {
                _written.Add(outgoing[0]);
                id = outgoing[0].TargetId;
                continue;
            }

            id = Split(node, outgoing);
        }
    }

    /// <summary>Writes the branches of a decision or fork and returns the node where they meet again, if any.</summary>
    private string? Split(GraphNode node, List<GraphEdge> outgoing)
    {
        var merge = Merge(outgoing.Select(e => e.TargetId).ToList());
        if (node.Kind == GraphNodeKind.ForkJoin)
        {
            for (var i = 0; i < outgoing.Count; i++)
            {
                Line(i == 0 ? "fork" : "fork again");
                Branch(outgoing[i], merge);
            }

            Line("end fork");
        }
        else
        {
            if (node.Kind == GraphNodeKind.Action)
            {
                warnings.Add($"{Describe(node.Id)} has several outgoing flows; they are written as a decision after it.");
            }

            for (var i = 0; i < outgoing.Count; i++)
            {
                var guard = Guard(outgoing[i]);
                Line(i == 0 ? $"if () then{Parenthesized(guard)}"
                    : i < outgoing.Count - 1 ? $"elseif () then{Parenthesized(guard)}"
                    : $"else{Parenthesized(guard)}");
                Branch(outgoing[i], merge);
            }

            Line("endif");
        }

        if (merge is null)
        {
            return null;
        }

        // The decision or join bar where the branches meet is the endif or end fork itself.
        var mergeNode = model.FindNode(merge)!;
        var next = _forward[merge].ToList();
        if (mergeNode.Kind is GraphNodeKind.Decision or GraphNodeKind.ForkJoin && next.Count <= 1 && !_backInto[merge].Any() && _emitted.Add(merge))
        {
            if (next.Count == 0)
            {
                return null;
            }

            _written.Add(next[0]);
            return next[0].TargetId;
        }

        return merge;
    }

    private void Branch(GraphEdge edge, string? merge)
    {
        _written.Add(edge);
        _depth++;
        Emit(edge.TargetId, merge);
        _depth--;
    }

    /// <summary>
    /// Writes the loop that starts at <paramref name="here"/>, the target of flows back from later in the diagram, and
    /// gives the node where the loop is left in <paramref name="next"/>. Returns false when the loop is not one the
    /// syntax has.
    /// </summary>
    private bool TryLoop(string here, out string? next)
    {
        next = null;
        var header = model.FindNode(here)!;
        var backs = _backInto[here].ToList();
        var outgoing = _forward[here].ToList();

        // while: the decision at the top leads into the body, which comes back to it, and out of the loop.
        if (header.Kind == GraphNodeKind.Decision && outgoing.Count == 2)
        {
            var sources = backs.Select(b => b.SourceId).ToHashSet();
            var bodies = outgoing.Where(e => Reach(e.TargetId).Overlaps(sources) || sources.Contains(here)).ToList();
            if (bodies.Count == 1)
            {
                var body = bodies[0];
                var exit = outgoing.Single(e => e != body);
                _emitted.Add(here);
                _written.Add(body);
                _written.UnionWith(backs);
                Line($"while (){(Guard(body) is { Length: > 0 } g ? $" is ({g})" : string.Empty)}");
                _depth++;
                Emit(body.TargetId, here);
                _depth--;
                Line("endwhile" + Parenthesized(Guard(exit)));
                _written.Add(exit);
                next = exit.TargetId;
                return true;
            }
        }

        // repeat: the body starts here, and a decision at its end comes back here or leaves the loop.
        if (backs.Count == 1 && model.FindNode(backs[0].SourceId) is { Kind: GraphNodeKind.Decision } decision &&
            _forward[decision.Id].Count() == 1 && decision.Id != here)
        {
            var back = backs[0];
            var exit = _forward[decision.Id].Single();
            Line("repeat");
            _depth++;
            var start = here;
            if (header.Kind == GraphNodeKind.Decision && outgoing.Count == 1)
            {
                // The decision where the loop is entered and re-entered is the repeat itself.
                _emitted.Add(start);
                _written.Add(outgoing[0]);
                start = outgoing[0].TargetId;
            }

            Emit(start, decision.Id);
            _depth--;
            _emitted.Add(decision.Id);
            _written.Add(back);
            _written.Add(exit);
            var yes = Guard(back);
            var no = Guard(exit);
            Line($"repeat while (){(yes.Length > 0 ? $" is ({yes})" : string.Empty)}{(no.Length > 0 ? $" not ({no})" : string.Empty)}");
            next = exit.TargetId;
            return true;
        }

        return false;
    }

    /// <summary>The node the branches meet at again: the earliest node every one of them leads to; null for none.</summary>
    private string? Merge(List<string> targets)
    {
        HashSet<string>? common = null;
        foreach (var target in targets)
        {
            var reach = Reach(target);
            common = common is null ? reach : [.. common.Intersect(reach)];
        }

        return common?.OrderBy(n => _order.GetValueOrDefault(n, int.MaxValue)).FirstOrDefault();
    }

    /// <summary>The nodes reachable from <paramref name="id"/> (itself included) along forward flows.</summary>
    private HashSet<string> Reach(string id)
    {
        var reach = new HashSet<string>();
        var stack = new Stack<string>([id]);
        while (stack.TryPop(out var current))
        {
            if (reach.Add(current))
            {
                foreach (var edge in _forward[current])
                {
                    stack.Push(edge.TargetId);
                }
            }
        }

        return reach;
    }

    /// <summary>
    /// Finds the flows that go back to a node already on the way there (by a depth-first search from the start), and
    /// the order of the nodes along the remaining flows.
    /// </summary>
    private void FindBackEdges(List<GraphEdge> edges)
    {
        var outgoing = edges.OrderBy(e => X(e.TargetId)).ToLookup(e => e.SourceId);
        var state = new Dictionary<string, int>();
        var back = new HashSet<GraphEdge>();
        var postOrder = new List<string>();
        var roots = model.Nodes.Where(n => n.Kind == GraphNodeKind.InitialNode).Concat(model.Nodes);
        foreach (var root in roots)
        {
            Visit(root.Id);
        }

        void Visit(string id)
        {
            if (state.ContainsKey(id))
            {
                return;
            }

            state[id] = 1;
            foreach (var edge in outgoing[id])
            {
                if (state.GetValueOrDefault(edge.TargetId) == 1)
                {
                    back.Add(edge);
                }
                else
                {
                    Visit(edge.TargetId);
                }
            }

            state[id] = 2;
            postOrder.Add(id);
        }

        _back = back;
        postOrder.Reverse();
        _order = postOrder.Select((id, i) => (id, i)).ToDictionary(p => p.id, p => p.i);
    }

    private void WriteAction(GraphNode node)
    {
        var lines = (node.Name ?? string.Empty).Replace("\r\n", "\n").Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            Line((i == 0 ? ":" : string.Empty) + lines[i] + (i == lines.Length - 1 ? ";" : string.Empty));
        }

        foreach (var note in NotesOf(node.Id))
        {
            NoteBlock("note right", note.Text);
        }
    }

    /// <summary>Notes that are not attached to an action are written as floating notes.</summary>
    private void WriteFloatingNotes()
    {
        var onActions = model.NoteAttachments
            .Where(a => model.FindNode(a.TargetId) is { Kind: GraphNodeKind.Action })
            .Select(a => a.NoteId)
            .ToHashSet();
        foreach (var note in model.Notes.Where(n => !onActions.Contains(n.Id)))
        {
            NoteBlock("floating note right", note.Text);
        }
    }

    private IEnumerable<NoteElement> NotesOf(string nodeId) =>
        model.Notes.Where(n => model.NoteAttachments.Any(a => a.NoteId == n.Id && a.TargetId == nodeId));

    private void NoteBlock(string header, string text)
    {
        var lines = text.Replace("\r\n", "\n").Split('\n');
        if (lines.Length == 1)
        {
            Line($"{header} : {text}");
            return;
        }

        Line(header);
        foreach (var l in lines)
        {
            Line(l);
        }

        Line("end note");
    }

    /// <summary>A guard without its brackets: <c>[guest]</c> is <c>guest</c>.</summary>
    private static string Guard(GraphEdge edge)
    {
        var label = (edge.Label ?? string.Empty).Trim();
        if (label.StartsWith('[') && label.EndsWith(']'))
        {
            label = label[1..^1].Trim();
        }

        return PlantUmlText.OneLine(label.Replace("(", "[").Replace(")", "]"));
    }

    private static string Parenthesized(string text) => text.Length == 0 ? string.Empty : $" ({text})";

    private string Describe(string id) => model.FindNode(id) is { } node
        ? node.Kind switch
        {
            GraphNodeKind.Action => $"\"{node.Name}\"",
            GraphNodeKind.InitialNode => "the start",
            GraphNodeKind.ActivityFinal => "the end",
            GraphNodeKind.Decision => "a decision",
            _ => "a fork or join bar",
        }
        : id;

    private double X(string id) => document.Layout.Find(id)?.X ?? 0;

    private void Line(string text) => line(new string(' ', _depth * 2) + text);
}
