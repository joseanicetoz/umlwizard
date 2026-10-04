using System.Net;
using System.Text.Json.Nodes;
using UmlWizard.Core.Editing;
using UmlWizard.Core.Model;
using UmlWizard.Core.Rendering;
using UmlWizard.Core.Serialization;

namespace UmlWizard.Ai.Tests;

/// <summary>A conversation partner that answers from a script of turns, and keeps what it was sent.</summary>
internal sealed class ScriptedChat(params AiTurn[] turns) : IAiProvider
{
    private readonly Queue<AiTurn> _turns = new(turns);

    public List<(string Instructions, List<AiMessage> Messages, IReadOnlyList<AiTool> Tools)> Requests { get; } = [];

    public string DisplayName => "Scripted";

    public Task<string> CompleteAsync(string instructions, string message, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public Task<AiTurn> ChatAsync(string instructions, IReadOnlyList<AiMessage> messages, IReadOnlyList<AiTool> tools, CancellationToken cancellationToken = default)
    {
        Requests.Add((instructions, [.. messages], tools));
        return Task.FromResult(_turns.Count > 0 ? _turns.Dequeue() : new AiTurn("Done.", []));
    }

    public static AiToolCall Call(string name, string arguments) =>
        new(Guid.NewGuid().ToString("N")[..8], name, (JsonObject)JsonNode.Parse(arguments)!);
}

public sealed class ChatTests
{
    private static DiagramDocument Shop()
    {
        var editor = new DiagramEditor(DiagramDocument.Create("Shop", DiagramType.Class));
        var order = editor.AddClass(ClassifierKind.Class, 100, 100);
        order.Name = "Order";
        var customer = editor.AddClass(ClassifierKind.Class, 400, 100);
        customer.Name = "Customer";
        editor.AddRelationship(RelationshipKind.Association, customer.Id, order.Id);
        return editor.Document;
    }

    private static AiToolResult Run(ClassDiagramTools tools, string name, string arguments) => tools.Run(ScriptedChat.Call(name, arguments));

    [Fact]
    public void TheTools_ChangeTheClassDiagram_ByName_InUmlNotation()
    {
        var document = Shop();
        var tools = new ClassDiagramTools(document);
        var model = (ClassDiagramModel)document.Model;

        Assert.False(Run(tools, "add_class", """{"name": "Payment", "stereotype": "«entity»"}""").IsError);
        Assert.False(Run(tools, "add_class", """{"name": "Status", "kind": "enumeration"}""").IsError);
        Assert.False(Run(tools, "add_class", """{"name": "Payable", "kind": "interface"}""").IsError);
        Assert.False(Run(tools, "add_attribute", """{"class": "Payment", "attribute": "-amount: decimal = 0"}""").IsError);
        Assert.False(Run(tools, "add_attribute", """{"class": "status", "attribute": "Paid"}""").IsError);
        Assert.False(Run(tools, "add_operation", """{"class": "Payment", "operation": "+capture(): bool"}""").IsError);
        Assert.False(Run(tools, "add_relationship", """{"source": "Order", "target": "Payment", "kind": "composition", "source_multiplicity": "1", "target_multiplicity": "0..*", "label": "pays with"}""").IsError);
        Assert.False(Run(tools, "add_relationship", """{"source": "Payment", "target": "Payable", "kind": "realization"}""").IsError);
        Assert.False(Run(tools, "add_note", """{"text": "Payments are captured on shipping.", "attach_to": "Payment"}""").IsError);
        Assert.False(Run(tools, "rename_class", """{"class": "Customer", "new_name": "Buyer"}""").IsError);
        Assert.False(Run(tools, "set_class", """{"class": "Order", "kind": "abstract_class"}""").IsError);

        var payment = model.Classes.Single(c => c.Name == "Payment");
        Assert.Equal("entity", payment.Stereotype);
        Assert.Equal(("amount", "decimal", "0", UmlVisibility.Private), (payment.Attributes[0].Name, payment.Attributes[0].Type, payment.Attributes[0].DefaultValue, payment.Attributes[0].Visibility));
        Assert.Equal("capture", payment.Operations[0].Name);
        Assert.Equal("Paid", model.Classes.Single(c => c.Name == "Status").Attributes.Single().Name);
        var composition = model.Relationships.Single(r => r.Kind == RelationshipKind.Composition);
        Assert.Equal(("1", "0..*", "pays with"), (composition.SourceMultiplicity, composition.TargetMultiplicity, composition.Label));
        Assert.Single(model.NoteAttachments, a => a.TargetId == payment.Id);
        Assert.Contains(model.Classes, c => c.Name == "Buyer");
        Assert.Equal(ClassifierKind.AbstractClass, model.Classes.Single(c => c.Name == "Order").Kind);
        Assert.True(tools.Changed);
        Assert.Equal(4, tools.Added.Count);

        // Removing: a member, a relationship, a class with what hangs on it.
        Assert.False(Run(tools, "remove_member", """{"class": "Payment", "name": "capture"}""").IsError);
        Assert.Empty(payment.Operations);
        Assert.False(Run(tools, "remove_relationship", """{"source": "Buyer", "target": "Order"}""").IsError);
        Assert.False(Run(tools, "delete_class", """{"class": "Payment"}""").IsError);
        Assert.DoesNotContain(model.Classes, c => c.Name == "Payment");
        Assert.DoesNotContain(model.Relationships, r => r.Kind == RelationshipKind.Composition);
        Assert.Empty(model.NoteAttachments);
        Assert.Null(document.Layout.Find(payment.Id));
    }

