using System.Text.RegularExpressions;
using UmlWizard.Core.Model;
using UmlWizard.Core.Rendering;

namespace UmlWizard.PlantUml;

/// <summary>
/// Reads a PlantUML activity diagram into a graph of actions and control flows. The activity syntax is structured:
/// <c>if … else … endif</c> becomes a decision and a merge, <c>fork … end fork</c> a fork and a join bar, <c>while</c>
/// and <c>repeat</c> a decision with a flow back to the start of the loop; the labels of the branches are the guards.
/// The older syntax (<c>(*) --&gt; "Action"</c>) is read too.
/// </summary>
internal sealed partial class ActivityDiagramReader(string name) : DiagramReader(name, DiagramType.Activity)
{
    /// <summary>The open blocks: if, switch, fork, while and repeat.</summary>
    private sealed class Block(string kind, GraphNode node)
    {
        public string Kind { get; } = kind;

        /// <summary>The decision or fork bar the block starts with.</summary>
        public GraphNode Node { get; } = node;

        /// <summary>Where the branches ended so far.</summary>
        public List<(GraphNode Node, string? Guard)> Ends { get; } = [];

        public bool HasElse { get; set; }

        /// <summary>For repeat: the first node of the loop, which the flow comes back to.</summary>
        public GraphNode? LoopStart { get; set; }

        /// <summary>For repeat: the action on the way back (backward :text;).</summary>
        public GraphNode? Backward { get; set; }
    }

    private readonly Stack<Block> _blocks = new();
    private readonly Dictionary<string, GraphNode> _legacyNodes = new(StringComparer.Ordinal);
    private List<(GraphNode Node, string? Guard)> _tails = [];
    private GraphNode? _lastAction;
    private GraphNode? _legacyLast;
    private string? _pendingLabel;

    private GraphDiagramModel Model => (GraphDiagramModel)Document.Model;

