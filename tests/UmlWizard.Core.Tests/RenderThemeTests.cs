using SkiaSharp;
using UmlWizard.Core.Editing;
using UmlWizard.Core.Model;
using UmlWizard.Core.Rendering;

namespace UmlWizard.Core.Tests;

public sealed class RenderThemeTests
{
    public static TheoryData<string> Themes => ["Light", "Dark"];

    private static RenderTheme Theme(string name) => name == "Dark" ? RenderTheme.Dark : RenderTheme.Light;

    /// <summary>Every surface text is drawn on.</summary>
    private static IEnumerable<(string Name, SKColor Color)> TextSurfaces(RenderTheme t) =>
    [
        (nameof(t.CanvasBackground), t.CanvasBackground), (nameof(t.NodeFill), t.NodeFill), (nameof(t.NoteFill), t.NoteFill),
        (nameof(t.ClassHeader), t.ClassHeader), (nameof(t.AbstractHeader), t.AbstractHeader),
        (nameof(t.InterfaceHeader), t.InterfaceHeader), (nameof(t.EnumerationHeader), t.EnumerationHeader),
        (nameof(t.LifelineHead), t.LifelineHead), (nameof(t.StateFill), t.StateFill), (nameof(t.UseCaseFill), t.UseCaseFill),
        (nameof(t.ComponentFill), t.ComponentFill), (nameof(t.BoundaryFill), t.BoundaryFill),
        (nameof(t.DeploymentNodeFill), t.DeploymentNodeFill), (nameof(t.ArtifactFill), t.ArtifactFill),
    ];

    /// <summary>WCAG contrast ratio, 1 to 21.</summary>
    private static double Contrast(SKColor a, SKColor b)
    {
        static double Luminance(SKColor c)
        {
            static double Channel(byte value)
            {
                var v = value / 255.0;
                return v <= 0.03928 ? v / 12.92 : Math.Pow((v + 0.055) / 1.055, 2.4);
            }

            return 0.2126 * Channel(c.Red) + 0.7152 * Channel(c.Green) + 0.0722 * Channel(c.Blue);
        }

        var (la, lb) = (Luminance(a), Luminance(b));
        return (Math.Max(la, lb) + 0.05) / (Math.Min(la, lb) + 0.05);
    }

    [Theory]
    [MemberData(nameof(Themes))]
    public void Text_IsReadable_OnEverySurface(string name)
    {
        var theme = Theme(name);
        foreach (var (surface, color) in TextSurfaces(theme))
        {
            Assert.True(Contrast(theme.Text, color) >= 4.5, $"{name}: text on {surface} is {Contrast(theme.Text, color):F2}:1");
        }

        Assert.True(Contrast(theme.MutedText, theme.CanvasBackground) >= 4.5);
        Assert.True(Contrast(theme.MutedText, theme.NodeFill) >= 4.5);
    }

    [Theory]
    [MemberData(nameof(Themes))]
    public void LinesAndSelection_StandOut_FromTheCanvas(string name)
    {
        var theme = Theme(name);
        Assert.True(Contrast(theme.Line, theme.CanvasBackground) >= 3);
        Assert.True(Contrast(theme.Border, theme.CanvasBackground) >= 3);
        Assert.True(Contrast(theme.Selection, theme.CanvasBackground) >= 3);
    }

    [Fact]
    public void Renderer_DrawsInTheLightColours_UntilItsThemeChanges()
    {
        var editor = new DiagramEditor(DiagramDocument.Create("Test", DiagramType.Class));
        var note = editor.AddNote(0, 0);
        note.Text = string.Empty;
        editor.SelectedId = null;
        editor.ResizeElement(note.Id, 200, 100);
        using var renderer = DiagramRenderer.Create(DiagramType.Class);
        Assert.Same(RenderTheme.Light, renderer.Theme);

        using (var light = Render(renderer, editor.Document))
        {
            Assert.Equal(RenderTheme.Light.NoteFill, light.GetPixel(100, 60));
        }

        renderer.Theme = RenderTheme.Dark;
        using var dark = Render(renderer, editor.Document);
        Assert.Equal(RenderTheme.Dark.NoteFill, dark.GetPixel(100, 60));
    }

    private static SKBitmap Render(DiagramRenderer renderer, DiagramDocument document)
    {
        var bitmap = new SKBitmap(220, 120);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(renderer.Theme.CanvasBackground);
        renderer.Render(canvas, document, new RenderOptions());
        canvas.Flush();
        return bitmap;
    }
}
