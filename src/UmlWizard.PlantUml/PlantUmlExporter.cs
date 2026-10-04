using System.Text;
using UmlWizard.Core.Model;
using UmlWizard.Core.Notation;

namespace UmlWizard.PlantUml;

/// <summary>The PlantUML text of a diagram, and what could not be written in it.</summary>
public sealed record PlantUmlExportResult(string Text, IReadOnlyList<string> Warnings);

/// <summary>
/// Writes a diagram of any type as PlantUML (between <c>@startuml</c> and <c>@enduml</c>), with the diagram's name as
/// its title. Nodes nested in containers (system boundaries, deployment nodes) are nested in braces; notes are written
/// with their attachments. What PlantUML has no place for (text boxes, colours, tagged values) is reported.
/// </summary>
public static class PlantUmlExporter
{
    public static PlantUmlExportResult Export(DiagramDocument document)
    {
        var writer = new Writer(document);
        return new PlantUmlExportResult(writer.Write(), writer.Warnings);
    }

    private sealed class Writer(DiagramDocument document)
    {
        private readonly StringBuilder _text = new();
        private readonly Aliases _aliases = new();
        private int _indent;

        public List<string> Warnings { get; } = [];

        public string Write()
        {
            Line("@startuml");
            if (!string.IsNullOrWhiteSpace(document.Name))
            {
                Line("title " + PlantUmlText.OneLine(document.Name.Trim()));
            }

            switch (document.Model)
            {
                case ClassDiagramModel model:
                    WriteClassDiagram(model);
                    break;
                case SequenceDiagramModel model:
                    WriteSequenceDiagram(model);
                    break;
                case GraphDiagramModel model when document.DiagramType == DiagramType.StateMachine:
                    WriteStateDiagram(model);
                    break;
                case GraphDiagramModel model when document.DiagramType == DiagramType.Activity:
                    new ActivityWriter(model, document, Line, Warnings).Write();
                    break;
                case GraphDiagramModel model:
                    WriteDescriptionDiagram(model);
                    break;
            }

            ReportLoss(document.Model);
            Line("@enduml");
            return _text.ToString();
        }

        #region Class diagrams

        private void WriteClassDiagram(ClassDiagramModel model)
        {
            var taggedValues = 0;
            foreach (var cls in model.Classes)
            {
                var generic = GenericName(cls.Name);
                var alias = _aliases.Get(cls.Id, generic?.Name ?? cls.Name, "Class");
                var keyword = cls.Kind switch
                {
                    ClassifierKind.AbstractClass => "abstract class",
                    ClassifierKind.Interface => "interface",
                    ClassifierKind.Enumeration => "enum",
                    _ => "class",
                };

                // A name PlantUML reads as is (also with type parameters, Repository<T>), or a display name.
                var name = generic is not null && generic.Value.Name == alias ? cls.Name
                    : cls.Name == alias ? alias
                    : $"{PlantUmlText.Quote(cls.Name)} as {alias}";
                var stereotype = string.IsNullOrWhiteSpace(cls.Stereotype) ? string.Empty : $" <<{cls.Stereotype.Trim()}>>";
                var members = cls.Attributes.Select(a => Attribute(cls, a)).Concat(cls.Operations.Select(Operation)).ToList();
                taggedValues += cls.TaggedValues.Count + cls.Attributes.Sum(a => a.TaggedValues.Count) + cls.Operations.Sum(o => o.TaggedValues.Count);
                if (members.Count == 0)
                {
                    Line($"{keyword} {name}{stereotype}");
                    continue;
                }

                Line($"{keyword} {name}{stereotype} {{");
                _indent++;
                foreach (var member in members)
                {
                    Line(member);
                }

                _indent--;
                Line("}");
            }

            if (model.Classes.Count > 0 && model.Relationships.Count > 0)
            {
                Line();
            }

            foreach (var relationship in model.Relationships)
            {
                if (!_aliases.Contains(relationship.SourceId) || !_aliases.Contains(relationship.TargetId))
                {
                    continue;
                }

                var (source, target) = (_aliases[relationship.SourceId], _aliases[relationship.TargetId]);
                var arrow = relationship.Kind switch
                {
                    RelationshipKind.Inheritance => "--|>",
                    RelationshipKind.Realization => "..|>",
                    RelationshipKind.DirectedAssociation => "-->",
                    RelationshipKind.Dependency => "..>",
                    RelationshipKind.Aggregation => "o--",
                    RelationshipKind.Composition => "*--",
                    _ => "--",
                };
                var sourceMultiplicity = string.IsNullOrWhiteSpace(relationship.SourceMultiplicity) ? string.Empty : $" {PlantUmlText.Quote(relationship.SourceMultiplicity.Trim())}";
                var targetMultiplicity = string.IsNullOrWhiteSpace(relationship.TargetMultiplicity) ? string.Empty : $"{PlantUmlText.Quote(relationship.TargetMultiplicity.Trim())} ";
                Line($"{source}{sourceMultiplicity} {arrow} {targetMultiplicity}{target}{LabelSuffix(relationship.Label)}");
            }

            taggedValues += document.TaggedValues.Count;
            Count(taggedValues, "tagged value was not exported: PlantUML has no place for it", "tagged values were not exported: PlantUML has no place for them");
            WriteNotes(model, id => _aliases.Contains(id) ? _aliases[id] : null);
        }

