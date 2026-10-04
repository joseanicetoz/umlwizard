using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace UmlWizard.Ai;

/// <summary>What the providers share: posting JSON, the time limit, and turning failures into <see cref="AiException"/>.</summary>
public abstract class HttpAiProvider(HttpClient http, AiServiceSettings settings, string? apiKey) : IAiProvider
{
    /// <summary>How long a request may take: models can take a while for a large diagram.</summary>
    public static TimeSpan Timeout { get; set; } = TimeSpan.FromMinutes(2);

    /// <summary>The longest reply asked for.</summary>
    protected const int MaxTokens = 8000;

    protected AiServiceSettings Settings { get; } = settings;

    protected string? ApiKey { get; } = string.IsNullOrWhiteSpace(apiKey) ? null : apiKey.Trim();

    public abstract string DisplayName { get; }

    public abstract Task<string> CompleteAsync(string instructions, string message, CancellationToken cancellationToken = default);

    public abstract Task<AiTurn> ChatAsync(
        string instructions, IReadOnlyList<AiMessage> messages, IReadOnlyList<AiTool> tools, CancellationToken cancellationToken = default);

    public abstract Task<AiTurn> StreamChatAsync(
        string instructions, IReadOnlyList<AiMessage> messages, IReadOnlyList<AiTool> tools, Action<string> onText, CancellationToken cancellationToken = default);

    /// <summary>
    /// Posts <paramref name="body"/> to <paramref name="url"/> and hands each server-sent event's data to
    /// <paramref name="onEvent"/> as it comes. Returns <c>null</c>, or the reply's JSON when the service answered
    /// without streaming.
    /// </summary>
    protected async Task<JsonNode?> StreamAsync(
        string url, JsonObject body, Action<HttpRequestHeaders> headers, Action<JsonNode> onEvent, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(Timeout);
        using var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json"),
        };
        headers(request.Headers);

        try
        {
            using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            if (!response.IsSuccessStatusCode || response.Content.Headers.ContentType?.MediaType != "text/event-stream")
            {
                // An error, or a service that does not stream: the whole reply at once.
                var text = await response.Content.ReadAsStringAsync(timeout.Token);
                return Reply(response, text);
            }

            await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token);
            using var reader = new StreamReader(stream);
            while (await reader.ReadLineAsync(timeout.Token) is { } line)
            {
                if (!line.StartsWith("data:", StringComparison.Ordinal))
                {
                    continue;
                }

                var data = line[5..].Trim();
                if (data == "[DONE]")
                {
                    break;
                }

                JsonNode? json;
                try
                {
                    json = JsonNode.Parse(data);
                }
                catch (JsonException)
                {
                    continue;
                }

                if (json?["error"] is not null)
                {
                    throw new AiException($"{DisplayName} answered with an error: {ErrorMessage(json)}");
                }

                if (json is not null)
                {
                    onEvent(json);
                }
            }

