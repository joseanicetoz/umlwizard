using SkiaSharp;
using UmlWizard.Core.Model;
using UmlWizard.Core.Rendering;
using UmlWizard.Core.Serialization;

namespace UmlWizard.Core.Tests;

/// <summary>Compares rectangles allowing for anti-aliased edges.</summary>
internal sealed class RectTolerance(float tolerance) : IEqualityComparer<SKRect>
{
    public bool Equals(SKRect a, SKRect b) =>
        Math.Abs(a.Left - b.Left) <= tolerance && Math.Abs(a.Top - b.Top) <= tolerance &&
        Math.Abs(a.Right - b.Right) <= tolerance && Math.Abs(a.Bottom - b.Bottom) <= tolerance;

    public int GetHashCode(SKRect rect) => 0;
}