        private static string Attribute(UmlClass owner, UmlAttribute attribute)
        {
            // Enumeration literals are written as they are, with their value.
            if (owner.Kind == ClassifierKind.Enumeration)
            {
                return attribute.Name + (string.IsNullOrWhiteSpace(attribute.DefaultValue) ? string.Empty : " = " + attribute.DefaultValue.Trim());
            }

            return (attribute.IsStatic ? "{static} " : string.Empty) + OneLine(UmlNotation.Format(attribute)).Replace(": ", " : ");
        }

        private static string Operation(UmlOperation operation)
        {
            var text = (operation.IsAbstract ? "{abstract} " : string.Empty) + (operation.IsStatic ? "{static} " : string.Empty) +
                       OneLine(UmlNotation.Format(operation)).Replace(": ", " : ");

            // A web API endpoint keeps its method and path, as a stereotype after the operation.
            return string.IsNullOrWhiteSpace(operation.HttpMethod)
                ? text
                : $"{text} <<{operation.HttpMethod.Trim().ToUpperInvariant()} {operation.Path?.Trim()}".TrimEnd() + ">>";
        }

        /// <summary>The name and type parameters of a generic class name, <c>Repository&lt;T&gt;</c>; null for others.</summary>
        private static (string Name, string Parameters)? GenericName(string name)
        {
            var open = name.IndexOf('<');
            return open > 0 && name.EndsWith('>') && PlantUmlText.IsIdentifier(name[..open]) ? (name[..open], name[(open + 1)..^1]) : null;
        }

        #endregion

        #region Sequence diagrams

