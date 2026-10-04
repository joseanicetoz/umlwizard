using System.Net;
using System.Text.Json.Nodes;

namespace UmlWizard.Ai.Tests;

/// <summary>Answers requests from a script, and keeps them to check; no test talks to a real service.</summary>
internal sealed class FakeHandler(Func<HttpRequestMessage, string, CancellationToken, Task<HttpResponseMessage>> respond) : HttpMessageHandler
{
    public List<(HttpRequestMessage Request, string Body)> Requests { get; } = [];

    public static FakeHandler Replying(HttpStatusCode status, string json) =>
        new((_, _, _) => Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(json) }));

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
        Requests.Add((request, body));
        return await respond(request, body, cancellationToken);
    }
}

public sealed class ProviderTests
{
    private static readonly AiServiceSettings Anthropic = new() { Provider = AiProviderKind.Anthropic };

    [Fact]
    public async Task Anthropic_PostsToTheMessagesApi_AndReturnsTheTextOfTheReply()
    {
        var handler = FakeHandler.Replying(HttpStatusCode.OK, """
            {"content": [{"type": "text", "text": "Hello"}, {"type": "text", "text": ", world"}], "stop_reason": "end_turn"}
            """);
        var provider = AiProviders.Create(Anthropic, "sk-ant-test", new HttpClient(handler))!;

        Assert.Equal("Claude (claude-sonnet-5-5)", provider.DisplayName);
        Assert.Equal("Hello, world", await provider.CompleteAsync("Be brief.", "Say hello"));

        var (request, body) = Assert.Single(handler.Requests);
        Assert.Equal("https://api.anthropic.com/v1/messages", request.RequestUri!.ToString());
        Assert.Equal("sk-ant-test", request.Headers.GetValues("x-api-key").Single());
        Assert.Equal("2023-06-01", request.Headers.GetValues("anthropic-version").Single());
        var json = JsonNode.Parse(body)!;
        Assert.Equal("claude-sonnet-5-5", (string?)json["model"]);
        Assert.Equal("Be brief.", (string?)json["system"]);
        Assert.Equal("Say hello", (string?)json["messages"]![0]!["content"]);
        Assert.Equal("user", (string?)json["messages"]![0]!["role"]);
        Assert.True((int)json["max_tokens"]! > 0);
    }

