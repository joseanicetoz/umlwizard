using UmlWizard.CodeImport.CSharp;
using UmlWizard.Core.Model;
using UmlWizard.Core.Serialization;

namespace UmlWizard.CodeImport.Tests;

/// <summary>
/// The C# importer on a folder of idiomatic C# (Shop.Domain, with build output and files that are not C#), imported as
/// Import Source Folder does, and on a folder without types (Shop.Empty).
/// </summary>
public sealed class CSharpImportTests : IDisposable
{
    private readonly string _folder = Directory.CreateTempSubdirectory("umlwizard-csharp-").FullName;

    public CSharpImportTests()
    {
        Write("Shop.Domain/Entity.cs", """
            namespace Shop.Domain;

            public abstract class Entity
            {
                public Guid Id { get; protected set; }

                public abstract bool Validate();
            }
            """);
        Write("Shop.Domain/Order.cs", """
            namespace Shop.Domain;

            public partial class Order : Entity, IPayable
            {
                public const int MaxLines = 100;
                private readonly List<OrderLine> _lines = new();

                public Order(int number) { }

                public Customer? Customer { get; set; }
                public OrderStatus Status { get; set; } = OrderStatus.New;
                public event EventHandler? Changed;
                public OrderLine this[int index] => _lines[index];

                public decimal Total() => 0;

                [Obsolete]
                public void AddLine(OrderLine line, int quantity = 1) { }

                public override bool Validate() => true;

                public bool Pay(decimal amount) => true;

                public static Order operator +(Order a, Order b) => a;
            }
            """);
        Write("Shop.Domain/Orders/Order.Number.cs", """
            namespace Shop.Domain
            {
                public partial class Order
                {
                    public string Number { get; set; } = "";
                }
            }
            """);
        Write("Shop.Domain/IPayable.cs", "namespace Shop.Domain; public interface IPayable : IDisposable { bool Pay(decimal amount); }");
        Write("Shop.Domain/OrderStatus.cs", "namespace Shop.Domain; public enum OrderStatus : byte { New = 1, Paid, Shipped }");
        Write("Shop.Domain/Customer.cs", "namespace Shop.Domain; public record Customer(string Name, string Email);");
        Write("Shop.Domain/OrderLine.cs", "namespace Shop.Domain; public class OrderLine { public Shop.Legacy.Product Product { get; set; } int quantity; }");
        Write("Shop.Domain/Money.cs", "namespace Shop.Domain; public readonly struct Money : IEquatable<Money> { public decimal Amount { get; } public bool Equals(Money other) => true; }");
        Write("Shop.Domain/Extensions.cs", "namespace Shop.Domain; public static class Extensions { public static T Map<T>(this T value) where T : class => value; }");
        Write("Shop.Domain/Repository.cs", "namespace Shop.Domain; public class Repository<T> where T : Entity { public IReadOnlyList<T> Items { get; } = []; }");
        Write("Shop.Domain/Outer.cs", "namespace Shop.Domain; public class Outer { public class Inner { } }");
        Write("Shop.Domain/Broken.cs", "namespace Shop.Domain; public class Broken { public void Run( }");
        Write("Shop.Domain/appsettings.json", "{}");
        Write("Shop.Domain/README.md", "# Domain");
        Write("Shop.Domain/obj/Generated.cs", "public class FromObj { }");
        Write("Shop.Domain/bin/Debug/Copied.cs", "public class FromBin { }");
        Write("Shop.Empty/Nothing.cs", "// Nothing here yet.");
    }

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    [Fact]
    public void TheFolder_BecomesAClassDiagram()
    {
        var result = Import("Shop.Domain");

        var diagram = result.Diagram!;
        Assert.Equal("Shop.Domain", diagram.Name);
        Assert.Equal(DiagramType.Class, diagram.DiagramType);
        Assert.Equal("Imported from the folder Shop.Domain.", diagram.Description);

        // Every class is placed, and the diagram can be saved.
        var model = Model(result);
        Assert.All(model.Classes, c => Assert.NotNull(diagram.Layout.Find(c.Id)));
        Assert.Equal(model.Classes.Count, ((ClassDiagramModel)DiagramSerializer.Deserialize(DiagramSerializer.Serialize(diagram)).Model).Classes.Count);
    }

    [Fact]
    public void AFolderWithoutTypes_HasNoDiagram()
    {
        var result = Import("Shop.Empty");

        Assert.Null(result.Diagram);
        Assert.Equal("0 classes, 0 interfaces and 0 enumerations from the folder Shop.Empty (1 file).", result.Summary);
    }

    [Fact]
    public void Types_MapToClassifiersWithStereotypes()
    {
        var model = Model(Import("Shop.Domain"));

        Assert.Equal(ClassifierKind.AbstractClass, Class(model, "Entity").Kind);
        Assert.Equal(ClassifierKind.Interface, Class(model, "IPayable").Kind);
        Assert.Equal(ClassifierKind.Enumeration, Class(model, "OrderStatus").Kind);
        Assert.Equal("record", Class(model, "Customer").Stereotype);
        Assert.Equal("struct", Class(model, "Money").Stereotype);
        Assert.Equal("static", Class(model, "Extensions").Stereotype);
        Assert.Equal(ClassifierKind.Class, Class(model, "Repository<T>").Kind);
        Assert.NotNull(Class(model, "Outer.Inner"));
        Assert.Equal("Shop.Domain", Class(model, "Order").TaggedValues.Get(CodeImporter.NamespaceTag));
    }