        private void WriteSequenceDiagram(SequenceDiagramModel model)
        {
            var layout = document.Layout;
            foreach (var lifeline in model.Lifelines.OrderBy(l => layout.Find(l.Id)?.X ?? 0))
            {
                var alias = _aliases.Get(lifeline.Id, string.IsNullOrWhiteSpace(lifeline.Name) ? lifeline.Type : lifeline.Name, "Participant");
                var label = string.IsNullOrWhiteSpace(lifeline.Type) ? lifeline.Name : $"{lifeline.Name} : {lifeline.Type}".Trim();
                var keyword = lifeline.Kind == LifelineKind.Actor ? "actor" : "participant";
                Line(label == alias ? $"{keyword} {alias}" : $"{keyword} {PlantUmlText.Quote(label)} as {alias}");
            }

            // Messages and the notes attached to lifelines or messages, in the order of their height.
            var attached = model.NoteAttachments.ToLookup(a => a.NoteId, a => a.TargetId);
            var notes = model.Notes.Select(n => (Note: n, Y: layout.Find(n.Id)?.Y ?? double.MaxValue)).ToList();
            var steps = model.Messages.Select(m => (Y: layout.Find(m.Id)?.Y ?? 0, Message: (SequenceMessage?)m, Note: (NoteElement?)null))
                .Concat(notes.Where(n => !attached[n.Note.Id].Any(model.Messages.Select(m => m.Id).Contains)).Select(n => (n.Y, Message: (SequenceMessage?)null, Note: (NoteElement?)n.Note)))
                .OrderBy(s => s.Y)
                .ToList();
            var created = new HashSet<string>();
            if (steps.Count > 0)
            {
                Line();
            }

            foreach (var (_, message, note) in steps)
            {
                if (message is not null)
                {
                    if (!_aliases.Contains(message.SourceId) || !_aliases.Contains(message.TargetId))
                    {
                        continue;
                    }

                    if (message.Kind == MessageKind.Create && created.Add(message.TargetId))
                    {
                        Line("create " + _aliases[message.TargetId]);
                    }

                    var arrow = message.Kind switch
                    {
                        MessageKind.Asynchronous => "->>",
                        MessageKind.Reply => "-->",
                        _ => "->",
                    };
                    Line($"{_aliases[message.SourceId]} {arrow} {_aliases[message.TargetId]}{LabelSuffix(message.Text)}");

                    // Notes attached to the message follow it.
                    foreach (var messageNote in model.Notes.Where(n => attached[n.Id].Contains(message.Id)))
                    {
                        NoteBlock("note right", messageNote.Text);
                    }
                }
                else if (note is not null)
                {
                    var over = attached[note.Id].Where(_aliases.Contains).Select(id => _aliases[id]).Distinct().ToList();
                    NoteBlock(over.Count > 0 ? "note over " + string.Join(", ", over) : "note across", note.Text);
                }
            }
        }

        #endregion

        #region State machine diagrams

        private void WriteStateDiagram(GraphDiagramModel model)
        {
            foreach (var node in model.Nodes)
            {
                switch (node.Kind)
                {
                    case GraphNodeKind.State:
                        var alias = _aliases.Get(node.Id, node.Name, "State");
                        Line(node.Name == alias ? $"state {alias}" : $"state {PlantUmlText.Quote(node.Name ?? string.Empty)} as {alias}");
                        break;
                    case GraphNodeKind.Choice:
                        Line($"state {_aliases.Get(node.Id, "choice", "choice")} <<choice>>");
                        break;
                }
            }

            Count(model.Nodes.Count(n => n.Kind == GraphNodeKind.InitialState) - 1,
                "more initial state is written as [*], the same start as the first one", "more initial states are written as [*], the same start as the first one");
            Count(model.Nodes.Count(n => n.Kind == GraphNodeKind.FinalState) - 1,
                "more final state is written as [*], the same end as the first one", "more final states are written as [*], the same end as the first one");

            if (model.Edges.Count > 0)
            {
                Line();
            }

            foreach (var edge in model.Edges)
            {
                if (model.FindNode(edge.SourceId) is not { } source || model.FindNode(edge.TargetId) is not { } target)
                {
                    continue;
                }

                Line($"{StateEnd(source)} --> {StateEnd(target)}{LabelSuffix(edge.Label)}");
            }

            // State diagrams attach a note to a state (note right of), not with a dotted line.
            var lost = 0;
            foreach (var note in model.Notes)
            {
                Line();
                var targets = model.NoteAttachments.Where(a => a.NoteId == note.Id).ToList();
                var states = targets.Select(a => model.FindNode(a.TargetId))
                    .Where(n => n is { Kind: GraphNodeKind.State or GraphNodeKind.Choice } && _aliases.Contains(n.Id))
                    .Select(n => _aliases[n!.Id])
                    .ToList();
                lost += targets.Count - states.Count;
                if (states.Count == 0)
                {
                    NoteBlock("note as " + _aliases.Get(note.Id, "N", "N"), note.Text, oneLine: false);
                }

                foreach (var state in states)
                {
                    NoteBlock("note right of " + state, note.Text);
                }
            }

            Count(lost, "note attachment to a transition or pseudostate was not exported: PlantUML attaches notes to states",
                "note attachments to transitions or pseudostates were not exported: PlantUML attaches notes to states");
        }