    [Theory]
    [InlineData("add_class", """{"name": "Order"}""", "already a class named Order")]
    [InlineData("add_attribute", """{"class": "Nope", "attribute": "x: int"}""", "no class named Nope. The classes are: Order, Customer")]
    [InlineData("add_attribute", """{"class": "Order", "attribute": ": int"}""", "not an attribute in UML notation")]
    [InlineData("add_relationship", """{"source": "Order", "target": "Customer", "kind": "realization"}""", "Only an interface can be realized")]
    [InlineData("add_relationship", """{"source": "Order", "target": "Order", "kind": "association"}""", "two different classes")]
    [InlineData("add_relationship", """{"source": "Customer", "target": "Order", "kind": "association"}""", "already has that relationship")]
    [InlineData("remove_member", """{"class": "Order", "name": "x"}""", "has no member named x")]
    [InlineData("add_class", """{}""", "argument name is missing")]
    [InlineData("draw_everything", """{}""", "no tool named draw_everything")]
    public void AToolThatCannotBeDone_ChangesNothing_AndSaysWhy(string tool, string arguments, string expected)
    {
        var document = Shop();
        var before = DiagramSerializer.Serialize(document);
        var tools = new ClassDiagramTools(document);

        var result = Run(tools, tool, arguments);

        Assert.True(result.IsError);
        Assert.Contains(expected, result.Content);
        Assert.False(tools.Changed);
        Assert.Equal(before, DiagramSerializer.Serialize(document));
    }

    [Fact]
    public async Task ASession_RunsTheToolsOnACopy_UntilTheModelAnswers_AndThePreviewSaysWhatChanged()
    {
        var diagram = Shop();
        var original = DiagramSerializer.Serialize(diagram);
        var chat = new ScriptedChat(
            new AiTurn("Adding a payment.", [ScriptedChat.Call("add_class", """{"name": "Payment"}"""), ScriptedChat.Call("add_class", """{"name": "Order"}""")]),
            new AiTurn(null, [ScriptedChat.Call("add_relationship", """{"source": "Order", "target": "Payment", "kind": "composition"}""")]),
            new AiTurn("I added Payment, composed by Order.", []));
        var session = new AiChatSession();

        var answer = await session.SendAsync(chat, diagram, "Orders need payments");

        Assert.Equal("I added Payment, composed by Order.", answer);
        Assert.Equal(original, DiagramSerializer.Serialize(diagram));
        Assert.True(session.HasChanges);
        Assert.Equal(["+ class Payment", "+ Order → Payment (composition)"], session.DescribeChanges(diagram));

        // The model saw the diagram, its tools and the results of its calls, including the error.
        Assert.Equal(3, chat.Requests.Count);
        Assert.Contains("class Order", chat.Requests[0].Instructions);
        Assert.Contains(chat.Requests[0].Tools, t => t.Name == "add_relationship");
        var results = Assert.IsType<AiToolResults>(chat.Requests[1].Messages[^1]);
        Assert.Equal([false, true], results.Results.Select(r => r.IsError));
        Assert.Equal(6, session.Messages.Count); // user, calls, results, call, results, answer

        // Payment is placed next to Order, overlapping nothing.
        var copy = session.WorkingCopy!;
        using var renderer = DiagramRenderer.Create(DiagramType.Class);
        renderer.UpdateLayout(copy);
        var bounds = ((ClassDiagramModel)copy.Model).Classes.Select(c => renderer.GetElementBounds(copy, c.Id)!.Value).ToList();
        for (var i = 0; i < bounds.Count; i++)
        {
            for (var j = i + 1; j < bounds.Count; j++)
            {
                Assert.False(bounds[i].IntersectsWith(bounds[j]));
            }
        }

        var order = renderer.GetElementBounds(copy, ((ClassDiagramModel)copy.Model).Classes.Single(c => c.Name == "Order").Id)!.Value;
        var payment = renderer.GetElementBounds(copy, ((ClassDiagramModel)copy.Model).Classes.Single(c => c.Name == "Payment").Id)!.Value;
        Assert.InRange(SkiaSharp.SKPoint.Distance(new(order.MidX, order.MidY), new(payment.MidX, payment.MidY)), 0, 500);
    }

