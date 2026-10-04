using SkiaSharp;

namespace UmlWizard.Ai;

/// <summary>Finds room for a new element without overlapping the others: next to an anchor if it has one, else beside everything.</summary>
internal static class FreeSpace
{
    public const float Gap = 60;
    public const float Clearance = 20;

    /// <summary>The first free spot of the given size, near <paramref name="anchor"/> when there is one.</summary>
    public static SKRect Find(SKRect? anchor, IReadOnlyList<SKRect> occupied, float width, float height) =>
        Candidates(anchor, occupied, width, height).First(r => IsFree(r, occupied));

    public static bool IsFree(SKRect rect, IEnumerable<SKRect> occupied) => !occupied.Any(o => Inflate(o, Clearance).IntersectsWith(rect));

    /// <summary>Where to try, in order: around the anchor, then right of everything, going down, then below everything.</summary>
    public static IEnumerable<SKRect> Candidates(SKRect? anchor, IReadOnlyList<SKRect> occupied, float width, float height)
    {
        if (anchor is { } a)
        {
            yield return SKRect.Create(a.Right + Gap, a.Top, width, height);
            yield return SKRect.Create(a.Left, a.Bottom + Gap, width, height);
            yield return SKRect.Create(a.Left - Gap - width, a.Top, width, height);
            yield return SKRect.Create(a.Left, a.Top - Gap - height, width, height);
            for (var step = 1; step <= 6; step++)
            {
                yield return SKRect.Create(a.Right + Gap, a.Top + step * (height + Gap), width, height);
                yield return SKRect.Create(a.Left + step * (width + Gap), a.Bottom + Gap, width, height);
            }
        }

        var content = occupied.Count == 0 ? SKRect.Create(40, 40, 0, 0) : occupied.Aggregate(SKRect.Union);
        for (var y = content.Top; y < content.Bottom + 2000; y += height + Gap)
        {
            yield return SKRect.Create(content.Right + Gap, y, width, height);
        }

        yield return SKRect.Create(content.Left, content.Bottom + Gap, width, height);
    }

    public static SKRect Inflate(SKRect rect, float by)
    {
        rect.Inflate(by, by);
        return rect;
    }
}
