using System.Text;
using System.Text.RegularExpressions;
using UmlWizard.Core.Model;
using UmlWizard.PlantUml;

namespace UmlWizard.Ai;

/// <summary>A diagram made from a description, with what the import of the model's PlantUML could not keep.</summary>
/// <param name="Document">The diagram, laid out.</param>
/// <param name="Warnings">What the PlantUML import left out or changed.</param>
/// <param name="PlantUml">The PlantUML the model wrote.</param>
public sealed record AiDiagramResult(DiagramDocument Document, IReadOnlyList<string> Warnings, string PlantUml);

/// <summary>
/// Turns a description into a diagram (Diagram → Create with AI): asks the model for PlantUML in the subset UML Wizard
/// reads, and imports it with <see cref="PlantUmlImporter"/>, which checks and lays it out. When nothing could be read,
/// asks once more with what went wrong. The app never draws the model's output as is.
/// </summary>
public sealed partial class AiDiagramGenerator(IAiProvider provider)
{
    /// <summary>The diagram's name when the model gives it no title.</summary>
    public const string FallbackName = "AI diagram";

    /// <summary>Makes a diagram of <paramref name="type"/> (or of the type the model finds fitting) from a description.</summary>
    /// <exception cref="AiException">The service failed, or its answer could not be read as a diagram.</exception>
    public async Task<AiDiagramResult> GenerateAsync(string description, DiagramType? type, CancellationToken cancellationToken = default)
    {
        var instructions = Instructions(type);
        var reply = await provider.CompleteAsync(instructions, Request(description, type), cancellationToken);
        if (Read(reply) is { } result)
        {
            return result;
        }

        // One more try, saying what was wrong: models usually fix their output when told.
        var retry = await provider.CompleteAsync(instructions, Repair(description, type, reply), cancellationToken);
        return Read(retry) ?? throw new AiException(
            $"{provider.DisplayName} did not answer with a diagram UML Wizard can read. Try describing it differently, or choose the diagram type.");
    }

    /// <summary>The diagram in a reply, or <c>null</c> when there is none that can be read.</summary>
    internal static AiDiagramResult? Read(string reply)
    {
        if (ExtractPlantUml(reply) is not { } plantUml)
        {
            return null;
        }

        PlantUmlImportResult import;
        try
        {
            import = PlantUmlImporter.Import(plantUml, FallbackName);
        }
        catch (InvalidDataException)
        {
            // An empty block: nothing to read.
            return null;
        }

        return import.Documents.Count == 0 ? null : new AiDiagramResult(import.Documents[0], import.Warnings, plantUml);
    }

    /// <summary>The first <c>@startuml</c> … <c>@enduml</c> block of a reply, without code fences; <c>null</c> when there is none.</summary>
    internal static string? ExtractPlantUml(string reply)
    {
        var match = PlantUmlBlock().Match(reply);
        return match.Success ? match.Value.Trim() : null;
    }

