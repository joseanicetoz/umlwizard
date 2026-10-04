using Avalonia;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Rendering.SceneGraph;
using Avalonia.Skia;
using SkiaSharp;

namespace UmlWizard.App.Controls;

/// <summary>
/// Draws a pre-recorded <see cref="SKPicture"/> directly onto Avalonia's Skia canvas. The picture is recorded
/// on the UI thread, so the render thread never touches the (mutable) diagram model.
/// </summary>
internal sealed class SkiaPictureDrawOperation(Rect bounds, SKPicture picture) : ICustomDrawOperation
{
    public Rect Bounds => bounds;

    public bool HitTest(Point p) => bounds.Contains(p);

    public bool Equals(ICustomDrawOperation? other) => ReferenceEquals(this, other);

    public void Render(ImmediateDrawingContext context)
    {
        if (context.TryGetFeature(typeof(ISkiaSharpApiLeaseFeature)) is not ISkiaSharpApiLeaseFeature leaseFeature)
        {
            return;
        }

        using var lease = leaseFeature.Lease();
        var canvas = lease.SkCanvas;
        canvas.Save();
        canvas.ClipRect(SKRect.Create((float)bounds.X, (float)bounds.Y, (float)bounds.Width, (float)bounds.Height));
        canvas.DrawPicture(picture);
        canvas.Restore();
    }

    public void Dispose() => picture.Dispose();
}
