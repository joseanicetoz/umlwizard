using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using UmlWizard.Core.Model;

namespace UmlWizard.Ai.Tests;

/// <summary>Answers that come in pieces, as server-sent events.</summary>
public sealed class StreamingTests
{
    private static FakeHandler Streaming(params string[] events) =>
        new((_, _, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(string.Concat(events.Select(e => $"data: {e}\n\n")), Encoding.UTF8, "text/event-stream"),
        }));

    [Fact]
    public async Task Anthropic_ReportsTheTextWhileItComes_AndReadsToolCallsFromTheirPieces()
    {
        var handler = Streaming(
            """{"type": "message_start", "message": {"id": "m"}}""",
            """{"type": "content_block_start", "index": 0, "content_block": {"type": "text", "text": ""}}""",
            """{"type": "content_block_delta", "index": 0, "delta": {"type": "text_delta", "text": "Adding "}}""",
            """{"type": "content_block_delta", "index": 0, "delta": {"type": "text_delta", "text": "it."}}""",
            """{"type": "content_block_stop", "index": 0}""",
            """{"type": "content_block_start", "index": 1, "content_block": {"type": "tool_use", "id": "toolu_1", "name": "add_class", "input": {}}}""",
            """{"type": "content_block_delta", "index": 1, "delta": {"type": "input_json_delta", "partial_json": "{\"name\": \"Pay"}}""",
            """{"type": "content_block_delta", "index": 1, "delta": {"type": "input_json_delta", "partial_json": "ment\"}"}}""",
            """{"type": "content_block_stop", "index": 1}""",
            """{"type": "message_stop"}""");
        var provider = AiProviders.Create(new AiServiceSettings { Provider = AiProviderKind.Anthropic }, "key", new HttpClient(handler))!;
        var seen = new List<string>();

        var turn = await provider.StreamChatAsync("System.", [new AiUserMessage("Add a payment")], ClassDiagramTools.Definitions, seen.Add);

        Assert.Equal(["Adding ", "Adding it."], seen);
        Assert.Equal("Adding it.", turn.Text);
        var call = Assert.Single(turn.ToolCalls);
        Assert.Equal(("toolu_1", "add_class", "Payment"), (call.Id, call.Name, (string?)call.Arguments["name"]));
        Assert.True((bool)JsonNode.Parse(handler.Requests[0].Body)!["stream"]!);
    }

    [Fact]
    public async Task OpenAiCompatible_ReportsTheTextWhileItComes_AndReadsToolCallsFromTheirPieces()
    {
        var handler = Streaming(
            """{"choices": [{"delta": {"role": "assistant", "content": "Two "}}]}""",
            """{"choices": [{"delta": {"content": "classes."}}]}""",
            """{"choices": [{"delta": {"tool_calls": [{"index": 0, "id": "call_1", "function": {"name": "add_class", "arguments": "{\"na"}}]}}]}""",
            """{"choices": [{"delta": {"tool_calls": [{"index": 0, "function": {"arguments": "me\": \"Payment\"}"}}]}}]}""",
            "[DONE]");
        var settings = new AiServiceSettings { Provider = AiProviderKind.OpenAiCompatible, Endpoint = "http://localhost:11434/v1", Model = "qwen2.5" };
        var provider = AiProviders.Create(settings, null, new HttpClient(handler))!;
        var seen = new List<string>();

        var turn = await provider.StreamChatAsync("System.", [new AiUserMessage("Hi")], ClassDiagramTools.Definitions, seen.Add);

        Assert.Equal(["Two ", "Two classes."], seen);
        Assert.Equal("Two classes.", turn.Text);
        var call = Assert.Single(turn.ToolCalls);
        Assert.Equal(("call_1", "add_class", "Payment"), (call.Id, call.Name, (string?)call.Arguments["name"]));
    }

    [Fact]
    public async Task AServiceThatDoesNotStream_IsReadWhole()
    {
        var handler = FakeHandler.Replying(HttpStatusCode.OK, """{"choices": [{"message": {"role": "assistant", "content": "Hi"}}]}""");
        var settings = new AiServiceSettings { Provider = AiProviderKind.OpenAiCompatible, Endpoint = "http://localhost:1234/v1", Model = "m" };
        var seen = new List<string>();

        var turn = await AiProviders.Create(settings, null, new HttpClient(handler))!.StreamChatAsync("S", [new AiUserMessage("Hi")], [], seen.Add);

        Assert.Equal("Hi", turn.Text);
        Assert.Empty(seen);
    }

    [Fact]
    public async Task AnErrorInTheStream_OrBeforeIt_IsAnAiException()
    {
        var anthropic = new AiServiceSettings { Provider = AiProviderKind.Anthropic };
        var midway = Streaming(
            """{"type": "content_block_start", "index": 0, "content_block": {"type": "text", "text": ""}}""",
            """{"type": "error", "error": {"type": "overloaded_error", "message": "Overloaded"}}""");
        var error = await Assert.ThrowsAsync<AiException>(() =>
            AiProviders.Create(anthropic, "key", new HttpClient(midway))!.StreamChatAsync("S", [new AiUserMessage("Hi")], [], _ => { }));
        Assert.Contains("Overloaded", error.Message);

        var refused = FakeHandler.Replying(HttpStatusCode.Unauthorized, """{"type": "error", "error": {"type": "x", "message": "invalid x-api-key"}}""");
        error = await Assert.ThrowsAsync<AiException>(() =>
            AiProviders.Create(anthropic, "key", new HttpClient(refused))!.StreamChatAsync("S", [new AiUserMessage("Hi")], [], _ => { }));
        Assert.Contains("refused the API key: invalid x-api-key", error.Message);
    }

    [Fact]
    public async Task TheSession_ReportsTheAnswerOfEachRound_WhileItComes()
    {
        var provider = new PartsChat(
            (["I'll add it."], new AiTurn("I'll add it.", [ScriptedChat.Call("add_class", """{"name": "Payment"}""")])),
            (["Added", "Added Payment."], new AiTurn("Added Payment.", [])));
        var seen = new List<string>();

        var answer = await new AiChatSession().SendAsync(provider, DiagramDocument.Create("D", DiagramType.Class), "Add", onText: seen.Add);

        Assert.Equal("Added Payment.", answer);
        Assert.Equal(["I'll add it.", "Added", "Added Payment."], seen);
    }

    /// <summary>Streams scripted parts, then answers with the scripted turn.</summary>
    private sealed class PartsChat(params (string[] Parts, AiTurn Turn)[] script) : IAiProvider
    {
        private readonly Queue<(string[] Parts, AiTurn Turn)> _script = new(script);

        public string DisplayName => "Parts";

        public Task<string> CompleteAsync(string instructions, string message, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<AiTurn> StreamChatAsync(
            string instructions, IReadOnlyList<AiMessage> messages, IReadOnlyList<AiTool> tools, Action<string> onText, CancellationToken cancellationToken = default)
        {
            var (parts, turn) = _script.Dequeue();
            foreach (var part in parts)
            {
                onText(part);
            }

            return Task.FromResult(turn);
        }
    }
}