    protected override bool ReadLine(List<string> lines, ref int i)
    {
        var line = lines[i];
        if (LegacyArrow().Match(line) is { Success: true } legacy && (line.Contains("(*)") || _legacyNodes.Count > 0 || line.StartsWith('"')))
        {
            ReadLegacy(legacy);
            return true;
        }

        var lower = line.ToLowerInvariant();
        switch (lower)
        {
            case "start":
                Connect(Add(GraphNodeKind.InitialNode));
                return true;
            case "stop" or "end":
                Connect(Add(GraphNodeKind.ActivityFinal));
                _tails = [];
                return true;
            case "kill" or "detach":
                _tails = [];
                return true;
            case "fork" or "split":
                var bar = Add(GraphNodeKind.ForkJoin);
                Connect(bar);
                StartBlock("fork", bar);
                return true;
            case "fork again" or "split again":
                if (Top("fork") is { } fork)
                {
                    fork.Ends.AddRange(_tails);
                    _tails = [(fork.Node, null)];
                }

                return true;
            case "repeat":
                _blocks.Push(new Block("repeat", null!));
                return true;
            case "endif" or "end if" or "endswitch" or "end switch":
                EndBranches(lower.Contains("switch") ? "switch" : "if");
                return true;
            case "break":
                Lose("break", "break was left out", "breaks were left out");
                return true;
        }

        if (EndFork().IsMatch(line))
        {
            if (Pop("fork") is { } fork)
            {
                fork.Ends.AddRange(_tails);
                var join = Add(GraphNodeKind.ForkJoin);
                _tails = fork.Ends;
                Connect(join);
            }

            return true;
        }

        if (line.StartsWith(':'))
        {
            ReadAction(lines, ref i);
            return true;
        }

        if (IfLine().Match(line) is { Success: true } condition)
        {
            var decision = Add(GraphNodeKind.Decision);
            Connect(decision);
            StartBlock("if", decision, Guard(condition.Groups["label"].Value, condition.Groups["condition"].Value));
            return true;
        }

        if (ElseIfLine().Match(line) is { Success: true } elseIf && Top("if") is { } open)
        {
            open.Ends.AddRange(_tails);
            _tails = [(open.Node, Guard(elseIf.Groups["label"].Value, elseIf.Groups["condition"].Value))];
            return true;
        }

        if (ElseLine().Match(line) is { Success: true } otherwise && Top("if") is { } ifBlock)
        {
            ifBlock.Ends.AddRange(_tails);
            ifBlock.HasElse = true;
            _tails = [(ifBlock.Node, Guard(otherwise.Groups["label"].Value, null))];
            return true;
        }

        if (SwitchLine().IsMatch(line))
        {
            var decision = Add(GraphNodeKind.Decision);
            Connect(decision);
            StartBlock("switch", decision);
            _tails = [];
            return true;
        }

        if (CaseLine().Match(line) is { Success: true } branch && Top("switch") is { } switchBlock)
        {
            switchBlock.Ends.AddRange(_tails);
            switchBlock.HasElse = true;
            _tails = [(switchBlock.Node, Guard(branch.Groups["label"].Value, null))];
            return true;
        }

        if (WhileLine().Match(line) is { Success: true } loop)
        {
            var decision = Add(GraphNodeKind.Decision);
            Connect(decision);
            StartBlock("while", decision, Guard(loop.Groups["label"].Value, loop.Groups["condition"].Value));
            return true;
        }

        if (EndWhileLine().Match(line) is { Success: true } endWhile)
        {
            if (Pop("while") is { } whileBlock)
            {
                Connect(whileBlock.Node);
                _tails = [(whileBlock.Node, Guard(endWhile.Groups["label"].Value, null))];
            }

            return true;
        }

        if (RepeatWhileLine().Match(line) is { Success: true } repeatWhile)
        {
            if (Pop("repeat") is { } repeat)
            {
                var decision = Add(GraphNodeKind.Decision);
                Connect(decision);
                var back = Guard(repeatWhile.Groups["yes"].Value, repeatWhile.Groups["condition"].Value);
                if (repeat.LoopStart is { } start)
                {
                    // Back to the start, through the backward action if there is one.
                    var via = repeat.Backward ?? start;
                    Model.Edges.Add(new GraphEdge { Kind = GraphEdgeKind.ControlFlow, SourceId = decision.Id, TargetId = via.Id, Label = back });
                    if (repeat.Backward is { } backward)
                    {
                        Model.Edges.Add(new GraphEdge { Kind = GraphEdgeKind.ControlFlow, SourceId = backward.Id, TargetId = start.Id });
                    }
                }

                _tails = [(decision, Guard(repeatWhile.Groups["no"].Value, null))];
            }

            return true;
        }

        if (BackwardLine().Match(line) is { Success: true } backwardAction && Top("repeat") is { } open2)
        {
            // An action on the way back to the start of the loop: not part of the flow forward.
            open2.Backward = new GraphNode { Kind = GraphNodeKind.Action, Name = backwardAction.Groups["text"].Value.Replace("\\n", "\n").Trim() };
            Model.Nodes.Add(open2.Backward);
            return true;
        }

        if (RepeatAction().Match(line) is { Success: true } repeatWithAction)
        {
            _blocks.Push(new Block("repeat", null!));
            var action = Add(GraphNodeKind.Action, repeatWithAction.Groups["text"].Value.Trim());
            Connect(action);
            return true;
        }

        if (ArrowLabel().Match(line) is { Success: true } arrow)
        {
            var text = arrow.Groups["text"].Value.Trim();
            _pendingLabel = text.Length > 0 ? text : null;
            return true;
        }

        if (ReadNote(lines, ref i) is { } note)
        {
            AddNote(note.Text, note.Position == "floating" || _lastAction is null ? [] : [_lastAction.Id]);
            return true;
        }

        if (SwimlaneLine().IsMatch(line))
        {
            Lose("swimlane", "swimlane was left out", "swimlanes were left out");
            return true;
        }

        if (PartitionLine().IsMatch(line) || line is "}")
        {
            if (line != "}")
            {
                Lose("partition", "partition or group was left out; its actions were kept", "partitions and groups were left out; their actions were kept");
            }

            return true;
        }

        if (GotoLine().IsMatch(line))
        {
            Lose("goto", "label or goto was left out", "labels and gotos were left out");
            return true;
        }

        return false;
    }

    protected override void Finish()
    {
        GraphDiagramLayout.Arrange(Document, new Dictionary<string, string>(), LeftToRight);
        PlaceNotes();
    }

