using SkiaSharp;
using UmlWizard.Core.Editing;
using UmlWizard.Core.Model;
using UmlWizard.Core.Rendering;
using UmlWizard.Core.Serialization;

namespace UmlWizard.Core.Tests;

public sealed class RenderingTests
{
    [Fact]
    public void ClassHitTest_FindsHeaderRowsAndEmptyCompartments()
    {
        var editor = new DiagramEditor(DiagramDocument.Create("T", DiagramType.Class));
        var cls = editor.AddClass(ClassifierKind.Class, 100, 100);
        cls.Attributes.Add(new UmlAttribute { Name = "a" });
        using var renderer = DiagramRenderer.Create(DiagramType.Class);
        renderer.UpdateLayout(editor.Document);
        var layout = editor.Layout.Find(cls.Id)!;

        var header = renderer.HitTest(editor.Document, new SKPoint(150, 110));
        var firstRow = renderer.GetEditRect(editor.Document, cls.Id, HitPart.ClassAttribute, 0)!.Value;
        var attribute = renderer.HitTest(editor.Document, new SKPoint(firstRow.MidX, firstRow.MidY));
        var operations = renderer.HitTest(editor.Document, new SKPoint(150, (float)(layout.Y + layout.Height - 3)));
        var outside = renderer.HitTest(editor.Document, new SKPoint(10, 10));

        Assert.Equal(HitPart.ClassHeader, header?.Part);
        Assert.Equal(new HitInfo(cls.Id, HitPart.ClassAttribute, 0), attribute);
        Assert.Equal(HitPart.ClassOperationsArea, operations?.Part);
        Assert.Null(outside);
    }

    [Fact]
    public void ClassWidth_GrowsWithContent()
    {
        var editor = new DiagramEditor(DiagramDocument.Create("T", DiagramType.Class));
        var cls = editor.AddClass(ClassifierKind.Class, 0, 0);
        using var renderer = DiagramRenderer.Create(DiagramType.Class);
        renderer.UpdateLayout(editor.Document);
        var before = editor.Layout.Find(cls.Id)!.Width;

        cls.Operations.Add(new UmlOperation { Name = "aVeryLongOperationNameThatNeedsSpace", ReturnType = "IReadOnlyDictionary<string, int>" });
        renderer.UpdateLayout(editor.Document);

        Assert.True(editor.Layout.Find(cls.Id)!.Width > before);
    }

    [Fact]
    public void SequenceHitTest_FindsMessagesAndLifelines()
    {
        var editor = new DiagramEditor(DiagramDocument.Create("T", DiagramType.Sequence));
        var a = editor.AddLifeline(LifelineKind.Object, 0);
        var b = editor.AddLifeline(LifelineKind.Object, 300);
        var message = editor.AddMessage(MessageKind.Synchronous, a.Id, b.Id, 150)!;
        using var renderer = DiagramRenderer.Create(DiagramType.Sequence);
        renderer.UpdateLayout(editor.Document);

        var aCenter = (float)(editor.Layout.Find(a.Id)!.X + editor.Layout.Find(a.Id)!.Width / 2);

        Assert.Equal(new HitInfo(message.Id, HitPart.Message), renderer.HitTest(editor.Document, new SKPoint(200, 151)));
        Assert.Equal(new HitInfo(a.Id, HitPart.LifelineHead), renderer.HitTest(editor.Document, new SKPoint(aCenter, 30)));
        Assert.Equal(new HitInfo(a.Id, HitPart.LifelineBody), renderer.HitTest(editor.Document, new SKPoint(aCenter, 250)));
    }

    [Fact]
    public void ExportPng_WritesImagesForSampleDiagrams()
    {
        var projectPath = Path.Combine(AppContext.BaseDirectory, "samples", "OnlineShop", "OnlineShop.umlwiz");
        var project = ProjectSerializer.Load(projectPath);
        foreach (var document in project.Diagrams)
        {
            var output = Path.Combine(Path.GetTempPath(), $"umlwizard-{document.Id}.png");

            DiagramExporter.ExportPng(document, output);

            using var bitmap = SKBitmap.Decode(output);
            Assert.NotNull(bitmap);
            if (document.Model.AllElements().Any())
            {
                Assert.True(bitmap.Width > 300 && bitmap.Height > 300);
            }
        }
    }
}
