using SkiaSharp;
using UmlWizard.Core.Editing;
using UmlWizard.Core.Model;
using UmlWizard.Core.Rendering;
using UmlWizard.Core.Serialization;

namespace UmlWizard.Core.Tests;

public sealed class FontTests
{
    private static DiagramFont Font(double size, string? family = null, bool bold = false, bool italic = false) =>
        new() { Family = family, Size = size, Bold = bold, Italic = italic };

    [Fact]
    public void Fonts_RoundTripInLayout()
    {
        var document = DiagramDocument.Create("D", DiagramType.Class);
        document.Layout.Font = Font(15, "Arial", italic: true);
        document.Layout.Add(new ElementLayout { ElementId = "c1", Font = Font(20, "Consolas", bold: true) });
        document.Layout.Add(new ElementLayout { ElementId = "c2" });

        var json = DiagramSerializer.Serialize(document);
        var restored = DiagramSerializer.Deserialize(json);

        Assert.Equal("Arial", restored.Layout.Font!.Family);
        Assert.True(restored.Layout.Font.Italic);
        var element = restored.Layout.Find("c1")!.Font!;
        Assert.Equal(("Consolas", 20d, true, false), (element.Family, element.Size, element.Bold, element.Italic));
        Assert.Null(restored.Layout.Find("c2")!.Font);
        Assert.Equal(1, json.Split("\"family\": \"Consolas\"").Length - 1);
    }

    [Fact]
    public void FontFor_PrefersElementFontOverDiagramFont()
    {
        var layout = new DiagramLayout { Font = Font(15) };
        layout.Add(new ElementLayout { ElementId = "a", Font = Font(20) });
        layout.Add(new ElementLayout { ElementId = "b" });

        Assert.Equal(20, layout.FontFor("a")!.Size);
        Assert.Equal(15, layout.FontFor("b")!.Size);
        Assert.Equal(15, layout.FontFor("missing")!.Size);
    }

    [Fact]
    public void SetFont_IsUndoable_ForElementAndDiagram()
    {
        var editor = new DiagramEditor(DiagramDocument.Create("T", DiagramType.Class));
        var cls = editor.AddClass(ClassifierKind.Class, 0, 0);
        var font = Font(18, "Arial");

        editor.SetFont(cls.Id, font);
        font.Size = 99;
        editor.SetFont(null, Font(14));

        Assert.Equal(18, editor.Layout.Find(cls.Id)!.Font!.Size);
        Assert.Equal(14, editor.Layout.Font!.Size);

        editor.Undo();
        Assert.Null(editor.Layout.Font);
        Assert.Equal(18, editor.Layout.Find(cls.Id)!.Font!.Size);

        editor.Undo();
        Assert.Null(editor.Layout.Find(cls.Id)!.Font);

        editor.Redo();
        Assert.Equal("Arial", editor.Layout.Find(cls.Id)!.Font!.Family);
    }

    [Fact]
    public void RelationshipFont_CreatesLayoutEntry_RemovedWithRelationship()
    {
        var editor = new DiagramEditor(DiagramDocument.Create("T", DiagramType.Class));
        var a = editor.AddClass(ClassifierKind.Class, 0, 0);
        var b = editor.AddClass(ClassifierKind.Class, 300, 0);
        var relationship = editor.AddRelationship(RelationshipKind.Association, a.Id, b.Id)!;

        editor.SetFont(relationship.Id, Font(16));
        Assert.Equal(16, editor.Layout.FontFor(relationship.Id)!.Size);

        editor.Delete(relationship.Id);
        Assert.Null(editor.Layout.Find(relationship.Id));
    }

    [Fact]
    public void CopyPaste_KeepsFontAsIndependentCopy()
    {
        var editor = new DiagramEditor(DiagramDocument.Create("T", DiagramType.Class));
        var cls = editor.AddClass(ClassifierKind.Class, 0, 0);
        editor.SetFont(cls.Id, Font(21, "Arial"));

        var pastedId = editor.Paste(editor.Copy(cls.Id))!;
        editor.Layout.Find(pastedId)!.Font!.Size = 30;

        Assert.Equal(21, editor.Layout.Find(cls.Id)!.Font!.Size);
        Assert.Equal("Arial", editor.Layout.Find(pastedId)!.Font!.Family);
    }

    [Fact]
    public void LargerFont_MakesClassLarger_AndElementFontOverridesDiagramFont()
    {
        var editor = new DiagramEditor(DiagramDocument.Create("T", DiagramType.Class));
        var small = editor.AddClass(ClassifierKind.Class, 0, 0);
        var large = editor.AddClass(ClassifierKind.Class, 400, 0);
        foreach (var cls in new[] { small, large })
        {
            cls.Name = "AVeryLongClassNameForMeasuring";
            cls.Attributes.Add(new UmlAttribute { Name = "value", Type = "string" });
        }

        using var renderer = DiagramRenderer.Create(DiagramType.Class);
        renderer.UpdateLayout(editor.Document);
        var defaultHeight = editor.Layout.Find(small.Id)!.Height;

        editor.Layout.Font = Font(9);
        editor.Layout.Find(large.Id)!.Font = Font(26);
        renderer.UpdateLayout(editor.Document);

        var smallLayout = editor.Layout.Find(small.Id)!;
        var largeLayout = editor.Layout.Find(large.Id)!;
        Assert.True(smallLayout.Height < defaultHeight);
        Assert.True(largeLayout.Height > defaultHeight);
        Assert.True(largeLayout.Width > smallLayout.Width);

        var nameRect = renderer.GetEditRect(editor.Document, large.Id, HitPart.ClassHeader)!.Value;
        Assert.Equal(MathF.Ceiling(26f * 18f / 13f), nameRect.Height);
    }

    [Fact]
    public void LargerFont_MakesLifelineHeadTaller_AndMessagesStayBelowIt()
    {
        var editor = new DiagramEditor(DiagramDocument.Create("T", DiagramType.Sequence));
        var lifeline = editor.AddLifeline(LifelineKind.Object, 40);
        using var renderer = DiagramRenderer.Create(DiagramType.Sequence);

        editor.SetFont(lifeline.Id, Font(30));
        renderer.UpdateLayout(editor.Document);

        var layout = editor.Layout.Find(lifeline.Id)!;
        Assert.True(layout.Height > SequenceDiagramRenderer.ObjectHeadHeight);
        Assert.True(SequenceDiagramRenderer.MinimumMessageY(editor.Layout, lifeline) >= layout.Y + layout.Height);
        Assert.Equal(HitPart.LifelineHead,
            renderer.HitTest(editor.Document, new SKPoint((float)(layout.X + 10), (float)(layout.Y + layout.Height - 2)))?.Part);
    }

    [Fact]
    public void UnknownFamilyAndOutOfRangeSize_StillRender()
    {
        var document = DiagramDocument.Create("T", DiagramType.Class);
        var editor = new DiagramEditor(document);
        var cls = editor.AddClass(ClassifierKind.Class, 0, 0);
        editor.SetFont(cls.Id, Font(500, "No Such Font Family"));
        var output = Path.Combine(Path.GetTempPath(), $"umlwizard-font-{Guid.NewGuid():N}.png");

        try
        {
            DiagramExporter.ExportPng(document, output);

            using var bitmap = SKBitmap.Decode(output);
            Assert.NotNull(bitmap);
            Assert.True(editor.Layout.Find(cls.Id)!.Height < 500);
        }
        finally
        {
            File.Delete(output);
        }
    }
}
