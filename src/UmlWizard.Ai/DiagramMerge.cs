using UmlWizard.Core.Model;
using UmlWizard.Core.Notation;
using UmlWizard.Core.Serialization;

namespace UmlWizard.Ai;

/// <summary>
/// Takes a revised version of a diagram (the model's PlantUML, imported) into the diagram, matching elements by name:
/// what is in both keeps its identity, place and look, and what PlantUML does not carry (tagged values, styles, text
/// boxes); what is new is placed next to what it is connected to; what the revision leaves out is removed. For models
/// without tool calling (<see cref="AiChangeMode.WholeDiagram"/>).
/// </summary>
public static class DiagramMerge
{
    /// <summary>The diagram with the revision taken in; <paramref name="current"/> is not changed.</summary>
    /// <exception cref="AiException">The revision is another kind of diagram.</exception>
    public static DiagramDocument Merge(DiagramDocument current, DiagramDocument revision)
    {
        if (revision.DiagramType != current.DiagramType)
        {
            throw new AiException($"The AI answered with a {revision.DiagramType} diagram instead of a {current.DiagramType} diagram.");
        }

        var result = DiagramSerializer.Clone(current);
        var merge = new Merger(result, revision);
        switch (result.Model)
        {
            case ClassDiagramModel model:
                merge.Classes(model, (ClassDiagramModel)revision.Model);
                merge.Notes();
                merge.Layouts();
                ClassPlacement.Place(result, merge.Added);
                break;
            case GraphDiagramModel model:
                var inside = merge.Graph(model, (GraphDiagramModel)revision.Model);
                merge.Notes();
                merge.Layouts();
                GraphPlacement.Place(result, merge.Added, inside);
                break;
            case SequenceDiagramModel model:
                var order = merge.Sequence(model, (SequenceDiagramModel)revision.Model);
                merge.Notes();
                merge.Layouts();
                SequencePlacement.Place(result, merge.Added, order);
                break;
        }

        return result;
    }

    private sealed class Merger(DiagramDocument result, DiagramDocument revision)
    {
        /// <summary>The id in the result of each element of the revision.</summary>
        private readonly Dictionary<string, string> _ids = [];

        public List<string> Added { get; } = [];

        private string Id(string revisionId) => _ids.GetValueOrDefault(revisionId, revisionId);

        /// <summary>Pairs each element of the revision with the first unused element of the result with the same key.</summary>
        private Dictionary<T, T> Match<T>(IEnumerable<T> current, IEnumerable<T> revised, params Func<T, string?>[] keys)
            where T : ModelElement
        {
            var pairs = new Dictionary<T, T>();
            var used = new HashSet<T>();
            var unmatched = revised.ToList();
            foreach (var key in keys)
            {
                foreach (var element in unmatched.ToList())
                {
                    var k = key(element);
                    if (k is null)
                    {
                        continue;
                    }

                    var match = current.FirstOrDefault(c => !used.Contains(c) && key(c) == k);
                    if (match is not null)
                    {
                        pairs[element] = match;
                        used.Add(match);
                        unmatched.Remove(element);
                        _ids[element.Id] = match.Id;
                    }
                }
            }

            return pairs;
        }

        public void Classes(ClassDiagramModel model, ClassDiagramModel revised)
        {
            var pairs = Match(model.Classes, revised.Classes, c => c.Name, c => c.Name.ToLowerInvariant());
            var classes = new List<UmlClass>();
            foreach (var cls in revised.Classes)
            {
                if (!pairs.TryGetValue(cls, out var kept))
                {
                    classes.Add(cls);
                    Added.Add(cls.Id);
                    continue;
                }

                // What is unchanged keeps what PlantUML does not carry, such as tagged values.
                (kept.Name, kept.Kind, kept.Stereotype) = (cls.Name, cls.Kind, cls.Stereotype);
                kept.Attributes = [.. cls.Attributes.Select(a => kept.Attributes.FirstOrDefault(k => UmlNotation.Format(k) == UmlNotation.Format(a)) ?? a)];
                kept.Operations = [.. cls.Operations.Select(o => kept.Operations.FirstOrDefault(k => SameOperation(k, o)) ?? o)];
                classes.Add(kept);
            }

            var relationships = new List<UmlRelationship>();
            foreach (var relationship in revised.Relationships)
            {
                var (source, target) = (Id(relationship.SourceId), Id(relationship.TargetId));
                var kept = model.Relationships.FirstOrDefault(r => r.SourceId == source && r.TargetId == target && r.Kind == relationship.Kind && !relationships.Contains(r));
                if (kept is null)
                {
                    (relationship.SourceId, relationship.TargetId) = (source, target);
                    relationships.Add(relationship);
                    continue;
                }

                (kept.Label, kept.SourceMultiplicity, kept.TargetMultiplicity) = (relationship.Label, relationship.SourceMultiplicity, relationship.TargetMultiplicity);
                _ids[relationship.Id] = kept.Id;
                relationships.Add(kept);
            }

            (model.Classes, model.Relationships) = (classes, relationships);
        }

        private static bool SameOperation(UmlOperation a, UmlOperation b) =>
            UmlNotation.Format(a) == UmlNotation.Format(b) && a.HttpMethod == b.HttpMethod && a.Path == b.Path;