        private string StateEnd(GraphNode node) =>
            node.Kind is GraphNodeKind.InitialState or GraphNodeKind.FinalState ? "[*]" : _aliases.Get(node.Id, node.Name, "State");

        #endregion

        #region Use case, component and deployment diagrams

        /// <summary>
        /// The diagrams PlantUML calls description diagrams: nodes (nested in their containers, in braces), then edges.
        /// </summary>
        private void WriteDescriptionDiagram(GraphDiagramModel model)
        {
            if (document.DiagramType == DiagramType.UseCase)
            {
                Line("left to right direction");
            }

            var parentOf = Containment(model);
            var children = model.Nodes.ToLookup(n => parentOf.GetValueOrDefault(n.Id, string.Empty));
            foreach (var node in children[string.Empty])
            {
                WriteNode(node);
            }

            void WriteNode(GraphNode node)
            {
                var alias = _aliases.Get(node.Id, node.Name, node.Kind.ToString());
                var name = node.Name ?? string.Empty;
                var (keyword, stereotype) = node.Kind switch
                {
                    GraphNodeKind.Actor => ("actor", null),
                    GraphNodeKind.UseCase => ("usecase", null),
                    GraphNodeKind.SystemBoundary => ("rectangle", null),
                    GraphNodeKind.Component => ("component", null),
                    GraphNodeKind.Interface => ("interface", null),
                    GraphNodeKind.Device => ("node", "<<device>>"),
                    GraphNodeKind.ExecutionEnvironment => ("node", "<<executionEnvironment>>"),
                    GraphNodeKind.Artifact => ("artifact", null),
                    _ => ("node", (string?)null),
                };
                var header = $"{keyword} {(name == alias ? alias : $"{PlantUmlText.Quote(name)} as {alias}")}{(stereotype is null ? string.Empty : " " + stereotype)}";
                var inside = children[node.Id].ToList();
                if (inside.Count == 0 && !GraphRules.IsContainer(node.Kind))
                {
                    Line(header);
                    return;
                }

                Line(header + " {");
                _indent++;
                foreach (var child in inside)
                {
                    WriteNode(child);
                }

                _indent--;
                Line("}");
            }

            if (model.Edges.Count > 0)
            {
                Line();
            }

            foreach (var edge in model.Edges)
            {
                if (!_aliases.Contains(edge.SourceId) || !_aliases.Contains(edge.TargetId))
                {
                    continue;
                }

                var (arrow, keyword) = edge.Kind switch
                {
                    GraphEdgeKind.Include => ("..>", "<<include>>"),
                    GraphEdgeKind.Extend => ("..>", "<<extend>>"),
                    GraphEdgeKind.Generalization => ("--|>", null),
                    GraphEdgeKind.Dependency => ("..>", null),
                    GraphEdgeKind.Realization => ("..|>", null),
                    GraphEdgeKind.Deploy => ("..>", "<<deploy>>"),
                    GraphEdgeKind.Manifest => ("..>", "<<manifest>>"),
                    _ => ("--", (string?)null),
                };
                var label = string.Join(' ', new[] { keyword, edge.Label?.Trim() }.Where(t => !string.IsNullOrEmpty(t)));
                Line($"{_aliases[edge.SourceId]} {arrow} {_aliases[edge.TargetId]}{LabelSuffix(label)}");
            }

            WriteNotes(model, id => _aliases.Contains(id) ? _aliases[id] : null);
        }