    /// <summary>An action: <c>:text;</c>, possibly over several lines, ending with <c>;</c> (or another shape's end).</summary>
    private void ReadAction(List<string> lines, ref int i)
    {
        var text = lines[i][1..];
        while (!ActionEnd().IsMatch(text) && i + 1 < lines.Count)
        {
            text += "\n" + lines[++i];
        }

        text = ActionEnd().Replace(text, string.Empty);
        var action = Add(GraphNodeKind.Action, text.Replace("\\n", "\n").Trim());
        Connect(action);
    }

    private void StartBlock(string kind, GraphNode node, string? guard = null)
    {
        _blocks.Push(new Block(kind, node));
        _tails = [(node, guard)];
    }

    /// <summary>The end of an if or switch: the branches meet at a merge decision.</summary>
    private void EndBranches(string kind)
    {
        if (Pop(kind) is not { } block)
        {
            return;
        }

        block.Ends.AddRange(_tails);
        if (!block.HasElse)
        {
            // Without else, the flow may also go past the branches.
            block.Ends.Add((block.Node, null));
        }

        if (block.Ends.Count > 1)
        {
            _tails = block.Ends;
            var merge = Add(GraphNodeKind.Decision);
            Connect(merge);
        }
        else
        {
            _tails = block.Ends;
        }
    }

    private Block? Top(string kind) => _blocks.Count > 0 && _blocks.Peek().Kind == kind ? _blocks.Peek() : null;

    private Block? Pop(string kind) => Top(kind) is not null ? _blocks.Pop() : null;

    private GraphNode Add(GraphNodeKind kind, string? text = null)
    {
        var node = new GraphNode { Kind = kind, Name = kind == GraphNodeKind.Action ? text : null };
        Model.Nodes.Add(node);
        if (kind == GraphNodeKind.Action)
        {
            _lastAction = node;
        }

        return node;
    }

    /// <summary>Connects the ends of the flow so far to <paramref name="node"/>, which becomes its end.</summary>
    private void Connect(GraphNode node)
    {
        foreach (var (tail, guard) in _tails)
        {
            Model.Edges.Add(new GraphEdge { Kind = GraphEdgeKind.ControlFlow, SourceId = tail.Id, TargetId = node.Id, Label = guard ?? Guard(_pendingLabel, null) });
        }

        _pendingLabel = null;
        _tails = [(node, null)];

        // The first node of a repeat loop is where the loop comes back to.
        foreach (var repeat in _blocks.Where(b => b.Kind == "repeat" && b.LoopStart is null))
        {
            repeat.LoopStart = node;
        }
    }

    /// <summary>
    /// A branch's guard: its label (<c>then (yes)</c>), else its condition, in brackets. A decision has no text of its
    /// own in UML, so a condition that has a label besides is left out.
    /// </summary>
    private string? Guard(string? label, string? condition)
    {
        label = label?.Trim();
        condition = condition?.Trim();
        if (!string.IsNullOrEmpty(label) && !string.IsNullOrEmpty(condition))
        {
            Lose("question", "condition of a decision was left out (UML decisions have no text); its branches keep their labels",
                "conditions of decisions were left out (UML decisions have no text); their branches keep their labels");
        }

        var text = !string.IsNullOrEmpty(label) ? label : condition;
        if (string.IsNullOrEmpty(text))
        {
            return null;
        }

        text = text.Replace("\\n", "\n");
        return text.StartsWith('[') && text.EndsWith(']') ? text : $"[{text}]";
    }

    #region The older syntax

    /// <summary><c>(*) --&gt; "Review cart"</c>, <c>"Pay" --&gt;[ok] ===B1===</c>, <c>--&gt; (*)</c>.</summary>
    private void ReadLegacy(Match arrow)
    {
        var left = arrow.Groups["left"].Success ? LegacyNode(arrow.Groups["left"].Value, source: true, null) : _legacyLast;
        var right = LegacyNode(arrow.Groups["right"].Value, source: false, arrow.Groups["alias"].Success ? arrow.Groups["alias"].Value : null);
        if (left is not null)
        {
            var guard = arrow.Groups["guard"].Value.Trim();
            Model.Edges.Add(new GraphEdge
            {
                Kind = GraphEdgeKind.ControlFlow,
                SourceId = left.Id,
                TargetId = right.Id,
                Label = guard.Length > 0 ? $"[{guard}]" : null,
            });
        }

        _legacyLast = right;
    }

