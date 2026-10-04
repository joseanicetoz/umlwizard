using UmlWizard.Core.Model;

namespace UmlWizard.PlantUml.Tests;

/// <summary>PlantUML as people write it, in the forms the PlantUML documentation shows, read into diagrams.</summary>
public sealed class ImportTests
{
    private static (DiagramDocument Document, IReadOnlyList<string> Warnings) Import(string text)
    {
        var result = PlantUmlImporter.Import(text, "Imported");
        return (Assert.Single(result.Documents), result.Warnings);
    }

    [Fact]
    public void Class_JavaAndUmlMembers_HeritageInTheDeclaration_PackagesAndNotes()
    {
        var (document, warnings) = Import("""
            @startuml
            ' A shop
            skinparam classAttributeIconSize 0
            hide empty members
            package shop.domain {
              abstract class Entity {
                # Long id
                {abstract} + boolean validate()
              }
              class Order extends Entity implements Payable {
                - String number
                - List<OrderLine> lines
                + {static} int count = 0
                --
                + BigDecimal total()
                + void addLine(OrderLine line, int quantity)
              }
            }
            interface Payable {
              pay(amount : decimal) : bool
            }
            enum OrderStatus {
              NEW
              PAID, SHIPPED
            }
            class "Order Line" as OrderLine <<value object>> #lightblue
            OrderLine : quantity : int
            Order "1" *-- "1..*" OrderLine : lines >
            Customer "1" -- "0..*" Order : places
            note on link : the orders a customer placed
            note "Totals include tax" as N1
            N1 .. Order
            note left of Customer : a person
            @enduml
            """);

        var model = (ClassDiagramModel)document.Model;
        var order = model.Classes.Single(c => c.Name == "Order");
        Assert.Equal(["number: String", "lines: List<OrderLine>", "count: int = 0"],
            order.Attributes.Select(a => $"{a.Name}: {a.Type}" + (a.DefaultValue is null ? "" : $" = {a.DefaultValue}")));
        Assert.Equal([UmlVisibility.Private, UmlVisibility.Private, UmlVisibility.Public], order.Attributes.Select(a => a.Visibility));
        Assert.True(order.Attributes[2].IsStatic);
        Assert.Equal(["total(): BigDecimal", "addLine(line: OrderLine, quantity: int): void"],
            order.Operations.Select(o => $"{o.Name}({string.Join(", ", o.Parameters.Select(p => $"{p.Name}: {p.Type}"))}): {o.ReturnType}"));
        Assert.Equal("shop.domain", order.TaggedValues.Get("namespace"));

        var entity = model.Classes.Single(c => c.Name == "Entity");
        Assert.Equal(ClassifierKind.AbstractClass, entity.Kind);
        Assert.Equal(("id", "Long", UmlVisibility.Protected), (entity.Attributes[0].Name, entity.Attributes[0].Type, entity.Attributes[0].Visibility));
        Assert.True(entity.Operations.Single().IsAbstract);
        Assert.Equal(ClassifierKind.Interface, model.Classes.Single(c => c.Name == "Payable").Kind);
        Assert.Equal(["NEW", "PAID", "SHIPPED"], model.Classes.Single(c => c.Name == "OrderStatus").Attributes.Select(a => a.Name));

        var line = model.Classes.Single(c => c.Name == "Order Line");
        Assert.Equal("value object", line.Stereotype);
        Assert.Equal("quantity", Assert.Single(line.Attributes).Name);

        string Name(string id) => model.FindClass(id)!.Name;
        Assert.Contains(model.Relationships, r => r.Kind == RelationshipKind.Inheritance && Name(r.SourceId) == "Order" && Name(r.TargetId) == "Entity");
        Assert.Contains(model.Relationships, r => r.Kind == RelationshipKind.Realization && Name(r.SourceId) == "Order" && Name(r.TargetId) == "Payable");
        var composition = model.Relationships.Single(r => r.Kind == RelationshipKind.Composition);
        Assert.Equal(("Order", "Order Line", "1", "1..*", "lines"), (Name(composition.SourceId), Name(composition.TargetId), composition.SourceMultiplicity, composition.TargetMultiplicity, composition.Label));
        var places = model.Relationships.Single(r => r.Label == "places");
        Assert.Equal(RelationshipKind.Association, places.Kind);

        Assert.Equal(3, model.Notes.Count);
        Assert.Equal(places.Id, model.NoteAttachments.Single(a => model.Notes.Single(n => n.Text.StartsWith("the orders")).Id == a.NoteId).TargetId);
        Assert.Equal(order.Id, model.NoteAttachments.Single(a => model.Notes.Single(n => n.Text == "Totals include tax").Id == a.NoteId).TargetId);
        Assert.Empty(warnings);
    }