    [Fact]
    public async Task AppliedOrDiscardedChanges_AreToldToTheModel_WithTheNextMessage()
    {
        var diagram = Shop();
        var session = new AiChatSession();
        var chat = new ScriptedChat(new AiTurn(null, [ScriptedChat.Call("add_class", """{"name": "Payment"}""")]), new AiTurn("Added.", []));
        await session.SendAsync(chat, diagram, "Add Payment");

        session.Discard();
        Assert.False(session.HasChanges);
        await session.SendAsync(chat, diagram, "Thanks");
        Assert.StartsWith("(The user discarded your changes", ((AiUserMessage)chat.Requests[^1].Messages[^1]).Text);
        Assert.DoesNotContain("class Payment", chat.Requests[^1].Instructions);

        var again = new ScriptedChat(new AiTurn(null, [ScriptedChat.Call("add_class", """{"name": "Payment"}""")]), new AiTurn("Added.", []));
        await session.SendAsync(again, diagram, "Add it after all");
        var applied = session.TakeChanges()!;
        Assert.Contains(((ClassDiagramModel)applied.Model).Classes, c => c.Name == "Payment");
        await session.SendAsync(again, applied, "Now what?");
        Assert.StartsWith("(The user applied your changes.)", ((AiUserMessage)again.Requests[^1].Messages[^1]).Text);
    }

    [Fact]
    public async Task AFailedRequest_LeavesTheConversationAndTheChangesAsTheyWere()
    {
        var diagram = Shop();
        var session = new AiChatSession();
        await session.SendAsync(new ScriptedChat(new AiTurn("Hello.", [])), diagram, "Hi");
        var failing = new FailingChat();

        await Assert.ThrowsAsync<AiException>(() => session.SendAsync(failing, diagram, "Add Payment"));

        Assert.Equal(2, session.Messages.Count);
        Assert.False(session.HasChanges);
    }

    private sealed class FailingChat : IAiProvider
    {
        private int _calls;

        public string DisplayName => "Failing";

        public Task<string> CompleteAsync(string instructions, string message, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<AiTurn> ChatAsync(string instructions, IReadOnlyList<AiMessage> messages, IReadOnlyList<AiTool> tools, CancellationToken cancellationToken = default) =>
            _calls++ == 0
                ? Task.FromResult(new AiTurn(null, [ScriptedChat.Call("add_class", """{"name": "Payment"}""")]))
                : throw new AiException("The service went away.");
    }

    [Fact]
    public async Task AModelThatKeepsCallingTools_IsStopped()
    {
        var turns = Enumerable.Range(0, 20).Select(i => new AiTurn(null, [ScriptedChat.Call("add_class", $$"""{"name": "C{{i}}"}""")])).ToArray();
        var session = new AiChatSession();

        var answer = await session.SendAsync(new ScriptedChat(turns), Shop(), "Go");

        Assert.Contains($"Stopped after {AiChatSession.MaxRounds} rounds", answer);
        Assert.IsType<AiAssistantMessage>(session.Messages[^1]);
        Assert.Equal(AiChatSession.MaxRounds, ((ClassDiagramModel)session.WorkingCopy!.Model).Classes.Count - 2);
    }

    [Fact]
    public async Task EveryDiagramType_HasTools_WithTheKindsOfItsType()
    {
        foreach (var type in Enum.GetValues<DiagramType>())
        {
            var diagram = DiagramDocument.Create("D", type);
            var chat = new ScriptedChat(new AiTurn("It is empty.", []));

            Assert.True(AiChatSession.CanEdit(type));
            Assert.Equal("It is empty.", await new AiChatSession().SendAsync(chat, diagram, "What is this?"));
            Assert.NotEmpty(chat.Requests[0].Tools);
            Assert.Contains("with your tools", chat.Requests[0].Instructions);
            Assert.All(chat.Requests[0].Tools, t => Assert.Equal("object", (string?)t.InputSchema["type"]));
        }

        var activity = new ScriptedChat(new AiTurn("Ok.", []));
        await new AiChatSession().SendAsync(activity, DiagramDocument.Create("A", DiagramType.Activity), "Hi");
        var kinds = activity.Requests[0].Tools.Single(t => t.Name == "add_node").InputSchema["properties"]!["kind"]!["enum"]!.AsArray().Select(k => (string?)k);
        Assert.Equal(["action", "initial_node", "activity_final", "decision", "fork_join"], kinds);
        Assert.DoesNotContain(activity.Requests[0].Tools, t => t.Name == "move_node");
    }