    [Fact]
    public async Task Anthropic_WithoutAKey_SaysSo_WithoutSendingAnything()
    {
        var handler = FakeHandler.Replying(HttpStatusCode.OK, "{}");
        var provider = AiProviders.Create(Anthropic, "  ", new HttpClient(handler))!;

        var error = await Assert.ThrowsAsync<AiException>(() => provider.CompleteAsync("i", "m"));
        Assert.Contains("API key", error.Message);
        Assert.Empty(handler.Requests);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, "refused the API key: invalid x-api-key")]
    [InlineData(HttpStatusCode.NotFound, "does not know this endpoint or model: invalid x-api-key")]
    [InlineData(HttpStatusCode.TooManyRequests, "limit is reached: invalid x-api-key")]
    [InlineData(HttpStatusCode.InternalServerError, "answered with an error (500): invalid x-api-key")]
    public async Task Errors_CarryTheServicesMessage(HttpStatusCode status, string expected)
    {
        var handler = FakeHandler.Replying(status, """{"type": "error", "error": {"type": "x", "message": "invalid x-api-key"}}""");
        var provider = AiProviders.Create(Anthropic, "key", new HttpClient(handler))!;

        var error = await Assert.ThrowsAsync<AiException>(() => provider.CompleteAsync("i", "m"));
        Assert.Contains(expected, error.Message);
    }

    [Fact]
    public async Task ANonJsonError_IsShownShortened()
    {
        var handler = FakeHandler.Replying(HttpStatusCode.BadGateway, "<html>" + new string('x', 500) + "</html>");
        var provider = AiProviders.Create(Anthropic, "key", new HttpClient(handler))!;

        var error = await Assert.ThrowsAsync<AiException>(() => provider.CompleteAsync("i", "m"));
        Assert.Contains("(502)", error.Message);
        Assert.EndsWith("…", error.Message);
    }

    [Fact]
    public async Task ASlowService_TimesOut_ButCancellingIsNotAnError()
    {
        var handler = new FakeHandler(async (_, _, cancellationToken) =>
        {
            await Task.Delay(TimeSpan.FromSeconds(30), cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK);
        });
        var provider = AiProviders.Create(Anthropic, "key", new HttpClient(handler))!;
        var timeout = HttpAiProvider.Timeout;
        HttpAiProvider.Timeout = TimeSpan.FromMilliseconds(100);
        try
        {
            var error = await Assert.ThrowsAsync<AiException>(() => provider.CompleteAsync("i", "m"));
            Assert.Contains("did not answer within", error.Message);

            using var cancel = new CancellationTokenSource(TimeSpan.FromMilliseconds(20));
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => provider.CompleteAsync("i", "m", cancel.Token));
        }
        finally
        {
            HttpAiProvider.Timeout = timeout;
        }
    }

    [Fact]
    public async Task OpenAiCompatible_PostsChatCompletions_WithTheKeyOnlyWhenThereIsOne()
    {
        var handler = FakeHandler.Replying(HttpStatusCode.OK, """{"choices": [{"message": {"role": "assistant", "content": "Hi"}}]}""");

        // A local Ollama server: no key, and no Authorization header.
        var ollama = new AiServiceSettings { Provider = AiProviderKind.OpenAiCompatible, Endpoint = "http://localhost:11434/v1/", Model = "llama3.1" };
        var local = AiProviders.Create(ollama, null, new HttpClient(handler))!;
        Assert.Equal("llama3.1 (localhost)", local.DisplayName);
        Assert.Equal("Hi", await local.CompleteAsync("System.", "User."));
        var (request, body) = handler.Requests[0];
        Assert.Equal("http://localhost:11434/v1/chat/completions", request.RequestUri!.ToString());
        Assert.Null(request.Headers.Authorization);
        var json = JsonNode.Parse(body)!;
        Assert.Equal("llama3.1", (string?)json["model"]);
        Assert.Equal(["system", "user"], json["messages"]!.AsArray().Select(m => (string?)m!["role"]));
        Assert.Null(json["max_tokens"]);

        // OpenAI itself: the key as a bearer token.
        var openAi = new AiServiceSettings { Provider = AiProviderKind.OpenAiCompatible, Model = "gpt-4o-mini" };
        await AiProviders.Create(openAi, "sk-test", new HttpClient(handler))!.CompleteAsync("s", "u");
        Assert.Equal("Bearer sk-test", handler.Requests[1].Request.Headers.Authorization!.ToString());
        Assert.Equal("https://api.openai.com/v1/chat/completions", handler.Requests[1].Request.RequestUri!.ToString());
    }

    [Fact]
    public async Task OpenAiCompatible_NeedsAModel_AndOpenAiItselfAKey()
    {
        var handler = FakeHandler.Replying(HttpStatusCode.OK, "{}");
        var noModel = AiProviders.Create(new AiServiceSettings { Provider = AiProviderKind.OpenAiCompatible }, "k", new HttpClient(handler))!;
        Assert.Contains("Name the model", (await Assert.ThrowsAsync<AiException>(() => noModel.CompleteAsync("s", "u"))).Message);

        var noKey = AiProviders.Create(new AiServiceSettings { Provider = AiProviderKind.OpenAiCompatible, Model = "gpt-4o" }, null, new HttpClient(handler))!;
        Assert.Contains("API key", (await Assert.ThrowsAsync<AiException>(() => noKey.CompleteAsync("s", "u"))).Message);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public void Settings_FallBackToTheProvidersDefaults()
    {
        Assert.False(new AiServiceSettings().IsEnabled);
        Assert.Null(AiProviders.Create(new AiServiceSettings(), "key"));
        Assert.Equal("https://api.anthropic.com", Anthropic.EffectiveEndpoint);
        Assert.Equal("claude-sonnet-5-5", Anthropic.EffectiveModel);
        Assert.Equal("ANTHROPIC_API_KEY", Anthropic.KeyEnvironmentVariable);
        Assert.Equal("ai.Anthropic.apiKey", Anthropic.SecretName);
        Assert.True(Anthropic.NeedsApiKey);
        Assert.False(new AiServiceSettings { Provider = AiProviderKind.OpenAiCompatible, Endpoint = "http://localhost:1234/v1" }.NeedsApiKey);
        Assert.Equal("http://x/v1", new AiServiceSettings { Provider = AiProviderKind.OpenAiCompatible, Endpoint = " http://x/v1/ " }.EffectiveEndpoint);
    }
}
