using System.Text.RegularExpressions;
using UmlWizard.Core.Model;
using UmlWizard.Core.Rendering;

namespace UmlWizard.PlantUml;

/// <summary>
/// Reads what PlantUML calls description diagrams, as a use case, component or deployment diagram: actors, use cases,
/// components, interfaces, nodes and artifacts (by keyword, or written <c>:Actor:</c>, <c>(Use case)</c>,
/// <c>[Component]</c>, <c>() Interface</c>), containers in braces (a <c>rectangle</c> is a system boundary, a
/// <c>node</c> a deployment node, a device or an execution environment by its stereotype), and the relations between them.
/// </summary>
internal sealed partial class DescriptionDiagramReader(string name, DiagramType type) : DiagramReader(name, type)
{
    private readonly Dictionary<string, GraphNode> _byAlias = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _notes = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _parentOf = [];
    private readonly List<string?> _containers = [];
    private string? _last;

    private GraphDiagramModel Model => (GraphDiagramModel)Document.Model;

    protected override bool ReadLine(List<string> lines, ref int i)
    {
        var line = lines[i];
        if (line == "}")
        {
            if (_containers.Count > 0)
            {
                _containers.RemoveAt(_containers.Count - 1);
            }

            return true;
        }

        if (ReadNote(lines, ref i) is { } note)
        {
            var targets = note.Targets.Select(t => Find(t)?.Id).OfType<string>().ToList();
            if (note.ToLast && _last is not null)
            {
                targets.Add(_last);
            }

            var added = AddNote(note.Text, targets);
            if (note.Alias is { } alias)
            {
                _notes[alias] = added.Id;
            }

            return true;
        }

        if (KeywordLine().Match(line) is { Success: true } declaration)
        {
            Declare(declaration.Groups["keyword"].Value.ToLowerInvariant(), declaration.Groups["rest"].Value.Trim());
            return true;
        }

        // Shorthands on a line of their own: :Actor:, (Use case), [Component], () Interface, with an alias or braces.
        if (ShorthandLine().Match(line) is { Success: true } shorthand)
        {
            var end = PlantUmlText.ParseRelation(shorthand.Groups["end"].Value + " -- X");
            if (end is not null)
            {
                var node = NodeOf(end.Left, shorthand.Groups["alias"].Success ? shorthand.Groups["alias"].Value : null);
                if (node is not null && line.TrimEnd().EndsWith('{'))
                {
                    _containers.Add(node.Id);
                }

                return node is not null;
            }
        }

        if (PlantUmlText.ParseRelation(line) is { } relation)
        {
            ReadRelation(relation);
            return true;
        }

        return false;
    }

    protected override void Finish()
    {
        GraphDiagramLayout.Arrange(Document, _parentOf, LeftToRight || Document.DiagramType == DiagramType.UseCase);
        PlaceNotes();
    }

    private void Declare(string keyword, string rest)
    {
        var opens = rest.EndsWith('{');
        if (opens)
        {
            rest = rest[..^1].Trim();
        }

        string? stereotype = null;
        if (Stereotype().Match(rest) is { Success: true } s)
        {
            stereotype = s.Groups["text"].Value.Trim().ToLowerInvariant().Replace(" ", string.Empty);
            rest = rest.Remove(s.Index, s.Length).Trim();
        }

        rest = Color().Replace(rest, string.Empty).Trim();
        var (alias, display) = NameAndAlias(rest);
        var kind = KindOf(keyword, stereotype);
        if (kind is null)
        {
            Lose("element", $"{keyword} was left out: it has no place in a {Document.DiagramType} diagram",
                "elements that have no place in this kind of diagram were left out");
            if (opens)
            {
                _containers.Add(null);
            }

            return;
        }

        var node = Get(alias, display, kind.Value);
        if (opens)
        {
            if (!GraphRules.IsContainer(node.Kind))
            {
                Lose("nested", "element nested in one that is not a container was placed beside it",
                    "elements nested in ones that are not containers were placed beside them");
            }

            _containers.Add(GraphRules.IsContainer(node.Kind) ? node.Id : _containers.LastOrDefault());
        }
    }

