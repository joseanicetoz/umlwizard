namespace UmlWizard.Ai;

/// <summary>The kind of AI service UML Wizard talks to.</summary>
public enum AiProviderKind
{
    /// <summary>No service: the AI commands are off, and nothing is sent anywhere.</summary>
    None,

    /// <summary>Claude, through Anthropic's Messages API.</summary>
    Anthropic,

    /// <summary>A service with the OpenAI chat completions API: OpenAI, Azure OpenAI, OpenRouter, Ollama, LM Studio, …</summary>
    OpenAiCompatible,
}

/// <summary>How the AI changes a diagram in the chat.</summary>
public enum AiChangeMode
{
    /// <summary>With tool calls (add a class, add an edge…): precise, for models that can call tools.</summary>
    Tools,

    /// <summary>By answering with the whole revised diagram in PlantUML, matched by name: for models without tool calling.</summary>
    WholeDiagram,
}

/// <summary>
/// The AI service the user chose: its kind, endpoint and model. The API key is not part of it: it is kept in the
/// operating system's credential store.
/// </summary>
public sealed record AiServiceSettings
{
    public const string AnthropicEndpoint = "https://api.anthropic.com";

    public const string AnthropicModel = "claude-sonnet-5-5";

    public const string OpenAiEndpoint = "https://api.openai.com/v1";

    public AiProviderKind Provider { get; init; } = AiProviderKind.None;

    /// <summary>The service's address; <c>null</c> for the provider's default.</summary>
    public string? Endpoint { get; init; }

    /// <summary>The model to use; <c>null</c> for the provider's default (OpenAI-compatible services have none).</summary>
    public string? Model { get; init; }

    /// <summary>How the AI changes diagrams in the chat.</summary>
    public AiChangeMode ChangeMode { get; init; } = AiChangeMode.Tools;

    public bool IsEnabled => Provider != AiProviderKind.None;

    /// <summary>The endpoint used: the one set, else the provider's default.</summary>
    public string EffectiveEndpoint => string.IsNullOrWhiteSpace(Endpoint) ? DefaultEndpoint(Provider) : Endpoint.Trim().TrimEnd('/');

    /// <summary>The model used: the one set, else the provider's default; empty when there is neither.</summary>
    public string EffectiveModel => string.IsNullOrWhiteSpace(Model) ? DefaultModel(Provider) : Model.Trim();

    /// <summary>Whether the API key is required: for Anthropic, and for OpenAI itself; local servers need none.</summary>
    public bool NeedsApiKey =>
        Provider == AiProviderKind.Anthropic ||
        (Provider == AiProviderKind.OpenAiCompatible && EffectiveEndpoint.Contains("api.openai.com", StringComparison.OrdinalIgnoreCase));

    /// <summary>The name of the API key in the credential store, one per provider.</summary>
    public string SecretName => $"ai.{Provider}.apiKey";

    /// <summary>The environment variable read when the credential store has no key.</summary>
    public string? KeyEnvironmentVariable => Provider switch
    {
        AiProviderKind.Anthropic => "ANTHROPIC_API_KEY",
        AiProviderKind.OpenAiCompatible => "OPENAI_API_KEY",
        _ => null,
    };

    public static string DefaultEndpoint(AiProviderKind provider) => provider switch
    {
        AiProviderKind.Anthropic => AnthropicEndpoint,
        AiProviderKind.OpenAiCompatible => OpenAiEndpoint,
        _ => string.Empty,
    };

    public static string DefaultModel(AiProviderKind provider) => provider == AiProviderKind.Anthropic ? AnthropicModel : string.Empty;
}