    [GeneratedRegex(@"@startuml\b.*?@enduml\b", RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private static partial Regex PlantUmlBlock();

    private static string Request(string description, DiagramType? type) =>
        type is { } t
            ? $"Draw a {Names[t]} for this:\n\n{description.Trim()}"
            : $"Draw the kind of UML diagram that fits this best:\n\n{description.Trim()}";

    private static string Repair(string description, DiagramType? type, string previous) =>
        $"{Request(description, type)}\n\nYour previous answer could not be read as a diagram:\n\n{previous}\n\n" +
        "Answer again with exactly one @startuml … @enduml block that uses only the PlantUML described in your instructions.";

    private static readonly Dictionary<DiagramType, string> Names = new()
    {
        [DiagramType.Class] = "UML class diagram",
        [DiagramType.Sequence] = "UML sequence diagram",
        [DiagramType.StateMachine] = "UML state machine diagram",
        [DiagramType.Activity] = "UML activity diagram",
        [DiagramType.UseCase] = "UML use case diagram",
        [DiagramType.Component] = "UML component diagram",
        [DiagramType.Deployment] = "UML deployment diagram",
    };

    /// <summary>The system prompt: the task, the output format, and the PlantUML each diagram type may use.</summary>
    internal static string Instructions(DiagramType? type)
    {
        var text = new StringBuilder("""
            You draw UML diagrams for UML Wizard, a UML designer. You answer with PlantUML that UML Wizard imports and lays
            out itself, so do not position or style anything.

            Rules:
            - Answer with exactly one block from @startuml to @enduml, and nothing else: no explanation, no code fences.
            - Start the block with a short title line, e.g. "title Order Lifecycle", which names the diagram.
            - Use only the PlantUML listed below for the kind of diagram; anything else is lost.
            - No skinparam, colours, layout hints (left to right, -[hidden]-, up/down arrows), !include, packages or notes on links.
            - Keep names short and in the language of the request; give classes their important attributes and operations only.
            - Prefer a clear diagram of 5 to 15 elements over an exhaustive one, unless asked otherwise.

            """);

        foreach (var (diagramType, syntax) in Syntax)
        {
            if (type is null || type == diagramType)
            {
                text.Append('\n').Append(Names[diagramType]).Append(":\n").Append(syntax).Append('\n');
            }
        }

        text.Append("""

            Every kind of diagram may have notes: note right of X : text (or left of, top of, bottom of).
            """);
        return text.ToString();
    }

    /// <summary>The PlantUML UML Wizard reads for a diagram type, as a list for the model.</summary>
    internal static string SyntaxOf(DiagramType type) => Syntax.First(s => s.Type == type).Syntax;

    private static readonly (DiagramType Type, string Syntax)[] Syntax =
    [
        (DiagramType.Class, """
            - class Order, abstract class Shape, interface Payable, enum Status { New, Paid }
            - members inside braces: -total : decimal, +pay(amount : decimal) : bool, {static} and {abstract} before a member
            - a stereotype after the name: class OrderService <<service>>
            - relationships: A --|> B (A inherits from B), A ..|> B (A realizes interface B), A --> B (directed
              association), A ..> B (dependency), A o-- B (A aggregates B), A *-- B (A is composed of B), A -- B
              (association); multiplicities in quotes and a label after a colon: Customer "1" --> "0..*" Order : places
            - a web API endpoint: +getOrder(id : string) : Order <<GET /orders/{id}>>
            """),
        (DiagramType.Sequence, """
            - actor Customer, participant "shop : ShopController" as shop
            - messages: a -> b : call() (synchronous), a ->> b : notify() (asynchronous), b --> a : result (reply),
              create b then a -> b : new() (creation)
            - no activation bars, alt, opt, loop or other fragments
            """),
        (DiagramType.StateMachine, """
            - state Paid, state Check <<choice>>, [*] --> Created (initial), Delivered --> [*] (final)
            - transitions with a label: Created --> Paid : pay [valid] / capture()
            - no composite states, history or fork/join
            """),
        (DiagramType.Activity, """
            - the newer activity syntax only: start, :Action;, stop
            - decisions with their guards as the branch labels and an empty condition (UML decisions have no text):
              if () then (logged in) … else (guest) … endif
            - fork … fork again … end fork, while (more items?) … endwhile, repeat … repeat while (again?)
            - no swimlanes, partitions or detach
            """),
        (DiagramType.UseCase, """
            - actor Customer, usecase "Place order" as UC1, rectangle "Online Shop" { usecase … } for the system
            - Customer -- UC1 (association), UC1 ..> UC2 : <<include>>, UC3 ..> UC1 : <<extend>>, Member --|> Customer
            """),
        (DiagramType.Component, """
            - component "Order Service" as orders, interface IOrders
            - orders ..> payments (dependency), orders ..|> IOrders (provides the interface), a -- b (association)
            """),
        (DiagramType.Deployment, """
            - node "Web Server" <<device>> { node Kestrel <<executionEnvironment>> { artifact "shop-web.dll" } }
            - artifact, component; nesting in braces means "deployed on"
            - a -- b : HTTPS (communication path with its protocol), artifact ..> node : <<deploy>>,
              artifact ..> component : <<manifest>>, a ..> b (dependency)
            """),
    ];
}
