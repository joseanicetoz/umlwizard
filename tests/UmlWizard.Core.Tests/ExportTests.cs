using SkiaSharp;
using UmlWizard.Core.Editing;
using UmlWizard.Core.Model;
using UmlWizard.Core.Rendering;

namespace UmlWizard.Core.Tests;

public sealed class ExportTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"umlwizard-export-{Guid.NewGuid():N}.png");

    public void Dispose() => File.Delete(_path);

    /// <summary>A class diagram with one class; exported at scale 1, the class starts at the 24 px margin.</summary>
    private static DiagramDocument OneClass()
    {
        var editor = new DiagramEditor(DiagramDocument.Create("Export", DiagramType.Class));
        editor.AddClass(ClassifierKind.Class, 0, 0);
        editor.SelectedId = null;
        return editor.Document;
    }

    [Fact]
    public void Export_HasAnOpaqueWhiteBackground_ByDefault()
    {
        DiagramExporter.ExportPng(OneClass(), _path, scale: 1f);

        using var bitmap = SKBitmap.Decode(_path);
        Assert.Equal(SKColors.White, bitmap.GetPixel(2, 2));
    }

    [Fact]
    public void TransparentExport_LeavesTheBackgroundClear_ButKeepsWhiteShapesWhite()
    {
        DiagramExporter.ExportPng(OneClass(), _path, scale: 1f, transparent: true);

        using var bitmap = SKBitmap.Decode(_path);
        Assert.Equal(0, bitmap.GetPixel(2, 2).Alpha);
        Assert.Equal(0, bitmap.GetPixel(bitmap.Width - 3, bitmap.Height - 3).Alpha);

        // Inside the class, below its header, the white fill stays opaque.
        var inside = bitmap.GetPixel(24 + 60, bitmap.Height - 24 - 4);
        Assert.Equal((byte)255, inside.Alpha);
        Assert.True(inside.Red > 240 && inside.Green > 240 && inside.Blue > 240, inside.ToString());
    }
}