    [Fact]
    public void Sequence_ParticipantsMessagesReturnsCreationAndNotes()
    {
        var (document, warnings) = Import("""
            @startuml
            autonumber
            actor Customer
            participant "shop : ShopController" as shop
            database DB
            Customer -> shop ++ : checkout(cart)
            shop -> Orders : placeOrder(cart)
            activate Orders
            alt in stock
              Orders ->> DB : save(order)
            else out of stock
              Orders -> Orders : backorder()
            end
            return order
            shop --> Customer : confirmation
            create Receipt
            shop -> Receipt : new
            note right : printed
            note over Customer, shop : paid
            @enduml
            """);

        var model = (SequenceDiagramModel)document.Model;
        Assert.Equal(["Customer", "shop : ShopController", "DB", "Orders", "Receipt"], model.Lifelines.Select(l => l.Type is null ? l.Name : $"{l.Name} : {l.Type}"));
        Assert.Equal(LifelineKind.Actor, model.Lifelines[0].Kind);
        string Name(string id) => model.FindLifeline(id)!.Name;
        Assert.Equal(
            [
                "Synchronous Customer -> shop : checkout(cart)",
                "Synchronous shop -> Orders : placeOrder(cart)",
                "Asynchronous Orders -> DB : save(order)",
                "Synchronous Orders -> Orders : backorder()",
                "Reply Orders -> shop : order",
                "Reply shop -> Customer : confirmation",
                "Create shop -> Receipt : new",
            ],
            model.Messages.OrderBy(m => document.Layout.Find(m.Id)!.Y).Select(m => $"{m.Kind} {Name(m.SourceId)} -> {Name(m.TargetId)} : {m.Text}"));
        Assert.Equal(model.Messages.Last().Id, model.NoteAttachments.Single(a => a.NoteId == model.Notes.Single(n => n.Text == "printed").Id).TargetId);
        Assert.Equal(2, model.NoteAttachments.Count(a => a.NoteId == model.Notes.Single(n => n.Text == "paid").Id));
        Assert.Equal(
            [
                "1 combined fragment (alt, loop, opt, …) was left out; its messages were kept.",
                "1 participant drawn as a database in PlantUML is shown as an object.",
            ],
            warnings.Order());
    }

    [Fact]
    public void State_StatesChoicesStartAndEnd_CompositeStatesAreFlattened()
    {
        var (document, warnings) = Import("""
            @startuml
            [*] --> Created
            state "Order Placed" as Placed
            state check <<choice>>
            Created --> Placed : place
            Placed --> check
            check --> Paid : [paid]
            check --> Cancelled : [expired]
            Created --> Created : edit
            Paid : entry / notify()
            state Paid {
              [*] --> Packing
              Packing --> Shipped
            }
            Shipped -right-> [*]
            Cancelled --> [*]
            note left of Cancelled
              refunded
              within a week
            end note
            @enduml
            """);

        var model = (GraphDiagramModel)document.Model;
        Assert.Equal(["Cancelled", "Created", "Order Placed", "Packing", "Paid", "Shipped"], model.Nodes.Where(n => n.Kind == GraphNodeKind.State).Select(n => n.Name!).Order());
        Assert.Single(model.Nodes, n => n.Kind == GraphNodeKind.Choice);
        Assert.Equal(2, model.Nodes.Count(n => n.Kind == GraphNodeKind.InitialState));
        Assert.Single(model.Nodes, n => n.Kind == GraphNodeKind.FinalState);
        string Name(string id) => model.FindNode(id) is { } n ? n.Name ?? n.Kind.ToString() : "?";
        Assert.Contains(model.Edges, e => Name(e.SourceId) == "Created" && Name(e.TargetId) == "Order Placed" && e.Label == "place");
        Assert.Contains(model.Edges, e => Name(e.SourceId) == "Choice" && Name(e.TargetId) == "Paid" && e.Label == "[paid]");
        Assert.Contains(model.Edges, e => e.SourceId == e.TargetId && e.Label == "edit");
        Assert.Equal("refunded\nwithin a week", model.Notes.Single().Text);
        Assert.Equal(
            [
                "1 composite state was flattened: its states are shown beside it.",
                "1 line of a state's description (State : text) was left out.",
            ],
            warnings.Order());
    }