    private GraphNode LegacyNode(string text, bool source, string? alias)
    {
        text = text.Trim();
        var key = text == "(*)" ? (source ? "(*start)" : "(*end)")
            : text.StartsWith("===") ? "bar:" + text.Trim('=', ' ')
            : text.StartsWith('"') ? "action:" + PlantUmlText.Unquote(text)
            : "alias:" + text;
        if (_legacyNodes.TryGetValue(key, out var node))
        {
            return node;
        }

        node = key switch
        {
            "(*start)" => Add(GraphNodeKind.InitialNode),
            "(*end)" => Add(GraphNodeKind.ActivityFinal),
            _ when key.StartsWith("bar:") => Add(GraphNodeKind.ForkJoin),
            _ => Add(GraphNodeKind.Action, key.StartsWith("action:") ? key["action:".Length..] : text),
        };
        _legacyNodes[key] = node;
        if (alias is not null)
        {
            _legacyNodes["alias:" + alias] = node;
        }

        return node;
    }

    #endregion

    [GeneratedRegex(@"^if\s*\((?<condition>.*?)\)\s*(?:is\s*\((?<label>[^)]*)\)\s*)?then\s*(?:\((?<label>[^)]*)\))?\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex IfLine();

    [GeneratedRegex(@"^else\s*if\s*\((?<condition>.*?)\)\s*(?:is\s*\((?<label>[^)]*)\)\s*)?then\s*(?:\((?<label>[^)]*)\))?\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex ElseIfLine();

    [GeneratedRegex(@"^else\s*(?:\((?<label>[^)]*)\))?\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex ElseLine();

    [GeneratedRegex(@"^switch\s*\(", RegexOptions.IgnoreCase)]
    private static partial Regex SwitchLine();

    [GeneratedRegex(@"^case\s*\((?<label>[^)]*)\)\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex CaseLine();

    [GeneratedRegex(@"^while\s*\((?<condition>.*?)\)\s*(?:is\s*\((?<label>[^)]*)\))?\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex WhileLine();

    [GeneratedRegex(@"^end\s*while\s*(?:\((?<label>[^)]*)\))?\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex EndWhileLine();

    [GeneratedRegex(@"^repeat\s*while\s*\((?<condition>.*?)\)\s*(?:is\s*\((?<yes>[^)]*)\))?\s*(?:not\s*\((?<no>[^)]*)\))?\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex RepeatWhileLine();

    [GeneratedRegex(@"^repeat\s*:(?<text>.*);$", RegexOptions.IgnoreCase)]
    private static partial Regex RepeatAction();

    [GeneratedRegex(@"^backward\s*:(?<text>.*);$", RegexOptions.IgnoreCase)]
    private static partial Regex BackwardLine();

    [GeneratedRegex(@"^end\s*(?:fork|merge|split)\b.*$", RegexOptions.IgnoreCase)]
    private static partial Regex EndFork();

    [GeneratedRegex(@"[;|<>/\]}]\s*$")]
    private static partial Regex ActionEnd();

    [GeneratedRegex(@"^-(?:\[[^\]]*\])?->\s*(?<text>[^;]*);?$")]
    private static partial Regex ArrowLabel();

    [GeneratedRegex(@"^\|(?:#?[\w#]+\|)?[^|]+\|$")]
    private static partial Regex SwimlaneLine();

    [GeneratedRegex(@"^(?:partition|group|package|rectangle|card)\b.*|^end\s*group$", RegexOptions.IgnoreCase)]
    private static partial Regex PartitionLine();

    [GeneratedRegex(@"^(?:label|goto)\s+\w+", RegexOptions.IgnoreCase)]
    private static partial Regex GotoLine();

    [GeneratedRegex(@"^(?:(?<left>\(\*\)|""[^""]*""|===\s*\w+\s*===|\w+)\s*)?-+(?:left|right|up|down|l|r|u|d)?-*>\s*(?:\[(?<guard>[^\]]*)\])?\s*(?<right>\(\*\)|""[^""]*""|===\s*\w+\s*===|\w+)(?:\s+as\s+(?<alias>\w+))?$", RegexOptions.IgnoreCase)]
    private static partial Regex LegacyArrow();
}