    [Fact]
    public void PartialClass_IsMerged_WithAttributesAndOperations()
    {
        var order = Class(Model(Import("Shop.Domain")), "Order");

        Assert.Equal(["MaxLines", "Status", "Number"], order.Attributes.Select(a => a.Name));
        var maxLines = order.Attributes[0];
        Assert.Equal(("int", UmlVisibility.Public, true, "100"), (maxLines.Type, maxLines.Visibility, maxLines.IsStatic, maxLines.DefaultValue));
        Assert.Equal("OrderStatus.New", order.Attributes[1].DefaultValue);

        Assert.Equal(["Order", "Total", "AddLine", "Validate", "Pay"], order.Operations.Select(o => o.Name));
        Assert.Null(order.Operations[0].ReturnType);
        Assert.Equal("decimal", order.Operations[1].ReturnType);
        Assert.Equal([("line", "OrderLine"), ("quantity", "int")], order.Operations[2].Parameters.Select(p => (p.Name, p.Type)));
    }

    [Fact]
    public void Members_KeepTheirVisibility_AndInterfaceMembersArePublic()
    {
        var model = Model(Import("Shop.Domain"));

        Assert.Equal(UmlVisibility.Private, Class(model, "OrderLine").Attributes.Single(a => a.Name == "quantity").Visibility);
        Assert.Equal(UmlVisibility.Public, Class(model, "IPayable").Operations.Single().Visibility);
        Assert.True(Class(model, "Entity").Operations.Single().IsAbstract);
        var map = Class(model, "Extensions").Operations.Single();
        Assert.Equal(("Map<T>", true), (map.Name, map.IsStatic));
        Assert.Equal(["Name", "Email"], Class(model, "Customer").Attributes.Select(a => a.Name));
        Assert.Equal([("New", "1"), ("Paid", null), ("Shipped", null)], Class(model, "OrderStatus").Attributes.Select(a => (a.Name, a.DefaultValue)));
    }

    [Fact]
    public void BaseTypes_AndReferences_BecomeRelationships()
    {
        var model = Model(Import("Shop.Domain"));
        var order = Class(model, "Order");

        Assert.Contains(model.Relationships, r => r.Kind == RelationshipKind.Inheritance && r.SourceId == order.Id && r.TargetId == Class(model, "Entity").Id);
        Assert.Contains(model.Relationships, r => r.Kind == RelationshipKind.Realization && r.SourceId == order.Id && r.TargetId == Class(model, "IPayable").Id);

        var lines = model.Relationships.Single(r => r.SourceId == order.Id && r.TargetId == Class(model, "OrderLine").Id);
        Assert.Equal((RelationshipKind.DirectedAssociation, "_lines", "*"), (lines.Kind, lines.Label, lines.TargetMultiplicity));
        var customer = model.Relationships.Single(r => r.SourceId == order.Id && r.TargetId == Class(model, "Customer").Id);
        Assert.Equal(("Customer", "0..1"), (customer.Label, customer.TargetMultiplicity));

        // Enumerations stay attribute types; a type's own type parameter is not a class.
        Assert.DoesNotContain(model.Relationships, r => r.TargetId == Class(model, "OrderStatus").Id);
        Assert.Equal("IReadOnlyList<T>", Class(model, "Repository<T>").Attributes.Single().Type);
    }

    [Fact]
    public void Notes_TellWhatWasNotImported()
    {
        var result = Import("Shop.Domain");
        var notes = string.Join("\n", result.Notes);

        Assert.StartsWith("Only C# source files (.cs) are read", result.Notes[0]);
        Assert.Contains("2 other files were skipped: 1 .json, 1 .md", notes);
        Assert.Contains("1 file has syntax errors", notes);
        Assert.Contains("Base types that are not part of the import are not shown: IDisposable, IEquatable<Money>", notes);
        Assert.Contains("1 nested type is shown as a separate class named Outer.Inner", notes);
        Assert.Contains("1 event was left out", notes);
        Assert.Contains("1 indexer was left out", notes);
        Assert.Contains("1 operator was left out", notes);
        Assert.Contains("1 readonly field is shown as a plain attribute", notes);
        Assert.Contains("2 generic constraints (where …) were left out", notes);
        Assert.Contains("2 parameters lost their modifiers (ref, out, in, params) or default values", notes);
        Assert.Contains("1 declaration has C# attributes", notes);
        Assert.Equal("10 classes, 1 interface and 1 enumeration from the folder Shop.Domain (12 files).", result.Summary);
    }

    /// <summary>Imports a folder as Import Source Folder does: its C# files, read by the C# plug-in.</summary>
    private CodeImportResult Import(string folder)
    {
        var importer = new CSharpCodeImporter();
        return CodeImporter.Import(SourceFolder.Read(Path.Combine(_folder, folder), importer.FileExtensions), importer);
    }

    private static ClassDiagramModel Model(CodeImportResult result) => (ClassDiagramModel)result.Diagram!.Model;

    private static UmlClass Class(ClassDiagramModel model, string name) => model.Classes.Single(c => c.Name == name);

    private void Write(string relativePath, string content)
    {
        var path = Path.Combine(_folder, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }
}