    [Fact]
    public void Activity_StructuredFlowsBecomeAGraph()
    {
        var (document, warnings) = Import("""
            @startuml
            |Customer|
            start
            :Review cart;
            if (logged in?) then (no)
              :Log in;
            endif
            while (items left?) is (yes)
              :Pick item;
            endwhile (no)
            repeat
              :Enter card;
              backward :Show error;
            repeat while (valid?) is (no) not (yes)
            switch (shipping)
            case (standard)
              :Ship by post;
            case (express)
              :Ship by courier;
            endswitch
            fork
              :Charge;
            fork again
              :Reserve;
            end fork
            :Send confirmation
            with details;
            note right: by e-mail
            stop
            @enduml
            """);

        var model = (GraphDiagramModel)document.Model;
        Assert.Equal(
            ["Charge", "Enter card", "Log in", "Pick item", "Reserve", "Review cart", "Send confirmation\nwith details", "Ship by courier", "Ship by post", "Show error"],
            model.Nodes.Where(n => n.Kind == GraphNodeKind.Action).Select(n => n.Name!).Order());
        Assert.Single(model.Nodes, n => n.Kind == GraphNodeKind.InitialNode);
        Assert.Single(model.Nodes, n => n.Kind == GraphNodeKind.ActivityFinal);
        Assert.Equal(2, model.Nodes.Count(n => n.Kind == GraphNodeKind.ForkJoin));
        string Name(string id) => model.FindNode(id) is { } n ? n.Name ?? n.Kind.ToString() : "?";
        Assert.Contains(model.Edges, e => Name(e.SourceId) == "Decision" && Name(e.TargetId) == "Log in" && e.Label == "[no]");
        Assert.Contains(model.Edges, e => Name(e.SourceId) == "Pick item" && Name(e.TargetId) == "Decision");
        Assert.Contains(model.Edges, e => Name(e.SourceId) == "Show error" && Name(e.TargetId) == "Enter card");
        Assert.Contains(model.Edges, e => Name(e.SourceId) == "Decision" && Name(e.TargetId) == "Ship by courier" && e.Label == "[express]");
        Assert.Contains(model.Edges, e => Name(e.SourceId) == "ForkJoin" && Name(e.TargetId) == "Charge");
        Assert.Equal("by e-mail", model.Notes.Single().Text);
        Assert.Contains("1 swimlane was left out.", warnings);
    }

    [Fact]
    public void Activity_TheOlderSyntax()
    {
        var (document, warnings) = Import("""
            @startuml
            (*) --> "Review cart"
            "Review cart" -->[ok] ===B1===
            ===B1=== --> "Charge"
            ===B1=== --> "Reserve"
            "Charge" --> (*)
            "Reserve" --> (*)
            @enduml
            """);

        var model = (GraphDiagramModel)document.Model;
        Assert.Equal(["Charge", "Reserve", "Review cart"], model.Nodes.Where(n => n.Kind == GraphNodeKind.Action).Select(n => n.Name!).Order());
        Assert.Single(model.Nodes, n => n.Kind == GraphNodeKind.ForkJoin);
        Assert.Equal(6, model.Edges.Count);
        Assert.Contains(model.Edges, e => e.Label == "[ok]");
        Assert.Empty(warnings);
    }

    [Fact]
    public void UseCase_ActorsUseCasesSystemsAndRelations()
    {
        var (document, warnings) = Import("""
            @startuml
            left to right direction
            :Customer: as C
            actor "Payment Provider" as PP
            actor Member
            package "Online Shop" {
              (Place order) as PO
              usecase "Apply coupon" as AC
              usecase Pay
            }
            Member --|> C
            C --> PO
            PO .> Pay : <<include>>
            AC ..> PO : <<extends>>
            Pay -- PP
            @enduml
            """);

        var model = (GraphDiagramModel)document.Model;
        Assert.Equal(DiagramType.UseCase, document.DiagramType);
        Assert.Equal(["Customer", "Member", "Payment Provider"], model.Nodes.Where(n => n.Kind == GraphNodeKind.Actor).Select(n => n.Name!).Order());
        var system = Assert.Single(model.Nodes, n => n.Kind == GraphNodeKind.SystemBoundary);
        Assert.Equal("Online Shop", system.Name);
        string Name(string id) => model.FindNode(id)!.Name!;
        Assert.Equal(
            ["Association Customer -> Place order", "Association Pay -> Payment Provider", "Extend Apply coupon -> Place order", "Generalization Member -> Customer", "Include Place order -> Pay"],
            model.Edges.Select(e => $"{e.Kind} {Name(e.SourceId)} -> {Name(e.TargetId)}").Order());

        // The use cases are laid out inside the system.
        var box = document.Layout.Find(system.Id)!;
        Assert.All(model.Nodes.Where(n => n.Kind == GraphNodeKind.UseCase).Select(n => document.Layout.Find(n.Id)!),
            l => Assert.True(l.X >= box.X && l.Y >= box.Y && l.X + l.Width <= box.X + box.Width && l.Y + l.Height <= box.Y + box.Height));
        Assert.Equal("1 package is shown as a system boundary.", Assert.Single(warnings));
    }

