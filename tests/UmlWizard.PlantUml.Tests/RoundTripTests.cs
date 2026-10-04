using UmlWizard.Core.Model;
using UmlWizard.Core.Notation;
using UmlWizard.Core.Serialization;

namespace UmlWizard.PlantUml.Tests;

/// <summary>
/// Exports every diagram of the samples as PlantUML, imports the text back, and checks that the same diagram comes
/// back: its type and name, its elements with all they say, their relationships, containment and notes.
/// </summary>
public sealed class RoundTripTests
{
    public static TheoryData<string, int> Diagrams()
    {
        var data = new TheoryData<string, int>();
        foreach (var sample in Samples)
        {
            var count = Load(sample).Diagrams.Count;
            for (var i = 0; i < count; i++)
            {
                data.Add(sample, i);
            }
        }

        return data;
    }

    private static readonly string[] Samples = ["OnlineShop/OnlineShop.umlwiz", "GangOfFour/GangOfFour.umlwiz"];

    private static UmlProject Load(string sample) => ProjectSerializer.Load(Path.Combine(AppContext.BaseDirectory, "samples", sample));

    [Theory]
    [MemberData(nameof(Diagrams))]
    public void ExportedDiagram_ImportsBackAsTheSameDiagram(string sample, int index)
    {
        var original = Load(sample).Diagrams[index];
        var exported = PlantUmlExporter.Export(original);

        var result = PlantUmlImporter.Import(exported.Text, "fallback");

        var imported = Assert.Single(result.Documents);
        Assert.True(result.Warnings.Count == 0, string.Join("\n", result.Warnings) + "\n\n" + exported.Text);
        Assert.Equal(original.DiagramType, imported.DiagramType);
        Assert.Equal(original.Name, imported.Name);
        Assert.Equal(Describe(original), Describe(imported));

        // Every element is laid out.
        Assert.All(ElementIds(imported.Model), id => Assert.NotNull(imported.Layout.Find(id)));
    }

    /// <summary>What a diagram says, as lines to compare, without ids or positions.</summary>
    private static List<string> Describe(DiagramDocument document)
    {
        var lines = new List<string>();
        switch (document.Model)
        {
            case ClassDiagramModel model:
                string ClassName(string id) => model.FindClass(id)?.Name ?? "?";
                foreach (var cls in model.Classes.OrderBy(c => c.Name))
                {
                    lines.Add($"{cls.Kind} {cls.Name} <<{cls.Stereotype}>>");
                    lines.AddRange(cls.Attributes.Select(a => $"  {(a.IsStatic ? "static " : "")}{UmlNotation.Format(a)}"));
                    lines.AddRange(cls.Operations.Select(o => $"  {(o.IsAbstract ? "abstract " : "")}{(o.IsStatic ? "static " : "")}{UmlNotation.Format(o)} {o.HttpMethod} {o.Path}"));
                }

                lines.AddRange(model.Relationships
                    .Select(r => $"{r.Kind} {ClassName(r.SourceId)} {r.SourceMultiplicity} -> {r.TargetMultiplicity} {ClassName(r.TargetId)} : {r.Label}")
                    .Order());
                break;

            case SequenceDiagramModel model:
                lines.AddRange(model.Lifelines.OrderBy(l => document.Layout.Find(l.Id)?.X).Select(l => $"{l.Kind} {UmlNotation.FormatLifeline(l)}"));
                string LifelineName(string id) => model.FindLifeline(id) is { } l ? UmlNotation.FormatLifeline(l) : "?";
                lines.AddRange(model.Messages.OrderBy(m => document.Layout.Find(m.Id)?.Y)
                    .Select(m => $"{m.Kind} {LifelineName(m.SourceId)} -> {LifelineName(m.TargetId)} : {m.Text}"));
                break;

            case GraphDiagramModel model:
                string NodeName(string id) => model.FindNode(id) is { } n ? $"{n.Kind}:{n.Name}" : "?";
                var parents = Containment(document, model);
                lines.AddRange(model.Nodes.Select(n => $"{n.Kind} {n.Name} in {(parents.TryGetValue(n.Id, out var p) ? NodeName(p) : "-")}").Order());
                lines.AddRange(model.Edges.Select(e => $"{e.Kind} {NodeName(e.SourceId)} -> {NodeName(e.TargetId)} : {e.Label}").Order());
                break;
        }

        // Notes, with what they are attached to.
        string Target(string id) => document.Model switch
        {
            ClassDiagramModel m => m.FindClass(id)?.Name ?? "relationship",
            SequenceDiagramModel m => m.FindLifeline(id) is { } l ? UmlNotation.FormatLifeline(l) : "message",
            GraphDiagramModel m => m.FindNode(id)?.Name ?? "edge",
            _ => id,
        };
        lines.AddRange(document.Model.Notes
            .Select(n => $"note {n.Text.Replace("\n", "\\n")} on {string.Join(", ", document.Model.NoteAttachments.Where(a => a.NoteId == n.Id).Select(a => Target(a.TargetId)).Order())}")
            .Order());
        return lines;
    }

    /// <summary>The container each node is drawn in, as the editor sees containment.</summary>
    private static Dictionary<string, string> Containment(DiagramDocument document, GraphDiagramModel model)
    {
        var result = new Dictionary<string, string>();
        foreach (var node in model.Nodes)
        {
            var l = document.Layout.Find(node.Id)!;
            var parent = model.Nodes
                .Where(c => c != node && GraphRules.IsContainer(c.Kind))
                .Select(c => (Node: c, Layout: document.Layout.Find(c.Id)!))
                .Where(c => c.Layout.X <= l.X && c.Layout.Y <= l.Y && c.Layout.X + c.Layout.Width >= l.X + l.Width && c.Layout.Y + c.Layout.Height >= l.Y + l.Height)
                .OrderBy(c => c.Layout.Width * c.Layout.Height)
                .Select(c => c.Node)
                .FirstOrDefault();
            if (parent is not null)
            {
                result[node.Id] = parent.Id;
            }
        }

        return result;
    }

    private static IEnumerable<string> ElementIds(DiagramModel model) => model switch
    {
        ClassDiagramModel m => m.Classes.Select(c => c.Id),
        SequenceDiagramModel m => m.Lifelines.Select(l => l.Id).Concat(m.Messages.Select(x => x.Id)),
        GraphDiagramModel m => m.Nodes.Select(n => n.Id),
        _ => [],
    };
}
