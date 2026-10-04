using SkiaSharp;

namespace UmlWizard.Core.Rendering;

internal static class Geometry
{
    public static SKPoint Center(SKRect rect) => new(rect.MidX, rect.MidY);

    public static SKPoint Add(SKPoint a, SKPoint b) => new(a.X + b.X, a.Y + b.Y);

    public static SKPoint Scale(SKPoint v, float factor) => new(v.X * factor, v.Y * factor);

    public static SKPoint Normalize(SKPoint v)
    {
        var length = MathF.Sqrt(v.X * v.X + v.Y * v.Y);
        return length < 1e-4f ? new SKPoint(1, 0) : new SKPoint(v.X / length, v.Y / length);
    }

    public static SKPoint Perpendicular(SKPoint v) => new(-v.Y, v.X);

    /// <summary>Point where the ray from the rectangle centre towards <paramref name="toward"/> leaves the rectangle.</summary>
    public static SKPoint ClipToRect(SKRect rect, SKPoint toward)
    {
        var center = Center(rect);
        var dx = toward.X - center.X;
        var dy = toward.Y - center.Y;
        if (MathF.Abs(dx) < 1e-4f && MathF.Abs(dy) < 1e-4f)
        {
            return center;
        }

        var tx = MathF.Abs(dx) < 1e-4f ? float.MaxValue : rect.Width / 2f / MathF.Abs(dx);
        var ty = MathF.Abs(dy) < 1e-4f ? float.MaxValue : rect.Height / 2f / MathF.Abs(dy);
        var t = MathF.Min(1f, MathF.Min(tx, ty));
        return new SKPoint(center.X + dx * t, center.Y + dy * t);
    }

    public static float DistanceToSegment(SKPoint p, SKPoint a, SKPoint b)
    {
        var abx = b.X - a.X;
        var aby = b.Y - a.Y;
        var lengthSquared = abx * abx + aby * aby;
        var t = lengthSquared < 1e-6f ? 0f : Math.Clamp(((p.X - a.X) * abx + (p.Y - a.Y) * aby) / lengthSquared, 0f, 1f);
        var dx = p.X - (a.X + t * abx);
        var dy = p.Y - (a.Y + t * aby);
        return MathF.Sqrt(dx * dx + dy * dy);
    }
}
