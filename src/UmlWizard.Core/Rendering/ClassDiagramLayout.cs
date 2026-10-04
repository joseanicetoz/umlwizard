using UmlWizard.Core.Model;

namespace UmlWizard.Core.Rendering;

/// <summary>
/// Places the classes of a generated class diagram (an import) in rows: the classes <c>first</c> picks (e.g. the «api»
/// interfaces of an OpenAPI import), then base classes before the classes that inherit from them, then the other classes
/// and the enumerations. Rows wrap at a width that keeps the diagram roughly twice as wide as it is tall.
/// </summary>
public static class ClassDiagramLayout
{
    private const double Margin = 40;
    private const double GapX = 40;
    private const double GapY = 60;
    private const double DefaultWidth = 160;

    public static void Arrange(DiagramDocument document, Func<UmlClass, bool>? first = null)
    {
        var model = (ClassDiagramModel)document.Model;
        foreach (var cls in model.Classes.Where(c => document.Layout.Find(c.Id) is null))
        {
            document.Layout.Add(new ElementLayout { ElementId = cls.Id, Width = DefaultWidth });
        }

        using var renderer = DiagramRenderer.Create(DiagramType.Class);
        renderer.UpdateLayout(document);
        var sizes = model.Classes.ToDictionary(c => c.Id, c => renderer.GetElementBounds(document, c.Id) is { } b ? (b.Width, b.Height) : (160f, 80f));

        var leading = first is null ? [] : model.Classes.Where(first).ToList();
        var enums = model.Classes.Where(c => c.Kind == ClassifierKind.Enumeration).Except(leading).ToList();
        var others = Ordered(model, model.Classes.Except(leading).Except(enums).ToList());

        var area = sizes.Values.Sum(s => (s.Item1 + GapX) * (s.Item2 + GapY));
        var rowWidth = Math.Max(1200, Math.Sqrt(area * 2));

        var y = Margin;
        foreach (var group in new[] { leading, others, enums }.Where(g => g.Count > 0))
        {
            var x = Margin;
            var rowHeight = 0.0;
            foreach (var cls in group)
            {
                var (width, height) = sizes[cls.Id];
                if (x > Margin && x + width > Margin + rowWidth)
                {
                    x = Margin;
                    y += rowHeight + GapY;
                    rowHeight = 0;
                }

                var layout = document.Layout.Find(cls.Id)!;
                layout.X = x;
                layout.Y = y;
                x += width + GapX;
                rowHeight = Math.Max(rowHeight, height);
            }

            y += rowHeight + GapY;
        }
    }

    /// <summary>Base classes before the classes that inherit from them; otherwise in diagram order.</summary>
    private static List<UmlClass> Ordered(ClassDiagramModel model, List<UmlClass> classes)
    {
        var bases = model.Relationships
            .Where(r => r.Kind is RelationshipKind.Inheritance or RelationshipKind.Realization)
            .ToLookup(r => r.SourceId, r => r.TargetId);
        var result = new List<UmlClass>();
        var visiting = new HashSet<string>();
        foreach (var cls in classes)
        {
            Visit(cls);
        }

        return result;

        void Visit(UmlClass cls)
        {
            if (result.Contains(cls) || !visiting.Add(cls.Id))
            {
                return;
            }

            foreach (var baseClass in bases[cls.Id].Select(model.FindClass).OfType<UmlClass>().Where(classes.Contains))
            {
                Visit(baseClass);
            }

            result.Add(cls);
        }
    }
}