    /// <summary>
    /// The kind of node a keyword declares in this kind of diagram; null when it has none there. Keywords UML Wizard
    /// has no shape for become the closest kind, and are reported.
    /// </summary>
    private GraphNodeKind? KindOf(string keyword, string? stereotype)
    {
        var diagram = Document.DiagramType;
        GraphNodeKind? kind = keyword switch
        {
            "actor" or "person" => GraphNodeKind.Actor,
            "usecase" => GraphNodeKind.UseCase,
            "component" => GraphNodeKind.Component,
            "interface" => GraphNodeKind.Interface,
            "artifact" or "file" => GraphNodeKind.Artifact,
            "node" => stereotype switch
            {
                "device" => GraphNodeKind.Device,
                "executionenvironment" => GraphNodeKind.ExecutionEnvironment,
                _ => GraphNodeKind.Node,
            },
            "rectangle" or "package" or "frame" or "folder" or "cloud" when diagram == DiagramType.UseCase => GraphNodeKind.SystemBoundary,
            "rectangle" or "package" or "frame" or "folder" or "cloud" when diagram == DiagramType.Deployment => GraphNodeKind.Node,
            "database" or "storage" or "queue" or "stack" or "collections" or "agent" or "card" or "hexagon" or "boundary" or "control" or "entity"
                when diagram == DiagramType.Deployment => GraphNodeKind.Node,
            "database" or "storage" or "queue" or "stack" or "collections" or "agent" or "card" or "hexagon" or "boundary" or "control" or "entity"
                when diagram == DiagramType.Component => GraphNodeKind.Component,
            _ => null,
        };

        if (kind is not null && !GraphRules.NodeKinds(diagram).Contains(kind.Value))
        {
            return null;
        }

        if (kind is not null && keyword is not ("actor" or "usecase" or "component" or "interface" or "artifact" or "node" or "rectangle"))
        {
            Lose("shape", $"{keyword} is shown as a {Words(kind.Value)}", "elements PlantUML draws with other shapes are shown as the closest kind");
        }

        if (kind is null && keyword is "rectangle" or "package" or "frame" or "folder" or "cloud")
        {
            // A grouping the diagram has no container for: its content is kept.
            Lose("group", $"{keyword} was left out; its content was kept", "groupings (package, rectangle, …) were left out; their content was kept");
        }

        return kind;
    }

    /// <summary>A kind of node in words: SystemBoundary is "system boundary".</summary>
    private static string Words(GraphNodeKind kind) => KindWords().Replace(kind.ToString(), " $1").Trim().ToLowerInvariant();

    /// <summary>The alias and display name: <c>Name</c>, <c>"Display" as A</c>, <c>A as "Display"</c>, <c>[Display] as A</c>, <c>(Display) as A</c>.</summary>
    private static (string Alias, string Display) NameAndAlias(string text)
    {
        if (DisplayAsAlias().Match(text) is { Success: true } a)
        {
            return (a.Groups["alias"].Value, Unwrap(a.Groups["display"].Value));
        }

        if (AliasAsDisplay().Match(text) is { Success: true } b)
        {
            return (b.Groups["alias"].Value, Unwrap(b.Groups["display"].Value));
        }

        var display = Unwrap(text);
        return (display, display);
    }

    /// <summary>A name without the quotes, brackets or colons around it.</summary>
    private static string Unwrap(string text)
    {
        text = text.Trim();
        if (text.Length >= 2 && (text[0], text[^1]) is ('[', ']') or ('(', ')') or (':', ':'))
        {
            text = text[1..^1];
        }

        return PlantUmlText.Unquote(text);
    }

    private void ReadRelation(Relation relation)
    {
        // A dotted line from a note attaches it.
        foreach (var (note, other) in new[] { (relation.Left, relation.Right), (relation.Right, relation.Left) })
        {
            if (note.Kind == EndKind.Name && _notes.TryGetValue(note.Text, out var noteId))
            {
                if (NodeOf(other, null) is { } target)
                {
                    Model.NoteAttachments.Add(new NoteAttachment { NoteId = noteId, TargetId = target.Id });
                }

                return;
            }
        }

        if (NodeOf(relation.Left, null) is not { } left || NodeOf(relation.Right, null) is not { } right)
        {
            return;
        }

        // «include», «extend», «deploy» and «manifest» are written as the label's stereotype.
        var label = relation.Label ?? string.Empty;
        string? keyword = null;
        if (KeywordLabel().Match(label) is { Success: true } k)
        {
            keyword = k.Groups["keyword"].Value.ToLowerInvariant() is "extends" ? "extend" : k.Groups["keyword"].Value.ToLowerInvariant();
            label = label.Remove(k.Index, k.Length);
        }

        label = label.Trim().TrimStart('<').TrimEnd('>').Trim().Replace("\\n", "\n");
        var arrow = relation.Arrow;
        var rightIsTarget = !(arrow.LeftHead.Length > 0 && arrow.RightHead.Length == 0);
        var (source, target2) = rightIsTarget ? (left, right) : (right, left);
        var generalizes = arrow.LeftHead == "<|" || arrow.RightHead == "|>";
        var kind = Document.DiagramType switch
        {
            DiagramType.UseCase => keyword switch
            {
                "include" => GraphEdgeKind.Include,
                "extend" => GraphEdgeKind.Extend,
                _ when generalizes => GraphEdgeKind.Generalization,
                _ => GraphEdgeKind.Association,
            },
            DiagramType.Component => generalizes ? GraphEdgeKind.Realization : arrow.Dashed ? GraphEdgeKind.Dependency : GraphEdgeKind.Association,
            _ => keyword switch
            {
                "deploy" => GraphEdgeKind.Deploy,
                "manifest" => GraphEdgeKind.Manifest,
                _ when arrow.Dashed => GraphEdgeKind.Dependency,
                _ when GraphRules.IsDeploymentTarget(source.Kind) && GraphRules.IsDeploymentTarget(target2.Kind) => GraphEdgeKind.CommunicationPath,
                _ => GraphEdgeKind.Dependency,
            },
        };

        // An edge the diagram does not allow one way may be allowed the other way (e.g. an interface realized by a component).
        if (GraphRules.CheckEdge(Document.DiagramType, kind, source, target2) is not null)
        {
            if (GraphRules.CheckEdge(Document.DiagramType, kind, target2, source) is null)
            {
                (source, target2) = (target2, source);
            }
            else
            {
                Lose("edge", "relation that is not allowed between its elements was left out", "relations that are not allowed between their elements were left out");
                return;
            }
        }

        Model.Edges.Add(new GraphEdge { Kind = kind, SourceId = source.Id, TargetId = target2.Id, Label = label.Length > 0 ? label : null });
    }

