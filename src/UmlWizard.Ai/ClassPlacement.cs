using SkiaSharp;
using UmlWizard.Core.Model;
using UmlWizard.Core.Rendering;

namespace UmlWizard.Ai;

/// <summary>
/// Places the classes and notes the AI added, without moving the others: each next to a class it is related to (a note
/// next to the class it comments on), in free space; otherwise to the right of the diagram. A diagram that had no
/// classes is arranged as a whole instead.
/// </summary>
public static class ClassPlacement
{
    public static void Place(DiagramDocument document, IReadOnlyList<string> added)
    {
        var model = (ClassDiagramModel)document.Model;
        var addedSet = added.ToHashSet();
        if (added.Count == 0)
        {
            return;
        }

        if (model.Classes.All(c => addedSet.Contains(c.Id)))
        {
            DiagramArranger.Arrange(document);
            return;
        }

        using var renderer = DiagramRenderer.Create(DiagramType.Class);
        renderer.UpdateLayout(document);
        SKRect Bounds(string id) => renderer.GetElementBounds(document, id) ?? SKRect.Create(0, 0, 160, 80);

        // Everything already in place, and what is placed next, must not be overlapped.
        var occupied = model.Classes.Select(c => c.Id).Concat(model.TextElements().Select(t => t.Id))
            .Where(id => !addedSet.Contains(id))
            .Select(Bounds)
            .ToList();

        // Classes first, so that notes can go next to classes that were just added.
        var order = added.Where(id => model.FindClass(id) is not null).Concat(added.Where(id => model.FindNote(id) is not null));
        foreach (var id in order)
        {
            var size = Bounds(id);
            var anchor = Anchor(model, id, addedSet) is { } anchorId ? Bounds(anchorId) : (SKRect?)null;
            var spot = FreeSpace.Find(anchor, occupied, size.Width, size.Height);
            var layout = document.Layout.Find(id)!;
            (layout.X, layout.Y) = (Math.Round(spot.Left), Math.Round(spot.Top));
            occupied.Add(spot);

            // Once placed, a new element anchors those added after it.
            addedSet.Remove(id);
            renderer.UpdateLayout(document);
        }
    }

    /// <summary>The placed element a new one goes next to: the class a note comments on, or a class related to a class.</summary>
    private static string? Anchor(ClassDiagramModel model, string id, IReadOnlySet<string> unplaced)
    {
        if (model.FindNote(id) is not null)
        {
            return model.NoteAttachments.Where(a => a.NoteId == id).Select(a => a.TargetId).FirstOrDefault(t => !unplaced.Contains(t));
        }

        return model.Relationships
            .Select(r => r.SourceId == id ? r.TargetId : r.TargetId == id ? r.SourceId : null)
            .FirstOrDefault(other => other is not null && !unplaced.Contains(other));
    }
}
