namespace UmlWizard.Ai;

/// <summary>An AI service: one request (instructions and a message), one reply.</summary>
public interface IAiProvider
{
    /// <summary>The service and model, for the user, e.g. "Claude (claude-sonnet-5-5)".</summary>
    string DisplayName { get; }

    /// <summary>Sends the instructions (the system prompt) and the user's message; returns the text of the reply.</summary>
    /// <exception cref="AiException">The service could not be reached, refused the request, or gave no answer.</exception>
    Task<string> CompleteAsync(string instructions, string message, CancellationToken cancellationToken = default);

    /// <summary>
    /// Continues a conversation in which the model may call <paramref name="tools"/>: returns its next answer, with the
    /// tool calls it makes. The caller runs them and continues with their results.
    /// </summary>
    /// <exception cref="AiException">The service could not be reached, refused the request, or gave no answer.</exception>
    Task<AiTurn> ChatAsync(
        string instructions, IReadOnlyList<AiMessage> messages, IReadOnlyList<AiTool> tools, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException($"{DisplayName} cannot hold a conversation.");

    /// <summary>
    /// <see cref="ChatAsync"/>, with the text of the answer reported while it comes: <paramref name="onText"/> gets the
    /// text so far. Services that cannot stream report it once, whole.
    /// </summary>
    /// <exception cref="AiException">The service could not be reached, refused the request, or gave no answer.</exception>
    async Task<AiTurn> StreamChatAsync(
        string instructions, IReadOnlyList<AiMessage> messages, IReadOnlyList<AiTool> tools, Action<string> onText, CancellationToken cancellationToken = default)
    {
        var turn = await ChatAsync(instructions, messages, tools, cancellationToken);
        if (turn.Text is { } text)
        {
            onText(text);
        }

        return turn;
    }
}

/// <summary>An AI request that failed, with a message fit for the user.</summary>
public sealed class AiException(string message, Exception? inner = null) : Exception(message, inner);