            return null;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new AiException($"{DisplayName} did not answer within {Timeout.TotalSeconds:0} seconds.");
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException)
        {
            throw new AiException($"Could not reach {Settings.EffectiveEndpoint}: {ex.Message}", ex);
        }
    }

    /// <summary>Posts <paramref name="body"/> to <paramref name="url"/> and returns the reply's JSON.</summary>
    protected async Task<JsonNode> PostAsync(string url, JsonObject body, Action<HttpRequestHeaders> headers, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(Timeout);
        using var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json"),
        };
        headers(request.Headers);

        HttpResponseMessage response;
        string text;
        try
        {
            response = await http.SendAsync(request, timeout.Token);
            text = await response.Content.ReadAsStringAsync(timeout.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new AiException($"{DisplayName} did not answer within {Timeout.TotalSeconds:0} seconds.");
        }
        catch (HttpRequestException ex)
        {
            throw new AiException($"Could not reach {Settings.EffectiveEndpoint}: {ex.Message}", ex);
        }

        using (response)
        {
            return Reply(response, text);
        }
    }

    /// <summary>The reply's JSON; an <see cref="AiException"/> with the service's own message when it is an error.</summary>
    private JsonNode Reply(HttpResponseMessage response, string text)
    {
        JsonNode? json = null;
        try
        {
            json = JsonNode.Parse(text);
        }
        catch (JsonException)
        {
            // Not JSON, e.g. an HTML error page of a proxy: reported below.
        }

        if (!response.IsSuccessStatusCode)
        {
            var detail = ErrorMessage(json) ?? Shorten(text);
            throw new AiException(response.StatusCode switch
            {
                HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => $"{DisplayName} refused the API key: {detail}",
                HttpStatusCode.NotFound => $"{DisplayName} does not know this endpoint or model: {detail}",
                HttpStatusCode.TooManyRequests => $"{DisplayName} is busy or the account's limit is reached: {detail}",
                _ => $"{DisplayName} answered with an error ({(int)response.StatusCode}): {detail}",
            });
        }

        return json ?? throw new AiException($"{DisplayName} answered with something that is not JSON: {Shorten(text)}");
    }

    /// <summary>The arguments of a tool call sent as a JSON string; empty when they are not an object.</summary>
    protected static JsonObject Arguments(string? json)
    {
        try
        {
            return string.IsNullOrWhiteSpace(json) ? [] : JsonNode.Parse(json) as JsonObject ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    /// <summary>The service's own error message: <c>{"error": {"message": …}}</c> for both APIs.</summary>
    private static string? ErrorMessage(JsonNode? json) =>
        json?["error"] is JsonObject error ? (string?)error["message"] : json?["error"] is JsonValue value ? value.ToString() : null;

    private static string Shorten(string text)
    {
        var trimmed = text.Trim();
        return trimmed.Length <= 200 ? trimmed : trimmed[..200] + "…";
    }
}

/// <summary>Claude, through Anthropic's Messages API.</summary>
public sealed class AnthropicProvider(HttpClient http, AiServiceSettings settings, string? apiKey) : HttpAiProvider(http, settings, apiKey)
{
    public const string ApiVersion = "2023-06-01";

    public override string DisplayName => $"Claude ({Settings.EffectiveModel})";

    public override async Task<string> CompleteAsync(string instructions, string message, CancellationToken cancellationToken = default)
    {
        if (ApiKey is null)
        {
            throw new AiException("Claude needs an API key: set it in Preferences → AI.");
        }

        var body = new JsonObject
        {
            ["model"] = Settings.EffectiveModel,
            ["max_tokens"] = MaxTokens,
            ["system"] = instructions,
            ["messages"] = new JsonArray(new JsonObject { ["role"] = "user", ["content"] = message }),
        };
        var reply = await PostAsync($"{Settings.EffectiveEndpoint}/v1/messages", body, headers =>
        {
            headers.Add("x-api-key", ApiKey);
            headers.Add("anthropic-version", ApiVersion);
        }, cancellationToken);

        // The reply is a list of content blocks; the text blocks are the answer.
        var text = string.Concat((reply["content"] as JsonArray ?? [])
            .Where(block => (string?)block?["type"] == "text")
            .Select(block => (string?)block!["text"]));
        return string.IsNullOrWhiteSpace(text) ? throw new AiException($"{DisplayName} gave no answer.") : text;
    }

    public override Task<AiTurn> ChatAsync(
        string instructions, IReadOnlyList<AiMessage> messages, IReadOnlyList<AiTool> tools, CancellationToken cancellationToken = default) =>
        ChatAsync(instructions, messages, tools, null, cancellationToken);

    public override Task<AiTurn> StreamChatAsync(
        string instructions, IReadOnlyList<AiMessage> messages, IReadOnlyList<AiTool> tools, Action<string> onText, CancellationToken cancellationToken = default) =>
        ChatAsync(instructions, messages, tools, onText, cancellationToken);

    private async Task<AiTurn> ChatAsync(
        string instructions, IReadOnlyList<AiMessage> messages, IReadOnlyList<AiTool> tools, Action<string>? onText, CancellationToken cancellationToken)
    {
        if (ApiKey is null)
        {
            throw new AiException("Claude needs an API key: set it in Preferences → AI.");
        }

        var body = new JsonObject
        {
            ["model"] = Settings.EffectiveModel,
            ["max_tokens"] = MaxTokens,
            ["system"] = instructions,
            ["messages"] = new JsonArray([.. messages.Select(ToJson)]),
        };
        if (tools.Count > 0)
        {
            body["tools"] = new JsonArray([.. tools.Select(t => (JsonNode)new JsonObject
            {
                ["name"] = t.Name,
                ["description"] = t.Description,
                ["input_schema"] = t.InputSchema.DeepClone(),
            })]);
        }

        void Headers(HttpRequestHeaders headers)
        {
            headers.Add("x-api-key", ApiKey);
            headers.Add("anthropic-version", ApiVersion);
        }

        JsonNode reply;
        if (onText is null)
        {
            reply = await PostAsync($"{Settings.EffectiveEndpoint}/v1/messages", body, Headers, cancellationToken);
        }
        else
        {
            // The content blocks arrive in pieces: text deltas, and the arguments of tool calls as partial JSON.
            body["stream"] = true;
            var blocks = new SortedDictionary<int, (string Type, StringBuilder Content, string? Id, string? Name)>();
            string Text() => string.Concat(blocks.Values.Where(b => b.Type == "text").Select(b => b.Content.ToString()));
            var streamed = await StreamAsync($"{Settings.EffectiveEndpoint}/v1/messages", body, Headers, e =>
            {
                var index = (int?)e["index"] ?? 0;
                switch ((string?)e["type"])
                {
                    case "content_block_start" when e["content_block"] is JsonObject start:
                        blocks[index] = ((string?)start["type"] ?? string.Empty, new StringBuilder((string?)start["text"] ?? string.Empty), (string?)start["id"], (string?)start["name"]);
                        break;
                    case "content_block_delta" when blocks.TryGetValue(index, out var block) && e["delta"] is JsonObject delta:
                        if ((string?)delta["type"] == "text_delta")
                        {
                            block.Content.Append((string?)delta["text"]);
                            onText(Text());
                        }
                        else if ((string?)delta["type"] == "input_json_delta")
                        {
                            block.Content.Append((string?)delta["partial_json"]);
                        }

                        break;
                }
            }, cancellationToken);
            reply = streamed ?? new JsonObject
            {
                ["content"] = new JsonArray([.. blocks.Values.Select(b => (JsonNode)(b.Type == "tool_use"
                    ? new JsonObject { ["type"] = "tool_use", ["id"] = b.Id, ["name"] = b.Name, ["input"] = Arguments(b.Content.ToString()) }
                    : new JsonObject { ["type"] = b.Type, ["text"] = b.Content.ToString() }))]),
            };
        }

        var content = reply["content"] as JsonArray ?? [];
        var text = string.Concat(content.Where(b => (string?)b?["type"] == "text").Select(b => (string?)b!["text"]));
        var calls = content.Where(b => (string?)b?["type"] == "tool_use")
            .Select(b => new AiToolCall((string?)b!["id"] ?? string.Empty, (string?)b["name"] ?? string.Empty, b["input"] as JsonObject ?? []))
            .ToList();
        return new AiTurn(string.IsNullOrWhiteSpace(text) ? null : text, calls);
    }

    /// <summary>
    /// A message in the Messages API: the model's tool calls are <c>tool_use</c> blocks, and their results a user
    /// message of <c>tool_result</c> blocks.
    /// </summary>
    private static JsonNode ToJson(AiMessage message) => message switch
    {
        AiUserMessage user => new JsonObject { ["role"] = "user", ["content"] = user.Text },
        AiAssistantMessage assistant => new JsonObject
        {
            ["role"] = "assistant",
            ["content"] = new JsonArray([
                .. string.IsNullOrEmpty(assistant.Text) ? [] : new[] { (JsonNode)new JsonObject { ["type"] = "text", ["text"] = assistant.Text } },
                .. assistant.ToolCalls.Select(c => (JsonNode)new JsonObject
                {
                    ["type"] = "tool_use",
                    ["id"] = c.Id,
                    ["name"] = c.Name,
                    ["input"] = c.Arguments.DeepClone(),
                }),
            ]),
        },
        AiToolResults results => new JsonObject
        {
            ["role"] = "user",
            ["content"] = new JsonArray([.. results.Results.Select(r => (JsonNode)new JsonObject
            {
                ["type"] = "tool_result",
                ["tool_use_id"] = r.CallId,
                ["content"] = r.Content,
                ["is_error"] = r.IsError,
            })]),
        },
        _ => throw new ArgumentOutOfRangeException(nameof(message)),
    };
}

/// <summary>A service with the OpenAI chat completions API: OpenAI, Azure OpenAI, OpenRouter, Ollama, LM Studio, …</summary>
public sealed class OpenAiCompatibleProvider(HttpClient http, AiServiceSettings settings, string? apiKey) : HttpAiProvider(http, settings, apiKey)
{
    public override string DisplayName =>
        $"{(Settings.EffectiveModel is { Length: > 0 } model ? model : "OpenAI-compatible service")} ({(Uri.TryCreate(Settings.EffectiveEndpoint, UriKind.Absolute, out var uri) ? uri.Host : Settings.EffectiveEndpoint)})";

    public override async Task<string> CompleteAsync(string instructions, string message, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(Settings.EffectiveModel))
        {
            throw new AiException("Name the model to use in Preferences → AI, e.g. gpt-4o-mini, or llama3.1 for Ollama.");
        }

        if (ApiKey is null && Settings.NeedsApiKey)
        {
            throw new AiException("OpenAI needs an API key: set it in Preferences → AI.");
        }

        var body = new JsonObject
        {
            // No max_tokens: newer OpenAI models refuse it (they take max_completion_tokens), and servers have a sensible default.
            ["model"] = Settings.EffectiveModel,
            ["messages"] = new JsonArray(
                new JsonObject { ["role"] = "system", ["content"] = instructions },
                new JsonObject { ["role"] = "user", ["content"] = message }),
        };
        var reply = await PostAsync($"{Settings.EffectiveEndpoint}/chat/completions", body, headers =>
        {
            // Local servers need no key, and some refuse an empty one.
            if (ApiKey is not null)
            {
                headers.Authorization = new AuthenticationHeaderValue("Bearer", ApiKey);
            }
        }, cancellationToken);

        var text = (string?)reply["choices"]?[0]?["message"]?["content"];
        return string.IsNullOrWhiteSpace(text) ? throw new AiException($"{DisplayName} gave no answer.") : text;
    }

    public override Task<AiTurn> ChatAsync(
        string instructions, IReadOnlyList<AiMessage> messages, IReadOnlyList<AiTool> tools, CancellationToken cancellationToken = default) =>
        ChatAsync(instructions, messages, tools, null, cancellationToken);

    public override Task<AiTurn> StreamChatAsync(
        string instructions, IReadOnlyList<AiMessage> messages, IReadOnlyList<AiTool> tools, Action<string> onText, CancellationToken cancellationToken = default) =>
        ChatAsync(instructions, messages, tools, onText, cancellationToken);

    private async Task<AiTurn> ChatAsync(
        string instructions, IReadOnlyList<AiMessage> messages, IReadOnlyList<AiTool> tools, Action<string>? onText, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(Settings.EffectiveModel))
        {
            throw new AiException("Name the model to use in Preferences → AI, e.g. gpt-4o-mini, or llama3.1 for Ollama.");
        }

        if (ApiKey is null && Settings.NeedsApiKey)
        {
            throw new AiException("OpenAI needs an API key: set it in Preferences → AI.");
        }

        var body = new JsonObject
        {
            ["model"] = Settings.EffectiveModel,
            ["messages"] = new JsonArray([
                new JsonObject { ["role"] = "system", ["content"] = instructions },
                .. messages.SelectMany(ToJson),
            ]),
        };
        if (tools.Count > 0)
        {
            body["tools"] = new JsonArray([.. tools.Select(t => (JsonNode)new JsonObject
            {
                ["type"] = "function",
                ["function"] = new JsonObject
                {
                    ["name"] = t.Name,
                    ["description"] = t.Description,
                    ["parameters"] = t.InputSchema.DeepClone(),
                },
            })]);
        }

        void Headers(HttpRequestHeaders headers)
        {
            if (ApiKey is not null)
            {
                headers.Authorization = new AuthenticationHeaderValue("Bearer", ApiKey);
            }
        }

        JsonNode reply;
        if (onText is null)
        {
            reply = await PostAsync($"{Settings.EffectiveEndpoint}/chat/completions", body, Headers, cancellationToken);
        }
        else
        {
            // Each chunk has a delta: more text, or pieces of tool calls by their index.
            body["stream"] = true;
            var said = new StringBuilder();
            var parts = new SortedDictionary<int, (string? Id, StringBuilder Name, StringBuilder Arguments)>();
            var streamed = await StreamAsync($"{Settings.EffectiveEndpoint}/chat/completions", body, Headers, e =>
            {
                if (e["choices"]?[0]?["delta"] is not JsonObject delta)
                {
                    return;
                }

                if ((string?)delta["content"] is { Length: > 0 } piece)
                {
                    said.Append(piece);
                    onText(said.ToString());
                }

                foreach (var call in delta["tool_calls"] as JsonArray ?? [])
                {
                    var index = (int?)call?["index"] ?? 0;
                    var part = parts.TryGetValue(index, out var p) ? p : (null, new StringBuilder(), new StringBuilder());
                    part.Id ??= (string?)call?["id"];
                    part.Name.Append((string?)call?["function"]?["name"]);
                    part.Arguments.Append((string?)call?["function"]?["arguments"]);
                    parts[index] = part;
                }
            }, cancellationToken);
            reply = streamed ?? new JsonObject
            {
                ["choices"] = new JsonArray(new JsonObject
                {
                    ["message"] = new JsonObject
                    {
                        ["content"] = said.ToString(),
                        ["tool_calls"] = new JsonArray([.. parts.Values.Select(c => (JsonNode)new JsonObject
                        {
                            ["id"] = c.Id ?? Guid.NewGuid().ToString("N")[..12],
                            ["function"] = new JsonObject { ["name"] = c.Name.ToString(), ["arguments"] = c.Arguments.ToString() },
                        })]),
                    },
                }),
            };
        }

        var message = reply["choices"]?[0]?["message"];
        var text = (string?)message?["content"];
        var calls = (message?["tool_calls"] as JsonArray ?? [])
            .Select(c => new AiToolCall(
                (string?)c?["id"] ?? string.Empty,
                (string?)c?["function"]?["name"] ?? string.Empty,
                Arguments((string?)c?["function"]?["arguments"])))
            .ToList();
        return new AiTurn(string.IsNullOrWhiteSpace(text) ? null : text, calls);
    }

    /// <summary>
    /// A message in the chat completions API: the model's tool calls are <c>tool_calls</c> with their arguments as a
    /// JSON string, and each result a <c>tool</c> message of its own.
    /// </summary>
    private static IEnumerable<JsonNode> ToJson(AiMessage message) => message switch
    {
        AiUserMessage user => [new JsonObject { ["role"] = "user", ["content"] = user.Text }],
        AiAssistantMessage assistant => [new JsonObject
        {
            ["role"] = "assistant",
            ["content"] = assistant.Text,
            ["tool_calls"] = assistant.ToolCalls.Count == 0 ? null : new JsonArray([.. assistant.ToolCalls.Select(c => (JsonNode)new JsonObject
            {
                ["id"] = c.Id,
                ["type"] = "function",
                ["function"] = new JsonObject { ["name"] = c.Name, ["arguments"] = c.Arguments.ToJsonString() },
            })]),
        }],
        AiToolResults results => results.Results.Select(r => (JsonNode)new JsonObject
        {
            ["role"] = "tool",
            ["tool_call_id"] = r.CallId,
            ["content"] = r.IsError ? "Error: " + r.Content : r.Content,
        }),
        _ => throw new ArgumentOutOfRangeException(nameof(message)),
    };
}

/// <summary>Makes the provider of the chosen service.</summary>
public static class AiProviders
{
    /// <summary>Shared by every request; each request has its own time limit (<see cref="HttpAiProvider.Timeout"/>).</summary>
    private static readonly HttpClient SharedHttp = new() { Timeout = System.Threading.Timeout.InfiniteTimeSpan };

    /// <summary>The provider of <paramref name="settings"/>, or <c>null</c> while no service is chosen.</summary>
    public static IAiProvider? Create(AiServiceSettings settings, string? apiKey, HttpClient? http = null) => settings.Provider switch
    {
        AiProviderKind.Anthropic => new AnthropicProvider(http ?? SharedHttp, settings, apiKey),
        AiProviderKind.OpenAiCompatible => new OpenAiCompatibleProvider(http ?? SharedHttp, settings, apiKey),
        _ => null,
    };
}