        /// <returns>The container each new node is drawn in, in the revision.</returns>
        public Dictionary<string, string?> Graph(GraphDiagramModel model, GraphDiagramModel revised)
        {
            // Named nodes by kind and name; the others (initial, final, decision…) by kind, in order.
            static string? Named(GraphNode n) => GraphRules.HasName(n.Kind) && !string.IsNullOrWhiteSpace(n.Name) ? $"{n.Kind}:{n.Name}" : null;
            static string? NamedLoosely(GraphNode n) => Named(n)?.ToLowerInvariant();
            static string? Nameless(GraphNode n) => Named(n) is null ? n.Kind.ToString() : null;
            var pairs = Match(model.Nodes, revised.Nodes, Named, NamedLoosely, Nameless);
            var nodes = new List<GraphNode>();
            foreach (var node in revised.Nodes)
            {
                if (pairs.TryGetValue(node, out var kept))
                {
                    kept.Name = node.Name;
                    nodes.Add(kept);
                }
                else
                {
                    nodes.Add(node);
                    Added.Add(node.Id);
                }
            }

            var edges = new List<GraphEdge>();
            foreach (var edge in revised.Edges)
            {
                var (source, target) = (Id(edge.SourceId), Id(edge.TargetId));
                var kept = model.Edges.FirstOrDefault(e => e.SourceId == source && e.TargetId == target && e.Kind == edge.Kind && !edges.Contains(e));
                if (kept is null)
                {
                    (edge.SourceId, edge.TargetId) = (source, target);
                    edges.Add(edge);
                    continue;
                }

                kept.Label = edge.Label;
                _ids[edge.Id] = kept.Id;
                edges.Add(kept);
            }

            var containers = Core.Rendering.DiagramArranger.ContainerOf(revision, revised);
            (model.Nodes, model.Edges) = (nodes, edges);
            return Added.Where(id => model.FindNode(id) is not null)
                .ToDictionary(id => id, id => containers.TryGetValue(id, out var c) ? Id(c) : null);
        }

        /// <returns>The messages in their new order.</returns>
        public List<string> Sequence(SequenceDiagramModel model, SequenceDiagramModel revised)
        {
            static string Name(Lifeline l) => string.IsNullOrWhiteSpace(l.Name) ? l.Type ?? string.Empty : l.Name;
            var pairs = Match(model.Lifelines, revised.Lifelines, Name, l => Name(l).ToLowerInvariant());
            var lifelines = new List<Lifeline>();
            foreach (var lifeline in revised.Lifelines)
            {
                if (pairs.TryGetValue(lifeline, out var kept))
                {
                    (kept.Name, kept.Type, kept.Kind) = (lifeline.Name, lifeline.Type, lifeline.Kind);
                    lifelines.Add(kept);
                }
                else
                {
                    lifelines.Add(lifeline);
                    Added.Add(lifeline.Id);
                }
            }

            var messages = new List<SequenceMessage>();
            foreach (var message in revised.Messages.OrderBy(m => revision.Layout.Find(m.Id)?.Y ?? 0))
            {
                var (source, target) = (Id(message.SourceId), Id(message.TargetId));
                var kept = model.Messages.FirstOrDefault(m =>
                    m.SourceId == source && m.TargetId == target && m.Text == message.Text && m.Kind == message.Kind && !messages.Contains(m));
                if (kept is null)
                {
                    (message.SourceId, message.TargetId) = (source, target);
                    messages.Add(message);
                    Added.Add(message.Id);
                    continue;
                }

                _ids[message.Id] = kept.Id;
                messages.Add(kept);
            }

            (model.Lifelines, model.Messages) = (lifelines, messages);
            return [.. messages.Select(m => m.Id)];
        }

        /// <summary>Notes by their text; the attachments of the revision, and those of kept notes to lines PlantUML cannot write.</summary>
        public void Notes()
        {
            var model = result.Model;
            static string Text(NoteElement n) => n.Text.ReplaceLineEndings("\n").Trim();
            var pairs = Match(model.Notes, revision.Model.Notes, Text);
            var notes = new List<NoteElement>();
            var top = model.TextElements().Select(e => e.ZIndex).DefaultIfEmpty(-1).Max();
            foreach (var note in revision.Model.Notes)
            {
                if (pairs.TryGetValue(note, out var kept))
                {
                    notes.Add(kept);
                }
                else
                {
                    // On top of the others, as when added in the editor.
                    note.ZIndex = ++top;
                    notes.Add(note);
                    Added.Add(note.Id);
                }
            }

            var attachments = revision.Model.NoteAttachments
                .Select(a => new NoteAttachment { NoteId = Id(a.NoteId), TargetId = Id(a.TargetId) })
                .ToList();
            var keptIds = notes.Select(n => n.Id).ToHashSet();
            model.Notes = notes;
            attachments.AddRange(model.NoteAttachments.Where(a =>
                keptIds.Contains(a.NoteId) && !attachments.Any(r => r.NoteId == a.NoteId && r.TargetId == a.TargetId)));
            model.NoteAttachments = attachments;
            model.RemoveDanglingAttachments();
        }

        /// <summary>The kept elements keep their layout; the new ones take their size from the revision, and get a place later.</summary>
        public void Layouts()
        {
            var revisionLayouts = revision.Layout.Elements.ToDictionary(l => l.ElementId);
            var elements = new List<ElementLayout>();
            foreach (var element in result.Model.AllElements())
            {
                if (result.Layout.Find(element.Id) is { } kept)
                {
                    elements.Add(kept);
                }
                else if (revisionLayouts.TryGetValue(element.Id, out var revised))
                {
                    var layout = revised.Clone();
                    (layout.X, layout.Y) = (0, 0);
                    elements.Add(layout);
                }
            }

            result.Layout.Elements = elements;
        }
    }
}
