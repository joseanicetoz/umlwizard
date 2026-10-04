using System.Text.Json.Nodes;

namespace UmlWizard.Ai;

/// <summary>A tool the model may call: its name, what it does, and its arguments as a JSON schema.</summary>
public sealed record AiTool(string Name, string Description, JsonObject InputSchema);

/// <summary>A call of a tool by the model, with its arguments.</summary>
public sealed record AiToolCall(string Id, string Name, JsonObject Arguments);

/// <summary>What a tool call did, sent back to the model; an error lets it correct itself.</summary>
public sealed record AiToolResult(string CallId, string Content, bool IsError = false);

/// <summary>A message of a conversation with the model.</summary>
public abstract record AiMessage;

/// <summary>What the user wrote.</summary>
public sealed record AiUserMessage(string Text) : AiMessage;

/// <summary>What the model answered: text, tool calls, or both.</summary>
public sealed record AiAssistantMessage(string? Text, IReadOnlyList<AiToolCall> ToolCalls) : AiMessage;

/// <summary>The results of the tool calls of the model's last message.</summary>
public sealed record AiToolResults(IReadOnlyList<AiToolResult> Results) : AiMessage;

/// <summary>One answer of the model in a conversation: text, and the tools it wants called (none when it is done).</summary>
public sealed record AiTurn(string? Text, IReadOnlyList<AiToolCall> ToolCalls)
{
    public AiAssistantMessage ToMessage() => new(Text, ToolCalls);
}