    [Fact]
    public async Task InAnEmptyDiagram_WhatTheAiAdds_IsArranged()
    {
        var chat = new ScriptedChat(
            new AiTurn(null,
            [
                ScriptedChat.Call("add_class", """{"name": "Animal", "kind": "abstract_class"}"""),
                ScriptedChat.Call("add_class", """{"name": "Dog"}"""),
                ScriptedChat.Call("add_relationship", """{"source": "Dog", "target": "Animal", "kind": "generalization"}"""),
            ]),
            new AiTurn("Done.", []));
        var session = new AiChatSession();
        await session.SendAsync(chat, DiagramDocument.Create("Pets", DiagramType.Class), "Animals");

        var copy = session.WorkingCopy!;
        var model = (ClassDiagramModel)copy.Model;
        double Top(string name) => copy.Layout.Find(model.Classes.Single(c => c.Name == name).Id)!.Y;
        Assert.True(Top("Animal") < Top("Dog"));
    }

    [Fact]
    public async Task Anthropic_SendsToolsAndToolResults_AndReadsToolCalls()
    {
        var handler = FakeHandler.Replying(HttpStatusCode.OK, """
            {"content": [{"type": "text", "text": "Adding it."},
                         {"type": "tool_use", "id": "toolu_1", "name": "add_class", "input": {"name": "Payment"}}],
             "stop_reason": "tool_use"}
            """);
        var provider = AiProviders.Create(new AiServiceSettings { Provider = AiProviderKind.Anthropic }, "key", new HttpClient(handler))!;
        AiMessage[] messages =
        [
            new AiUserMessage("Add Order"),
            new AiAssistantMessage("Sure.", [new AiToolCall("toolu_0", "add_class", new JsonObject { ["name"] = "Order" })]),
            new AiToolResults([new AiToolResult("toolu_0", "Added class Order."), new AiToolResult("toolu_x", "No.", IsError: true)]),
        ];

        var turn = await provider.ChatAsync("System.", messages, ClassDiagramTools.Definitions);

        Assert.Equal("Adding it.", turn.Text);
        var call = Assert.Single(turn.ToolCalls);
        Assert.Equal(("toolu_1", "add_class", "Payment"), (call.Id, call.Name, (string?)call.Arguments["name"]));

        var body = JsonNode.Parse(handler.Requests[0].Body)!;
        Assert.Equal("add_class", (string?)body["tools"]![0]!["name"]);
        Assert.NotNull(body["tools"]![0]!["input_schema"]);
        var sent = body["messages"]!.AsArray();
        Assert.Equal("tool_use", (string?)sent[1]!["content"]![1]!["type"]);
        Assert.Equal("toolu_0", (string?)sent[1]!["content"]![1]!["id"]);
        Assert.Equal("user", (string?)sent[2]!["role"]);
        Assert.Equal("tool_result", (string?)sent[2]!["content"]![0]!["type"]);
        Assert.True((bool)sent[2]!["content"]![1]!["is_error"]!);
    }

    [Fact]
    public async Task OpenAiCompatible_SendsFunctionsAndToolMessages_AndReadsToolCalls()
    {
        var handler = FakeHandler.Replying(HttpStatusCode.OK, """
            {"choices": [{"message": {"role": "assistant", "content": null, "tool_calls": [
              {"id": "call_1", "type": "function", "function": {"name": "add_class", "arguments": "{\"name\": \"Payment\"}"}}]}}]}
            """);
        var settings = new AiServiceSettings { Provider = AiProviderKind.OpenAiCompatible, Endpoint = "http://localhost:11434/v1", Model = "qwen2.5" };
        var provider = AiProviders.Create(settings, null, new HttpClient(handler))!;
        AiMessage[] messages =
        [
            new AiUserMessage("Add Order"),
            new AiAssistantMessage(null, [new AiToolCall("call_0", "add_class", new JsonObject { ["name"] = "Order" })]),
            new AiToolResults([new AiToolResult("call_0", "Added class Order.")]),
        ];

        var turn = await provider.ChatAsync("System.", messages, ClassDiagramTools.Definitions);

        Assert.Null(turn.Text);
        Assert.Equal("Payment", (string?)Assert.Single(turn.ToolCalls).Arguments["name"]);
        var body = JsonNode.Parse(handler.Requests[0].Body)!;
        Assert.Equal("function", (string?)body["tools"]![0]!["type"]);
        Assert.Equal("add_class", (string?)body["tools"]![0]!["function"]!["name"]);
        var sent = body["messages"]!.AsArray();
        Assert.Equal(["system", "user", "assistant", "tool"], sent.Select(m => (string?)m!["role"]));
        Assert.Equal("{\"name\":\"Order\"}", (string?)sent[2]!["tool_calls"]![0]!["function"]!["arguments"]);
        Assert.Equal("call_0", (string?)sent[3]!["tool_call_id"]);
    }
}
