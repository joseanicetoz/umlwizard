using UmlWizard.Core.Model;
using UmlWizard.PlantUml;

namespace UmlWizard.Ai.Tests;

/// <summary>Answers from a script, and keeps what it was asked.</summary>
internal sealed class ScriptedProvider(params string[] replies) : IAiProvider
{
    private int _next;

    public List<(string Instructions, string Message)> Requests { get; } = [];

    public string DisplayName => "Scripted";

    public Task<string> CompleteAsync(string instructions, string message, CancellationToken cancellationToken = default)
    {
        Requests.Add((instructions, message));
        return Task.FromResult(replies[Math.Min(_next++, replies.Length - 1)]);
    }
}

public sealed class DiagramGeneratorTests
{
    private const string OrderLifecycle = """
        @startuml
        title Order Lifecycle
        [*] --> Created
        Created --> Paid : pay / capture()
        Paid --> Shipped : ship
        Shipped --> [*]
        @enduml
        """;

    [Fact]
    public async Task ADescription_BecomesADiagram_LaidOut_EvenWhenTheModelAddsProseAndFences()
    {
        var provider = new ScriptedProvider($"Here is the diagram:\n\n```plantuml\n{OrderLifecycle}\n```\n\nIt shows the states.");
        var result = await new AiDiagramGenerator(provider).GenerateAsync("The lifecycle of an order", DiagramType.StateMachine);

        Assert.Equal(DiagramType.StateMachine, result.Document.DiagramType);
        Assert.Equal("Order Lifecycle", result.Document.Name);
        Assert.Empty(result.Warnings);
        Assert.StartsWith("@startuml", result.PlantUml);
        Assert.EndsWith("@enduml", result.PlantUml);
        var model = (GraphDiagramModel)result.Document.Model;
        Assert.Equal(["Created", "Paid", "Shipped"], model.Nodes.Where(n => n.Kind == GraphNodeKind.State).Select(n => n.Name));
        Assert.All(model.Nodes, n => Assert.NotNull(result.Document.Layout.Find(n.Id)));

        // The request names the type and carries the description; the instructions only that type's PlantUML.
        var (instructions, message) = Assert.Single(provider.Requests);
        Assert.Contains("UML state machine diagram", message);
        Assert.Contains("The lifecycle of an order", message);
        Assert.Contains("[*] --> Created", instructions);
        Assert.DoesNotContain("abstract class Shape", instructions);
    }

    [Fact]
    public async Task WithoutAType_TheInstructionsCoverEveryDiagramType()
    {
        var provider = new ScriptedProvider(OrderLifecycle);
        await new AiDiagramGenerator(provider).GenerateAsync("anything", null);

        var (instructions, message) = provider.Requests[0];
        Assert.Contains("fits this best", message);
        Assert.All(Enum.GetValues<DiagramType>(), _ => { });
        foreach (var name in new[] { "class diagram", "sequence diagram", "state machine diagram", "activity diagram", "use case diagram", "component diagram", "deployment diagram" })
        {
            Assert.Contains(name, instructions);
        }
    }

    [Fact]
    public async Task AnAnswerThatIsNotADiagram_IsAskedForOnceMore_WithWhatWasWrong()
    {
        var provider = new ScriptedProvider("I would draw three states.", OrderLifecycle);
        var result = await new AiDiagramGenerator(provider).GenerateAsync("order lifecycle", DiagramType.StateMachine);

        Assert.Equal("Order Lifecycle", result.Document.Name);
        Assert.Equal(2, provider.Requests.Count);
        Assert.Contains("could not be read as a diagram", provider.Requests[1].Message);
        Assert.Contains("I would draw three states.", provider.Requests[1].Message);
    }

    [Fact]
    public async Task TwoUnreadableAnswers_GiveUp_WithAMessage()
    {
        var provider = new ScriptedProvider("No.", "@startuml\n@enduml");
        var error = await Assert.ThrowsAsync<AiException>(() => new AiDiagramGenerator(provider).GenerateAsync("x", null));

        Assert.Contains("Scripted did not answer with a diagram", error.Message);
        Assert.Equal(2, provider.Requests.Count);
    }