        /// <summary>
        /// The container each node is drawn in: the smallest container whose bounds hold the node's, as the editor
        /// sees containment.
        /// </summary>
        private Dictionary<string, string> Containment(GraphDiagramModel model)
        {
            var bounds = model.Nodes.ToDictionary(n => n.Id, n => document.Layout.Find(n.Id) is { } l ? (l.X, l.Y, l.X + l.Width, l.Y + l.Height) : (0.0, 0.0, 0.0, 0.0));
            var containers = model.Nodes.Where(n => GraphRules.IsContainer(n.Kind)).ToList();
            var parentOf = new Dictionary<string, string>();
            foreach (var node in model.Nodes)
            {
                var (left, top, right, bottom) = bounds[node.Id];
                var parent = containers
                    .Where(c => c != node)
                    .Select(c => (Container: c, Bounds: bounds[c.Id]))
                    .Where(c => c.Bounds.Item1 <= left && c.Bounds.Item2 <= top && c.Bounds.Item3 >= right && c.Bounds.Item4 >= bottom &&
                                (c.Bounds.Item3 - c.Bounds.Item1) * (c.Bounds.Item4 - c.Bounds.Item2) > (right - left) * (bottom - top))
                    .OrderBy(c => (c.Bounds.Item3 - c.Bounds.Item1) * (c.Bounds.Item4 - c.Bounds.Item2))
                    .Select(c => c.Container)
                    .FirstOrDefault();
                if (parent is not null)
                {
                    parentOf[node.Id] = parent.Id;
                }
            }

            return parentOf;
        }

        #endregion

        #region Notes and what is lost

        /// <summary>
        /// The notes of the diagram: each one named, and linked with dotted lines to the elements it is attached to that
        /// have an alias. Attachments to lines (relationships, edges) cannot be written.
        /// </summary>
        private void WriteNotes(DiagramModel model, Func<string, string?> aliasOf)
        {
            if (model is SequenceDiagramModel)
            {
                return;
            }

            var lost = 0;
            foreach (var note in model.Notes)
            {
                Line();
                var alias = _aliases.Get(note.Id, "N", "N");
                var lines = note.Text.Replace("\r\n", "\n").Split('\n');
                if (lines.Length == 1)
                {
                    Line($"note {PlantUmlText.Quote(note.Text)} as {alias}");
                }
                else
                {
                    Line("note as " + alias);
                    foreach (var line in lines)
                    {
                        Line(line);
                    }

                    Line("end note");
                }

                foreach (var attachment in model.NoteAttachments.Where(a => a.NoteId == note.Id))
                {
                    if (aliasOf(attachment.TargetId) is { } target)
                    {
                        Line($"{alias} .. {target}");
                    }
                    else
                    {
                        lost++;
                    }
                }
            }

            Count(lost, "note attachment to a line was not exported: PlantUML attaches notes to elements",
                "note attachments to lines were not exported: PlantUML attaches notes to elements");
        }

        /// <summary>A note: on the header line after a colon when it is one line and <paramref name="oneLine"/>, else as a block.</summary>
        private void NoteBlock(string header, string text, bool oneLine = true)
        {
            var lines = text.Replace("\r\n", "\n").Split('\n');
            if (lines.Length == 1 && oneLine)
            {
                Line($"{header} : {text}");
                return;
            }

            Line(header);
            foreach (var line in lines)
            {
                Line(line);
            }

            Line("end note");
        }

        /// <summary>Reports text boxes, colours and fonts, which PlantUML has no place for in the diagram.</summary>
        private void ReportLoss(DiagramModel model)
        {
            Count(model.TextBoxes.Count, "text box was not exported: PlantUML has no free text", "text boxes were not exported: PlantUML has no free text");
            if (document.Layout.Font is not null || document.Layout.Elements.Any(e => e.Font is not null || e.Style is not null || e.Line is not null))
            {
                Warnings.Add("Fonts, colours and line styles were not exported.");
            }
        }

        #endregion

        private static string LabelSuffix(string? label) =>
            string.IsNullOrWhiteSpace(label) ? string.Empty : " : " + PlantUmlText.OneLine(label.Trim());

        private static string OneLine(string text) => PlantUmlText.OneLine(text);

        private void Count(int count, string one, string many)
        {
            if (count > 0)
            {
                Warnings.Add($"{count} {(count == 1 ? one : many)}.");
            }
        }

        private void Line(string text = "")
        {
            if (text.Length == 0)
            {
                // One blank line at most, and none after an opening line.
                if (_text.Length > 0 && !_text.ToString().EndsWith("\n\n", StringComparison.Ordinal) && !_text.ToString().TrimEnd().EndsWith('{'))
                {
                    _text.Append('\n');
                }

                return;
            }

            _text.Append(new string(' ', _indent * 2)).Append(text).Append('\n');
        }
    }
}