    [Fact]
    public void Component_ComponentsInterfacesAndDependencies()
    {
        var (document, warnings) = Import("""
            @startuml
            [Web Frontend] as Web
            component "Order Service" as Orders
            () "IOrders" as IO
            interface IPayments
            Web ..> IO : HTTPS
            Orders ..|> IO
            IPayments <|.. [Payment Service]
            Orders ..> IPayments : uses
            @enduml
            """);

        var model = (GraphDiagramModel)document.Model;
        Assert.Equal(DiagramType.Component, document.DiagramType);
        string Name(string id) => model.FindNode(id)!.Name!;
        Assert.Equal(["Order Service", "Payment Service", "Web Frontend"], model.Nodes.Where(n => n.Kind == GraphNodeKind.Component).Select(n => n.Name!).Order());
        Assert.Equal(["IOrders", "IPayments"], model.Nodes.Where(n => n.Kind == GraphNodeKind.Interface).Select(n => n.Name!).Order());
        Assert.Equal(
            ["Dependency Order Service -> IPayments : uses", "Dependency Web Frontend -> IOrders : HTTPS", "Realization Order Service -> IOrders : ", "Realization Payment Service -> IPayments : "],
            model.Edges.Select(e => $"{e.Kind} {Name(e.SourceId)} -> {Name(e.TargetId)} : {e.Label}").Order());
        Assert.Empty(warnings);
    }

    [Fact]
    public void Deployment_NestedNodesArtifactsAndPaths()
    {
        var (document, warnings) = Import("""
            @startuml
            node "Web Server" <<device>> as WS {
              node Kestrel <<executionEnvironment>> {
                artifact "shop-web.dll" as web
              }
            }
            database "Orders DB" as DB
            cloud Internet
            WS -- DB : TCP 5432
            web ..> WS : <<deploy>>
            @enduml
            """);

        var model = (GraphDiagramModel)document.Model;
        Assert.Equal(DiagramType.Deployment, document.DiagramType);
        Assert.Equal(GraphNodeKind.Device, model.Nodes.Single(n => n.Name == "Web Server").Kind);
        Assert.Equal(GraphNodeKind.ExecutionEnvironment, model.Nodes.Single(n => n.Name == "Kestrel").Kind);
        Assert.Equal(GraphNodeKind.Node, model.Nodes.Single(n => n.Name == "Orders DB").Kind);
        string Name(string id) => model.FindNode(id)!.Name!;
        Assert.Equal(["CommunicationPath Web Server -> Orders DB : TCP 5432", "Deploy shop-web.dll -> Web Server : "],
            model.Edges.Select(e => $"{e.Kind} {Name(e.SourceId)} -> {Name(e.TargetId)} : {e.Label}").Order());

        // Nested as written: the artifact inside Kestrel, Kestrel inside the server.
        bool Inside(string inner, string outer)
        {
            var a = document.Layout.Find(model.Nodes.Single(n => n.Name == inner).Id)!;
            var b = document.Layout.Find(model.Nodes.Single(n => n.Name == outer).Id)!;
            return a.X >= b.X && a.Y >= b.Y && a.X + a.Width <= b.X + b.Width && a.Y + a.Height <= b.Y + b.Height;
        }

        Assert.True(Inside("shop-web.dll", "Kestrel"));
        Assert.True(Inside("Kestrel", "Web Server"));
        Assert.False(Inside("Orders DB", "Web Server"));
        Assert.Single(warnings);
    }

    [Fact]
    public void SeveralDiagramsInOneFile_AreImportedEach_NamedByTitleOrFile()
    {
        var result = PlantUmlImporter.Import("""
            @startuml
            title Orders
            class Order
            @enduml

            @startuml
            Alice -> Bob : hi
            @enduml
            """, "shop");

        Assert.Equal([("Orders", DiagramType.Class), ("shop 2", DiagramType.Sequence)], result.Documents.Select(d => (d.Name, d.DiagramType)));
    }

    [Fact]
    public void TextThatIsNoDiagram_IsRefused()
    {
        var error = Assert.Throws<InvalidDataException>(() => PlantUmlImporter.Import("@startuml\nlorem ipsum dolor\n@enduml", "x"));
        Assert.Contains("not recognised", error.Message);
    }

    [Fact]
    public void LinesThatAreNotUnderstood_AreReported()
    {
        var (_, warnings) = Import("@startuml\nclass A\nclass B\nA --> B\nthis is not plantuml\n@enduml");

        Assert.Equal("1 line was not understood and left out: \"this is not plantuml\".", Assert.Single(warnings));
    }
}