    /// <summary>The node an end of a relation is, added when it is not declared yet, as PlantUML adds it.</summary>
    private GraphNode? NodeOf(End end, string? alias)
    {
        var kind = end.Kind switch
        {
            EndKind.Actor => GraphNodeKind.Actor,
            EndKind.UseCase => GraphNodeKind.UseCase,
            EndKind.Component => GraphNodeKind.Component,
            EndKind.Interface => GraphNodeKind.Interface,
            EndKind.Name or EndKind.Quoted => (GraphNodeKind?)null,
            _ => null,
        };
        if (end.Kind is not (EndKind.Actor or EndKind.UseCase or EndKind.Component or EndKind.Interface or EndKind.Name or EndKind.Quoted))
        {
            return null;
        }

        if (alias is null && Find(end.Text) is { } existing)
        {
            return existing;
        }

        kind ??= Document.DiagramType switch
        {
            DiagramType.UseCase => GraphNodeKind.Actor,
            DiagramType.Component => GraphNodeKind.Component,
            _ => GraphNodeKind.Node,
        };
        if (!GraphRules.NodeKinds(Document.DiagramType).Contains(kind.Value))
        {
            Lose("element", "element that has no place in this kind of diagram was left out", "elements that have no place in this kind of diagram were left out");
            return null;
        }

        return Get(alias ?? end.Text, end.Text, kind.Value);
    }

    /// <summary>A node by its alias or display name.</summary>
    private GraphNode? Find(string text) =>
        _byAlias.TryGetValue(text, out var node) ? node : Model.Nodes.FirstOrDefault(n => n.Name == text);

    private GraphNode Get(string alias, string display, GraphNodeKind kind)
    {
        if (!_byAlias.TryGetValue(alias, out var node))
        {
            node = new GraphNode { Kind = kind, Name = display };
            Model.Nodes.Add(node);
            _byAlias[alias] = node;
            if (_containers.LastOrDefault(c => c is not null) is { } container)
            {
                _parentOf[node.Id] = container;
            }
        }
        else
        {
            node.Kind = kind;
            node.Name = display;
        }

        _last = node.Id;
        return node;
    }

    [GeneratedRegex(@"^(?<keyword>actor|person|usecase|component|interface|node|artifact|file|rectangle|package|frame|folder|cloud|database|storage|queue|stack|collections|agent|card|hexagon|boundary|control|entity)\s+(?<rest>.+)$", RegexOptions.IgnoreCase)]
    private static partial Regex KeywordLine();

    [GeneratedRegex(@"^(?<end>:[^:]+:|\([^)*][^)]*\)|\[[^\]*][^\]]*\]|\(\)\s*(?:""[^""]+""|\w+))(?:\s+as\s+(?<alias>\w+))?(?:\s*<<[^>]*>>)?(?:\s+#\S+)?\s*\{?\s*$")]
    private static partial Regex ShorthandLine();

    [GeneratedRegex(@"<<\s*(?<text>[^>]*)>>")]
    private static partial Regex Stereotype();

    [GeneratedRegex(@"\s+#\S+")]
    private static partial Regex Color();

    [GeneratedRegex(@"^(?<display>""[^""]+""|\[[^\]]+\]|\([^)]+\)|:[^:]+:)\s+as\s+(?<alias>[\w.]+)$")]
    private static partial Regex DisplayAsAlias();

    [GeneratedRegex(@"^(?<alias>[\w.]+)\s+as\s+(?<display>""[^""]+""|\[[^\]]+\]|\([^)]+\)|:[^:]+:)$")]
    private static partial Regex AliasAsDisplay();

    [GeneratedRegex(@"(?<=.)([A-Z])")]
    private static partial Regex KindWords();

    [GeneratedRegex(@"(?:<<|«)\s*(?<keyword>include|extends?|deploy|manifest)\s*(?:>>|»)", RegexOptions.IgnoreCase)]
    private static partial Regex KeywordLabel();
}
