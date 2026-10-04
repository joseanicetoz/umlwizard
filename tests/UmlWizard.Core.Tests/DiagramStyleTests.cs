using SkiaSharp;
using UmlWizard.Core.Editing;
using UmlWizard.Core.Model;
using UmlWizard.Core.Rendering;

namespace UmlWizard.Core.Tests;

public sealed class DiagramStyleTests
{
    private static readonly DiagramStyle Preferred = new()
    {
        Fills = { ["Class"] = "#FFE0E0", ["Interface"] = "#E0FFE0", [FillSlot.Note] = "#E0E0FF" },
        Border = "#C62828",
        BorderWidth = 3,
    };

    [Fact]
    public void AnElementsOwnStyle_WinsPartByPart_OverThePreferences()
    {
        var cls = new UmlClass { Kind = ClassifierKind.Class };
        static (string?, string?, double?) Parts(ShapeStyle? s) => (s?.Fill, s?.Border, s?.BorderWidth);

        Assert.Equal(("#FFE0E0", "#C62828", 3.0), Parts(Preferred.Apply(cls, null)));
        Assert.Equal(("#123456", "#C62828", 1.0), Parts(Preferred.Apply(cls, new ShapeStyle { Fill = "#123456", BorderWidth = 1 })));

        // Kinds without a preferred fill keep the theme's, but take the border.
        Assert.Equal((null, "#C62828", 3.0), Parts(Preferred.Apply(new UmlClass { Kind = ClassifierKind.Enumeration }, null)));
        Assert.Equal("#E0FFE0", Preferred.Apply(new UmlClass { Kind = ClassifierKind.Interface }, null)!.Fill);
        Assert.Equal("#E0E0FF", Preferred.Apply(new NoteElement(), null)!.Fill);

        // Text boxes have no border by default, so they do not get one.
        Assert.Null(Preferred.Apply(new TextBoxElement(), null));
        Assert.Null(new DiagramStyle().Apply(cls, null));
    }

    [Fact]
    public void EveryDiagramType_OffersTheFillsOfItsShapes_WithTheThemesDefaults()
    {
        Assert.Equal(["Class", "AbstractClass", "Interface", "Enumeration", "Note"], FillSlot.For(DiagramType.Class).Select(s => s.Key));
        Assert.Equal(["Lifeline", "Note"], FillSlot.For(DiagramType.Sequence).Select(s => s.Key));
        Assert.All(Enum.GetValues<DiagramType>(), t => Assert.Contains(FillSlot.For(t), s => s.Key == FillSlot.Note));
        Assert.Equal(RenderTheme.Dark.InterfaceHeader, FillSlot.For(DiagramType.Class).Single(s => s.Key == "Interface").Default(RenderTheme.Dark));

        // Every slot names an element kind of its diagram type.
        foreach (var type in Enum.GetValues<DiagramType>().Where(GraphRules.IsGraphDiagram))
        {
            var keys = GraphRules.NodeKinds(type).Select(k => FillSlot.KeyOf(new GraphNode { Kind = k })).OfType<string>().Append(FillSlot.Note);
            Assert.All(FillSlot.For(type), slot => Assert.Contains(slot.Key, keys));
        }
    }

    [Fact]
    public void ThePreferences_AreDrawn_OnTheCanvasAndInExports_UnlessAShapeHasItsOwnFill()
    {
        var editor = new DiagramEditor(DiagramDocument.Create("Test", DiagramType.Class));
        var plain = editor.AddClass(ClassifierKind.Class, 40, 40);
        var own = editor.AddClass(ClassifierKind.Class, 300, 40);
        editor.SetShapeStyle(own.Id, new ShapeStyle { Fill = "#102030" });

        using var renderer = DiagramRenderer.Create(DiagramType.Class);
        renderer.Defaults = Preferred;
        using var bitmap = new SKBitmap(600, 300);
        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(SKColors.White);
            renderer.UpdateLayout(editor.Document);
            renderer.Render(canvas, editor.Document, new RenderOptions());
        }

        SKColor HeaderOf(UmlClass cls)
        {
            var bounds = renderer.GetElementBounds(editor.Document, cls.Id)!.Value;
            return bitmap.GetPixel((int)bounds.Left + 6, (int)bounds.Top + 6);
        }

        Assert.Equal(new SKColor(0xFF, 0xE0, 0xE0), HeaderOf(plain));
        Assert.Equal(new SKColor(0x10, 0x20, 0x30), HeaderOf(own));

        // Exports draw them too.
        var path = Path.Combine(Path.GetTempPath(), $"umlwizard-style-{Guid.NewGuid():N}.png");
        try
        {
            DiagramExporter.ExportPng(editor.Document, path, scale: 1, defaults: Preferred);
            using var exported = SKBitmap.Decode(path);
            var bounds = renderer.GetContentBounds(editor.Document);
            var plainBounds = renderer.GetElementBounds(editor.Document, plain.Id)!.Value;
            Assert.Equal(
                new SKColor(0xFF, 0xE0, 0xE0),
                exported.GetPixel((int)(plainBounds.Left - bounds.Left + 24 + 6), (int)(plainBounds.Top - bounds.Top + 24 + 6)));
        }
        finally
        {
            File.Delete(path);
        }
    }
}
