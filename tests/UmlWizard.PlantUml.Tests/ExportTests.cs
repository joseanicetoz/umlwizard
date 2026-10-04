using UmlWizard.Core.Model;

namespace UmlWizard.PlantUml.Tests;

/// <summary>The PlantUML written for a diagram, and what cannot be written in it.</summary>
public sealed class ExportTests
{
    [Fact]
    public void Class_MembersStereotypesRelationshipsAndEndpoints()
    {
        var order = new UmlClass
        {
            Name = "Order",
            Stereotype = "aggregate root",
            Attributes =
            {
                new UmlAttribute { Name = "number", Type = "string" },
                new UmlAttribute { Name = "count", Type = "int", Visibility = UmlVisibility.Public, IsStatic = true, DefaultValue = "0" },
            },
            Operations = { new UmlOperation { Name = "getOrder", ReturnType = "Order", HttpMethod = "GET", Path = "/orders/{id}", Parameters = { new UmlParameter { Name = "id", Type = "string" } } } },
        };
        var line = new UmlClass { Name = "Order Line" };
        var repository = new UmlClass { Name = "Repository<T>", Kind = ClassifierKind.Interface };
        var document = DiagramDocument.Create("Shop", DiagramType.Class);
        var model = (ClassDiagramModel)document.Model;
        model.Classes.AddRange([order, line, repository]);
        model.Relationships.Add(new UmlRelationship { Kind = RelationshipKind.Composition, SourceId = order.Id, TargetId = line.Id, SourceMultiplicity = "1", TargetMultiplicity = "1..*", Label = "lines" });
        model.Relationships.Add(new UmlRelationship { Kind = RelationshipKind.Realization, SourceId = order.Id, TargetId = repository.Id });

        var result = PlantUmlExporter.Export(document);

        Assert.Equal(
            """
            @startuml
            title Shop
            class Order <<aggregate root>> {
              -number : string
              {static} +count : int = 0
              +getOrder(id : string) : Order <<GET /orders/{id}>>
            }
            class "Order Line" as Order_Line
            interface Repository<T>

            Order "1" *-- "1..*" Order_Line : lines
            Order ..|> Repository
            @enduml

            """.Replace("\r\n", "\n"),
            result.Text);
        Assert.Empty(result.Warnings);
    }

    [Fact]
    public void Sequence_ParticipantsInOrder_CreationRepliesAndNotes()
    {
        var customer = new Lifeline { Name = "customer", Kind = LifelineKind.Actor };
        var shop = new Lifeline { Name = "shop", Type = "ShopController" };
        var receipt = new Lifeline { Name = "", Type = "Receipt" };
        var document = DiagramDocument.Create("Checkout", DiagramType.Sequence);
        var model = (SequenceDiagramModel)document.Model;
        model.Lifelines.AddRange([customer, shop, receipt]);
        var call = new SequenceMessage { SourceId = customer.Id, TargetId = shop.Id, Text = "checkout()" };
        var create = new SequenceMessage { SourceId = shop.Id, TargetId = receipt.Id, Text = "new", Kind = MessageKind.Create };
        var reply = new SequenceMessage { SourceId = shop.Id, TargetId = customer.Id, Text = "done", Kind = MessageKind.Reply };
        model.Messages.AddRange([reply, call, create]);
        var note = new NoteElement { Text = "paid" };
        model.Notes.Add(note);
        model.NoteAttachments.Add(new NoteAttachment { NoteId = note.Id, TargetId = create.Id });
        var x = 0;
        foreach (var lifeline in model.Lifelines)
        {
            document.Layout.Add(new ElementLayout { ElementId = lifeline.Id, X = x += 100 });
        }

        document.Layout.Add(new ElementLayout { ElementId = call.Id, Y = 100 });
        document.Layout.Add(new ElementLayout { ElementId = create.Id, Y = 140 });
        document.Layout.Add(new ElementLayout { ElementId = reply.Id, Y = 180 });

        var text = PlantUmlExporter.Export(document).Text;

        Assert.Contains(
            """
            actor customer
            participant "shop : ShopController" as shop
            participant ": Receipt" as Receipt

            customer -> shop : checkout()
            create Receipt
            shop -> Receipt : new
            note right : paid
            shop --> customer : done
            """.Replace("\r\n", "\n"),
            text);
    }

    [Fact]
    public void Activity_AFlowTheStructuredSyntaxCannotExpress_IsReported()
    {
        // A jump back into the middle of a branch that is not a loop the syntax has.
        var document = DiagramDocument.Create("Jumps", DiagramType.Activity);
        var model = (GraphDiagramModel)document.Model;
        GraphNode Add(GraphNodeKind kind, string? name = null)
        {
            var node = new GraphNode { Kind = kind, Name = name };
            model.Nodes.Add(node);
            return node;
        }

        void Flow(GraphNode from, GraphNode to) => model.Edges.Add(new GraphEdge { Kind = GraphEdgeKind.ControlFlow, SourceId = from.Id, TargetId = to.Id });
        var start = Add(GraphNodeKind.InitialNode);
        var a = Add(GraphNodeKind.Action, "A");
        var b = Add(GraphNodeKind.Action, "B");
        var c = Add(GraphNodeKind.Action, "C");
        var end = Add(GraphNodeKind.ActivityFinal);
        Flow(start, a);
        Flow(a, b);
        Flow(b, c);
        Flow(c, end);
        Flow(c, a);
        Flow(c, b);

        var result = PlantUmlExporter.Export(document);

        Assert.Contains(":A;", result.Text);
        Assert.Contains(result.Warnings, w => w.StartsWith("The flow from \"C\" to \"B\" was not exported"));
    }

    [Fact]
    public void WhatPlantUmlHasNoPlaceFor_IsReported()
    {
        var document = DiagramDocument.Create("Styled", DiagramType.Class);
        var model = (ClassDiagramModel)document.Model;
        var cls = new UmlClass { Name = "A", TaggedValues = { new TaggedValue { Name = "table", Value = "a" } } };
        model.Classes.Add(cls);
        model.TextBoxes.Add(new TextBoxElement { Text = "free text" });
        document.Layout.Add(new ElementLayout { ElementId = cls.Id, Style = new ShapeStyle { Fill = "#FF0000" } });

        var result = PlantUmlExporter.Export(document);

        Assert.Equal(
            [
                "1 tagged value was not exported: PlantUML has no place for it.",
                "1 text box was not exported: PlantUML has no free text.",
                "Fonts, colours and line styles were not exported.",
            ],
            result.Warnings);
    }
}
