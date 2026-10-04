using UmlWizard.Core.Model;
using UmlWizard.Core.Rendering;

namespace UmlWizard.Ai;

/// <summary>
/// Places what the AI added to a sequence diagram: new lifelines to the right of the others, and the messages in their
/// new order, each where it was when that leaves room above it, else just below the one before; so a message added in
/// the middle pushes the later ones down. A diagram that had no lifelines is arranged as a whole instead.
/// </summary>
public static class SequencePlacement
{
    private const double LifelineGap = 60;
    private const double MessageGap = 40;
    private const double MinimumGap = 30;

    /// <param name="added">The lifelines, messages and notes added.</param>
    /// <param name="order">Every message, in time order.</param>
    public static void Place(DiagramDocument document, IReadOnlyList<string> added, IReadOnlyList<string> order)
    {
        var model = (SequenceDiagramModel)document.Model;
        var addedSet = added.ToHashSet();
        var wasEmpty = model.Lifelines.All(l => addedSet.Contains(l.Id));

        var right = model.Lifelines.Where(l => !addedSet.Contains(l.Id))
            .Select(l => document.Layout.Find(l.Id))
            .Where(l => l is not null)
            .Select(l => l!.X + l.Width)
            .DefaultIfEmpty(40 - LifelineGap)
            .Max();
        foreach (var lifeline in model.Lifelines.Where(l => addedSet.Contains(l.Id)))
        {
            var layout = document.Layout.Find(lifeline.Id) ?? document.Layout.Add(new ElementLayout { ElementId = lifeline.Id, Width = 120 });
            layout.X = right + LifelineGap;
            layout.Y = SequenceDiagramRenderer.DefaultTop;
            layout.Height = lifeline.Kind == LifelineKind.Actor ? SequenceDiagramRenderer.ActorHeadHeight : SequenceDiagramRenderer.ObjectHeadHeight;
            right = layout.X + layout.Width;
        }

        double? previous = null;
        var previousLoop = false;
        foreach (var id in order)
        {
            var message = model.Messages.Find(m => m.Id == id);
            if (message is null)
            {
                continue;
            }

            var layout = document.Layout.Find(id) ?? document.Layout.Add(new ElementLayout { ElementId = id });
            var lowest = SequenceDiagramRenderer.MinimumMessageY(document.Layout, model.FindLifeline(message.SourceId), model.FindLifeline(message.TargetId));
            var after = previous is { } p ? p + (previousLoop ? SequenceDiagramRenderer.SelfLoopHeight : 0) : lowest;
            var y = !addedSet.Contains(id) && layout.Y >= after + (previous is null ? 0 : MinimumGap)
                ? layout.Y
                : after + (previous is null ? 0 : MessageGap);
            layout.Y = Math.Round(Math.Max(y, lowest));
            previous = layout.Y;
            previousLoop = message.SourceId == message.TargetId;
        }

        PlaceNotes(document, added.Where(id => model.FindNote(id) is not null).ToList());
        if (wasEmpty && model.Lifelines.Count > 0)
        {
            DiagramArranger.Arrange(document);
        }
    }

    /// <summary>New notes go to the right of the diagram, level with what they comment on.</summary>
    private static void PlaceNotes(DiagramDocument document, IReadOnlyList<string> notes)
    {
        if (notes.Count == 0)
        {
            return;
        }

        using var renderer = DiagramRenderer.Create(DiagramType.Sequence);
        renderer.UpdateLayout(document);
        var noteSet = notes.ToHashSet();
        var occupied = document.Model.AllElements()
            .Where(e => e is Lifeline or TextElement && !noteSet.Contains(e.Id))
            .Select(e => renderer.GetElementBounds(document, e.Id))
            .Where(b => b is not null)
            .Select(b => b!.Value)
            .ToList();
        foreach (var id in notes)
        {
            var layout = document.Layout.Find(id)!;
            var target = document.Model.NoteAttachments.Where(a => a.NoteId == id).Select(a => renderer.GetElementBounds(document, a.TargetId)).FirstOrDefault(b => b is not null);
            var spot = FreeSpace.Find(target, occupied, (float)layout.Width, (float)layout.Height);
            (layout.X, layout.Y) = (Math.Round(spot.Left), Math.Round(spot.Top));
            occupied.Add(spot);
        }
    }
}