    public static TheoryData<DiagramType, string> InstructedSyntax => new()
    {
        {
            DiagramType.Class, """
            @startuml
            title Shop
            abstract class Shape
            interface Payable
            enum Status {
              New
              Paid
            }
            class Order <<entity>> {
              -total : decimal
              {static} +count : int
              +pay(amount : decimal) : bool
              +getOrder(id : string) : Order <<GET /orders/{id}>>
            }
            class Customer
            class Line
            class Square
            Square --|> Shape
            Order ..|> Payable
            Customer "1" --> "0..*" Order : places
            Order *-- Line
            Order o-- Status
            Order ..> Customer
            Line -- Square
            @enduml
            """
        },
        {
            DiagramType.Sequence, """
            @startuml
            title Checkout
            actor Customer
            participant "shop : ShopController" as shop
            participant "orders : OrderService" as orders
            Customer -> shop : checkout()
            shop ->> orders : notify()
            create orders
            shop -> orders : new()
            orders --> shop : order
            @enduml
            """
        },
        {
            DiagramType.StateMachine, """
            @startuml
            title States
            state Created
            state Check <<choice>>
            [*] --> Created
            Created --> Check : pay [valid] / capture()
            Check --> Created
            Created --> [*]
            note right of Created : A note
            @enduml
            """
        },
        {
            DiagramType.Activity, """
            @startuml
            title Checkout Process
            start
            :Review cart;
            if () then (logged in)
              :Pay;
            else (guest)
              :Log in;
            endif
            fork
              :Charge;
            fork again
              :Reserve stock;
            end fork
            while (more items?)
              :Pack item;
            endwhile
            repeat
              :Retry;
            repeat while (failed?)
            stop
            @enduml
            """
        },
        {
            DiagramType.UseCase, """
            @startuml
            title Shop Use Cases
            actor Customer
            actor Member
            rectangle "Online Shop" {
              usecase "Place order" as UC1
              usecase "Log in" as UC2
              usecase "Apply coupon" as UC3
            }
            Customer -- UC1
            UC1 ..> UC2 : <<include>>
            UC3 ..> UC1 : <<extend>>
            Member --|> Customer
            @enduml
            """
        },
        {
            DiagramType.Component, """
            @startuml
            title Components
            component "Order Service" as orders
            component "Payment Service" as payments
            interface IOrders
            orders ..> payments
            orders ..|> IOrders
            payments -- orders
            @enduml
            """
        },
        {
            DiagramType.Deployment, """
            @startuml
            title Deployment
            node "Web Server" <<device>> {
              node Kestrel <<executionEnvironment>> {
                artifact "shop-web.dll" as web
              }
            }
            node "Database" as db
            artifact "orders.dll" as orders
            component "Order Service" as service
            node Client <<device>>
            Client -- db : HTTPS
            orders ..> db : <<deploy>>
            orders ..> service : <<manifest>>
            web ..> service
            @enduml
            """
        },
    };

    /// <summary>
    /// What the instructions tell the model to write is what the importer reads: an example of each diagram type, in that
    /// syntax, imports without losing anything. If the instructions and the importer drift apart, this fails.
    /// </summary>
    [Theory]
    [MemberData(nameof(InstructedSyntax))]
    public void TheInstructedSyntax_IsReadWithoutLosingAnything(DiagramType type, string plantUml)
    {
        var instructions = AiDiagramGenerator.Instructions(type);
        Assert.Contains("@startuml", instructions);

        var result = AiDiagramGenerator.Read(plantUml);
        Assert.NotNull(result);
        Assert.Equal(type, result.Document.DiagramType);
        Assert.True(result.Warnings.Count == 0, string.Join("\n", result.Warnings));
    }

    [Fact]
    public void TheFirstBlock_IsTaken_WithoutFences()
    {
        Assert.Null(AiDiagramGenerator.ExtractPlantUml("no diagram here"));
        Assert.Equal("@startuml\nA -> B\n@enduml", AiDiagramGenerator.ExtractPlantUml("```\n@startuml\nA -> B\n@enduml\n```\n@startuml\nC\n@enduml"));
        Assert.NotNull(PlantUmlImporter.Import(OrderLifecycle, "x").Documents.SingleOrDefault());
    }
}
