namespace Mjm.LocalDocs.Core.Abstractions;

/// <summary>
/// Service for generating chat completions using a language model.
/// </summary>
public interface IChatCompletionService
{
    /// <summary>
    /// Streams a chat completion response token by token.
    /// </summary>
    /// <param name="systemPrompt">The system prompt providing context and instructions to the model.</param>
    /// <param name="history">The conversation history (user and assistant turns).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>An async stream of text tokens.</returns>
    IAsyncEnumerable<string> CompleteStreamAsync(
        string systemPrompt,
        IEnumerable<ChatTurn> history,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Represents a single turn in a conversation.
/// </summary>
/// <param name="Role">The role of the speaker: "user" or "assistant".</param>
/// <param name="Content">The text content of the turn.</param>
public record ChatTurn(string Role, string Content);
