using SkiaSharp;
using UmlWizard.Core.Model;
using UmlWizard.Core.Rendering;

namespace UmlWizard.Ai;

/// <summary>
/// Places the nodes and notes the AI added to a state machine, activity, use case, component or deployment diagram,
/// without moving the others. A node that goes in a container (a use case in its system, an artifact on its node) is
/// placed in free space inside it, and the container grows when it is full; any other node goes next to a node it is
/// connected to, in free space, else to the right of the diagram. A diagram that had no nodes is arranged as a whole.
/// </summary>
public static class GraphPlacement
{
    private const float Padding = 14;
    private const float Header = 34;
    private const float Step = 10;
    private const float InnerClearance = 10;

    /// <param name="added">The nodes and notes added, or moved.</param>
    /// <param name="inside">The container each added or moved node goes in; <c>null</c> for none.</param>
    public static void Place(DiagramDocument document, IReadOnlyList<string> added, IReadOnlyDictionary<string, string?> inside)
    {
        var model = (GraphDiagramModel)document.Model;
        if (added.Count == 0)
        {
            return;
        }

        var addedSet = added.ToHashSet();
        var wasEmpty = model.Nodes.All(n => addedSet.Contains(n.Id));

        // Where the others are drawn tells what holds them; the tools tell it for the new ones.
        var containerOf = DiagramArranger.ContainerOf(document, model)
            .Where(p => !addedSet.Contains(p.Key) && !addedSet.Contains(p.Value))
            .ToDictionary(p => p.Key, p => p.Value);
        foreach (var (id, container) in inside)
        {
            if (container is null)
            {
                containerOf.Remove(id);
            }
            else
            {
                containerOf[id] = container;
            }
        }

        var placed = model.Nodes.Select(n => n.Id).Concat(model.TextElements().Select(t => t.Id))
            .Where(id => !addedSet.Contains(id) && document.Layout.Find(id) is not null)
            .ToDictionary(id => id, id => Rect(document.Layout.Find(id)!));

        // Containers before what they hold, notes last, so that each has something placed to go in or next to.
        int Depth(string id)
        {
            var depth = 0;
            for (var c = id; containerOf.TryGetValue(c, out var parent) && depth < 20; c = parent)
            {
                depth++;
            }

            return depth;
        }

        var order = added.Where(id => model.FindNode(id) is not null).OrderBy(Depth)
            .Concat(added.Where(id => model.FindNote(id) is not null));
        foreach (var id in order)
        {
            var layout = document.Layout.Find(id);
            if (layout is null)
            {
                continue;
            }

            var (width, height) = ((float)layout.Width, (float)layout.Height);
            SKRect spot;
            if (containerOf.TryGetValue(id, out var container) && placed.ContainsKey(container))
            {
                spot = InsideSpot(document, container, width, height, placed, containerOf);
            }
            else
            {
                var anchor = Anchor(model, id, placed);
                spot = FreeSpace.Find(anchor is null ? null : placed[anchor], [.. placed.Values], width, height);
            }

            (layout.X, layout.Y) = (Math.Round(spot.Left), Math.Round(spot.Top));
            placed[id] = Rect(layout);
        }

        if (wasEmpty)
        {
            DiagramArranger.Arrange(document);
        }
    }

    /// <summary>Free room inside a container, scanning row by row; below what it holds, made larger, when it is full.</summary>
    private static SKRect InsideSpot(
        DiagramDocument document, string container, float width, float height, Dictionary<string, SKRect> placed, Dictionary<string, string> containerOf)
    {
        var box = placed[container];
        var contents = placed.Where(p => p.Key != container && box.Contains(p.Value)).Select(p => FreeSpace.Inflate(p.Value, InnerClearance)).ToList();
        for (var y = box.Top + Header; y + height <= box.Bottom - Padding; y += Step)
        {
            for (var x = box.Left + Padding; x + width <= box.Right - Padding; x += Step)
            {
                var rect = SKRect.Create(x, y, width, height);
                if (!contents.Any(c => c.IntersectsWith(rect)))
                {
                    return rect;
                }
            }
        }

        var top = contents.Count == 0 ? box.Top + Header : Math.Max(box.Top + Header, contents.Max(c => c.Bottom));
        var spot = SKRect.Create(box.Left + Padding, top, width, height);
        Grow(document, container, spot, placed, containerOf);
        return spot;
    }

    /// <summary>Makes a container hold <paramref name="rect"/>, and the containers it is in hold it.</summary>
    private static void Grow(DiagramDocument document, string container, SKRect rect, Dictionary<string, SKRect> placed, Dictionary<string, string> containerOf)
    {
        for (var depth = 0; depth < 20 && document.Layout.Find(container) is { } layout; depth++)
        {
            layout.Width = Math.Max(layout.Width, Math.Ceiling(rect.Right + Padding - layout.X));
            layout.Height = Math.Max(layout.Height, Math.Ceiling(rect.Bottom + Padding - layout.Y));
            placed[container] = Rect(layout);
            if (!containerOf.TryGetValue(container, out var parent))
            {
                return;
            }

            (container, rect) = (parent, placed[container]);
        }
    }

    /// <summary>The placed element a new one goes next to: the node a note comments on, or a node it is connected to.</summary>
    private static string? Anchor(GraphDiagramModel model, string id, Dictionary<string, SKRect> placed)
    {
        if (model.FindNote(id) is not null)
        {
            return model.NoteAttachments.Where(a => a.NoteId == id).Select(a => a.TargetId).FirstOrDefault(placed.ContainsKey);
        }

        return model.Edges
            .Select(e => e.SourceId == id ? e.TargetId : e.TargetId == id ? e.SourceId : null)
            .FirstOrDefault(other => other is not null && placed.ContainsKey(other));
    }

    private static SKRect Rect(ElementLayout layout) => SKRect.Create((float)layout.X, (float)layout.Y, (float)layout.Width, (float)layout.Height);
}
