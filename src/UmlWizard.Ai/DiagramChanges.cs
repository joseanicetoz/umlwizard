using UmlWizard.Core.Model;
using UmlWizard.Core.Notation;
using UmlWizard.Core.Rendering;

namespace UmlWizard.Ai;

/// <summary>What changed between a diagram and its working copy, in short lines for the preview, e.g. "+ class Payment".</summary>
public static class DiagramChanges
{
    public static IReadOnlyList<string> Describe(DiagramDocument before, DiagramDocument after)
    {
        var lines = new List<string>();
        if (before.Model is ClassDiagramModel b && after.Model is ClassDiagramModel a)
        {
            DescribeClasses(b, a, lines);
        }
        else if (before.Model is GraphDiagramModel gb && after.Model is GraphDiagramModel ga)
        {
            DescribeGraph(before, gb, after, ga, lines);
        }
        else if (before.Model is SequenceDiagramModel sb && after.Model is SequenceDiagramModel sa)
        {
            DescribeSequence(before, sb, after, sa, lines);
        }

        var beforeNotes = before.Model.Notes.ToDictionary(n => n.Id);
        var afterNotes = after.Model.Notes.ToDictionary(n => n.Id);
        lines.AddRange(afterNotes.Values.Where(n => !beforeNotes.ContainsKey(n.Id)).Select(n => $"+ note \"{Short(n.Text)}\""));
        lines.AddRange(beforeNotes.Values.Where(n => !afterNotes.ContainsKey(n.Id)).Select(n => $"− note \"{Short(n.Text)}\""));
        return lines;
    }

    private static void DescribeClasses(ClassDiagramModel before, ClassDiagramModel after, List<string> lines)
    {
        var old = before.Classes.ToDictionary(c => c.Id);
        var now = after.Classes.ToDictionary(c => c.Id);
        foreach (var cls in after.Classes)
        {
            if (!old.TryGetValue(cls.Id, out var was))
            {
                lines.Add($"+ {Kind(cls)} {cls.Name}");
                lines.AddRange(Members(cls).Select(m => $"    + {m}"));
                continue;
            }

            if (was.Name != cls.Name)
            {
                lines.Add($"~ {was.Name} renamed to {cls.Name}");
            }

            if (was.Kind != cls.Kind || was.Stereotype != cls.Stereotype)
            {
                lines.Add($"~ {cls.Name} is now {Kind(cls)}{(cls.Stereotype is { } s ? $" «{s}»" : string.Empty)}");
            }

            var (wasMembers, members) = (Members(was).ToList(), Members(cls).ToList());
            lines.AddRange(members.Except(wasMembers).Select(m => $"+ {cls.Name}: {m}"));
            lines.AddRange(wasMembers.Except(members).Select(m => $"− {cls.Name}: {m}"));
        }

        lines.AddRange(before.Classes.Where(c => !now.ContainsKey(c.Id)).Select(c => $"− {Kind(c)} {c.Name}"));

        string Name(ClassDiagramModel model, string id) => model.FindClass(id)?.Name ?? "?";
        var oldRelations = before.Relationships.Select(r => r.Id).ToHashSet();
        var newRelations = after.Relationships.Select(r => r.Id).ToHashSet();
        lines.AddRange(after.Relationships.Where(r => !oldRelations.Contains(r.Id))
            .Select(r => $"+ {Name(after, r.SourceId)} → {Name(after, r.TargetId)} ({Relationship(r.Kind)})"));
        lines.AddRange(before.Relationships.Where(r => !newRelations.Contains(r.Id))
            .Select(r => $"− {Name(before, r.SourceId)} → {Name(before, r.TargetId)} ({Relationship(r.Kind)})"));
    }

    private static void DescribeGraph(DiagramDocument beforeDocument, GraphDiagramModel before, DiagramDocument afterDocument, GraphDiagramModel after, List<string> lines)
    {
        var (oldNames, newNames) = (Names(before), Names(after));
        var old = before.Nodes.ToDictionary(n => n.Id);
        var now = after.Nodes.ToDictionary(n => n.Id);
        var (oldInside, newInside) = (DiagramArranger.ContainerOf(beforeDocument, before), DiagramArranger.ContainerOf(afterDocument, after));
        foreach (var node in after.Nodes)
        {
            var container = newInside.TryGetValue(node.Id, out var c) ? newNames[c] : null;
            if (!old.TryGetValue(node.Id, out var was))
            {
                lines.Add($"+ {Word(node.Kind)} {newNames[node.Id]}{(container is null ? string.Empty : $" in {container}")}");
                continue;
            }

            if (was.Name != node.Name)
            {
                lines.Add($"~ {was.Name} renamed to {node.Name}");
            }

            var wasIn = oldInside.TryGetValue(node.Id, out var o) && now.ContainsKey(o) ? newNames[o] : null;
            if (wasIn != container)
            {
                lines.Add(container is null ? $"~ {newNames[node.Id]} taken out of {wasIn}" : $"~ {newNames[node.Id]} moved into {container}");
            }
        }

        lines.AddRange(before.Nodes.Where(n => !now.ContainsKey(n.Id)).Select(n => $"− {Word(n.Kind)} {oldNames[n.Id]}"));

        string Edge(GraphEdge e, Dictionary<string, string> names) =>
            $"{names.GetValueOrDefault(e.SourceId, "?")} → {names.GetValueOrDefault(e.TargetId, "?")} ({Word(e.Kind)}){(string.IsNullOrWhiteSpace(e.Label) ? string.Empty : $" : {e.Label}")}";
        var oldEdges = before.Edges.ToDictionary(e => e.Id);
        var newEdges = after.Edges.Select(e => e.Id).ToHashSet();
        foreach (var edge in after.Edges)
        {
            if (!oldEdges.TryGetValue(edge.Id, out var was))
            {
                lines.Add($"+ {Edge(edge, newNames)}");
            }
            else if (was.Label != edge.Label)
            {
                lines.Add($"~ {Edge(edge, newNames)}");
            }
        }

        lines.AddRange(before.Edges.Where(e => !newEdges.Contains(e.Id)).Select(e => $"− {Edge(e, oldNames)}"));
    }

