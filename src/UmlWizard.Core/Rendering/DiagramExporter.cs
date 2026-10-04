using SkiaSharp;
using UmlWizard.Core.Model;

namespace UmlWizard.Core.Rendering;

public static class DiagramExporter
{
    /// <summary>Renders the whole diagram to a PNG image, always in the <see cref="RenderTheme.Light"/> colours.</summary>
    /// <param name="transparent">
    /// Leave the background transparent instead of white. Shapes that are filled white, and the backgrounds of
    /// labels, stay white.
    /// </param>
    /// <param name="defaults">The user's preferred look of the shapes of the diagram's type, as on the canvas.</param>
    public static void ExportPng(
        DiagramDocument document, string path, float scale = 2f, float margin = 24f, bool transparent = false, DiagramStyle? defaults = null)
    {
        // Exported images do not depend on the theme the application happens to be in.
        using var renderer = DiagramRenderer.Create(document.DiagramType);
        renderer.Theme = RenderTheme.Light;
        renderer.Defaults = defaults;
        renderer.UpdateLayout(document);

        var bounds = renderer.GetContentBounds(document);
        if (bounds.IsEmpty)
        {
            bounds = SKRect.Create(0, 0, 200, 100);
        }

        bounds.Inflate(margin, margin);
        var info = new SKImageInfo(
            Math.Max(1, (int)MathF.Ceiling(bounds.Width * scale)),
            Math.Max(1, (int)MathF.Ceiling(bounds.Height * scale)));

        using var surface = SKSurface.Create(info);
        var canvas = surface.Canvas;
        canvas.Clear(transparent ? SKColors.Transparent : renderer.Theme.CanvasBackground);
        canvas.Scale(scale);
        canvas.Translate(-bounds.Left, -bounds.Top);
        renderer.Render(canvas, document, new RenderOptions());

        using var image = surface.Snapshot();
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        using var stream = File.Create(path);
        data.SaveTo(stream);
    }
}
