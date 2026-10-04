using UmlWizard.Core.Editing;
using UmlWizard.Core.Model;
using UmlWizard.PlantUml;

namespace UmlWizard.Ai.Tests;

/// <summary>The whole-diagram mode, for models without tool calling: the revised PlantUML, taken in by name.</summary>
public sealed class WholeDiagramTests
{
    private static DiagramDocument Shop()
    {
        var editor = new DiagramEditor(DiagramDocument.Create("Shop", DiagramType.Class));
        var order = editor.AddClass(ClassifierKind.Class, 100, 100);
        order.Name = "Order";
        order.Attributes.Add(new UmlAttribute { Name = "total", Type = "decimal", TaggedValues = [new TaggedValue { Name = "column", Value = "TOTAL" }] });
        var customer = editor.AddClass(ClassifierKind.Class, 400, 100);
        customer.Name = "Customer";
        var logger = editor.AddClass(ClassifierKind.Class, 700, 100);
        logger.Name = "Logger";
        return editor.Document;
    }

    private static DiagramDocument Revision(string plantUml) => PlantUmlImporter.Import(plantUml, "Shop").Documents[0];

    [Fact]
    public void AClassDiagram_KeepsWhatIsUnchanged_WithItsPlaceAndTaggedValues_AndPlacesWhatIsNew()
    {
        var current = Shop();
        var model = (ClassDiagramModel)current.Model;
        var order = model.Classes.Single(c => c.Name == "Order");
        var (x, y) = (current.Layout.Find(order.Id)!.X, current.Layout.Find(order.Id)!.Y);

        var merged = DiagramMerge.Merge(current, Revision("""
            @startuml
            class Order {
              -total : decimal
              +pay() : bool
            }
            class Customer
            class Payment {
              -amount : decimal
            }
            Customer --> Order : places
            Order *-- Payment
            @enduml
            """));

        var result = (ClassDiagramModel)merged.Model;
        var kept = result.Classes.Single(c => c.Name == "Order");
        Assert.Equal(order.Id, kept.Id);
        Assert.Equal((x, y), (merged.Layout.Find(kept.Id)!.X, merged.Layout.Find(kept.Id)!.Y));
        Assert.Equal("TOTAL", kept.Attributes.Single(a => a.Name == "total").TaggedValues.Single().Value);
        Assert.Contains(kept.Operations, o => o.Name == "pay");
        Assert.DoesNotContain(result.Classes, c => c.Name == "Logger");
        var payment = result.Classes.Single(c => c.Name == "Payment");
        Assert.NotEqual((0d, 0d), (merged.Layout.Find(payment.Id)!.X, merged.Layout.Find(payment.Id)!.Y));
        Assert.Equal(2, result.Relationships.Count);
        Assert.All(result.Relationships, r => Assert.NotNull(result.FindClass(r.SourceId)));

        Assert.Equal(
            ["+ Order: +pay(): bool", "+ class Payment", "    + -amount: decimal", "− class Logger", "+ Customer → Order (directed association)", "+ Order → Payment (composition)"],
            DiagramChanges.Describe(current, merged));

        // The diagram itself is not touched.
        Assert.Contains(model.Classes, c => c.Name == "Logger");
    }

    [Fact]
    public void AStateMachine_KeepsItsStates_ByName()
    {
        var current = PlantUmlImporter.Import("""
            @startuml
            [*] --> Created
            Created --> Paid : pay
            @enduml
            """, "Orders").Documents[0];
        var paid = ((GraphDiagramModel)current.Model).Nodes.Single(n => n.Name == "Paid");

        var merged = DiagramMerge.Merge(current, Revision("""
            @startuml
            [*] --> Created
            Created --> Paid : pay [valid]
            Paid --> Shipped : ship
            Shipped --> [*]
            @enduml
            """));

        var model = (GraphDiagramModel)merged.Model;
        Assert.Equal(paid.Id, model.Nodes.Single(n => n.Name == "Paid").Id);
        Assert.Equal(
            ["+ state Shipped", "+ final state final_state#1", "~ Created → Paid (transition) : pay [valid]", "+ Paid → Shipped (transition) : ship", "+ Shipped → final_state#1 (transition)"],
            DiagramChanges.Describe(current, merged));
    }

    [Fact]
    public void ASequenceDiagram_KeepsItsMessages_InTheNewOrder()
    {
        var current = PlantUmlImporter.Import("""
            @startuml
            actor customer
            participant shop
            customer -> shop : checkout()
            shop --> customer : receipt
            @enduml
            """, "Checkout").Documents[0];
        var model = (SequenceDiagramModel)current.Model;
        var checkout = model.Messages.Single(m => m.Text == "checkout()");

        var merged = DiagramMerge.Merge(current, Revision("""
            @startuml
            actor customer
            participant shop
            participant payments
            customer -> shop : checkout()
            shop -> payments : pay(total)
            shop --> customer : receipt
            @enduml
            """));

        var result = (SequenceDiagramModel)merged.Model;
        var inOrder = result.Messages.OrderBy(m => merged.Layout.Find(m.Id)!.Y).Select(m => m.Text).ToList();
        Assert.Equal(["checkout()", "pay(total)", "receipt"], inOrder);
        Assert.Equal(checkout.Id, result.Messages.Single(m => m.Text == "checkout()").Id);
        Assert.Equal(current.Layout.Find(checkout.Id)!.Y, merged.Layout.Find(checkout.Id)!.Y);
    }

    [Fact]
    public async Task InWholeDiagramMode_TheSessionSendsNoTools_AndTakesTheRevisedDiagramFromTheAnswer()
    {
        var chat = new ScriptedChat(new AiTurn("""
            I added a Payment class.

            ```plantuml
            @startuml
            class Order {
              -total : decimal
            }
            class Customer
            class Logger
            class Payment
            Order *-- Payment
            @enduml
            ```
            """, []));
        var session = new AiChatSession { ChangeMode = AiChangeMode.WholeDiagram };
        var diagram = Shop();

        var answer = await session.SendAsync(chat, diagram, "Add a payment");

        Assert.Equal("I added a Payment class.", answer);
        Assert.Empty(chat.Requests[0].Tools);
        Assert.Contains("whole revised", chat.Requests[0].Instructions);
        Assert.Contains("class Order", chat.Requests[0].Instructions);
        Assert.Equal(["+ class Payment", "+ Order → Payment (composition)"], session.DescribeChanges(diagram));
    }

    [Fact]
    public async Task InWholeDiagramMode_AnAnswerWithoutADiagram_ChangesNothing_AndAnUnreadableOneSaysSo()
    {
        var session = new AiChatSession { ChangeMode = AiChangeMode.WholeDiagram };
        Assert.Equal("It has three classes.", await session.SendAsync(new ScriptedChat(new AiTurn("It has three classes.", [])), Shop(), "What is it?"));
        Assert.False(session.HasChanges);

        var answer = await session.SendAsync(new ScriptedChat(new AiTurn("Here:\n@startuml\n@enduml", [])), Shop(), "Change it");
        Assert.Contains("could not be read", answer);
        Assert.False(session.HasChanges);
    }

    [Theory]
    [InlineData("Sure:\n```plantuml\n@startuml\nclass A", "Sure:")]
    [InlineData("Done.\n\n@startuml\nclass A\n@enduml\n\nApply it.", "Done.\n\nApply it.")]
    [InlineData("No diagram here.", "No diagram here.")]
    public void WhileAnAnswerComes_TheDiagramInItIsNotShown(string answer, string shown) =>
        Assert.Equal(shown, AiChatSession.Visible(answer));
}