    private static Dictionary<string, string> Names(GraphDiagramModel model)
    {
        var references = new ElementReferences(model.Nodes.Select(n => (n.Id, GraphRules.HasName(n.Kind) ? n.Name : null, DiagramTools.SnakeCase(n.Kind.ToString()))));
        return model.Nodes.ToDictionary(n => n.Id, n => references[n.Id]);
    }

    private static void DescribeSequence(DiagramDocument beforeDocument, SequenceDiagramModel before, DiagramDocument afterDocument, SequenceDiagramModel after, List<string> lines)
    {
        var old = before.Lifelines.ToDictionary(l => l.Id);
        var now = after.Lifelines.ToDictionary(l => l.Id);
        foreach (var lifeline in after.Lifelines)
        {
            if (!old.TryGetValue(lifeline.Id, out var was))
            {
                lines.Add($"+ lifeline {Lifeline(lifeline)}");
            }
            else if (was.Name != lifeline.Name || was.Type != lifeline.Type)
            {
                lines.Add($"~ {Lifeline(was)} is now {Lifeline(lifeline)}");
            }
        }

        lines.AddRange(before.Lifelines.Where(l => !now.ContainsKey(l.Id)).Select(l => $"− lifeline {Lifeline(l)}"));

        List<SequenceMessage> InOrder(DiagramDocument document, SequenceDiagramModel model) =>
            [.. model.Messages.OrderBy(m => document.Layout.Find(m.Id)?.Y ?? 0)];
        string Message(SequenceDiagramModel model, SequenceMessage m) =>
            $"{model.FindLifeline(m.SourceId)?.Name ?? "?"} → {model.FindLifeline(m.TargetId)?.Name ?? "?"}: {m.Text}";
        var (oldOrder, newOrder) = (InOrder(beforeDocument, before), InOrder(afterDocument, after));
        var oldMessages = oldOrder.ToDictionary(m => m.Id);
        var newIds = newOrder.Select(m => m.Id).ToHashSet();
        for (var i = 0; i < newOrder.Count; i++)
        {
            var message = newOrder[i];
            if (!oldMessages.TryGetValue(message.Id, out var was))
            {
                lines.Add($"+ {i + 1}. {Message(after, message)}");
            }
            else if (was.Text != message.Text || was.Kind != message.Kind)
            {
                lines.Add($"~ {i + 1}. {Message(after, message)}");
            }
        }

        lines.AddRange(oldOrder.Where(m => !newIds.Contains(m.Id)).Select(m => $"− {Message(before, m)}"));
    }

    private static string Lifeline(Lifeline lifeline) =>
        string.IsNullOrWhiteSpace(lifeline.Type) ? lifeline.Name : $"{lifeline.Name} : {lifeline.Type}";

    private static string Word<T>(T kind) => DiagramTools.SnakeCase(kind!.ToString()!).Replace('_', ' ');

    private static IEnumerable<string> Members(UmlClass cls) =>
        cls.Attributes.Select(a => UmlNotation.Format(a)).Concat(cls.Operations.Select(UmlNotation.Format));

    private static string Kind(UmlClass cls) => cls.Kind switch
    {
        ClassifierKind.AbstractClass => "abstract class",
        ClassifierKind.Interface => "interface",
        ClassifierKind.Enumeration => "enumeration",
        _ => "class",
    };

    private static string Relationship(RelationshipKind kind) => kind switch
    {
        RelationshipKind.DirectedAssociation => "directed association",
        RelationshipKind.Inheritance => "generalization",
        _ => kind.ToString().ToLowerInvariant(),
    };

    private static string Short(string text)
    {
        var line = text.ReplaceLineEndings(" ").Trim();
        return line.Length <= 40 ? line : line[..40] + "…";
    }
}
