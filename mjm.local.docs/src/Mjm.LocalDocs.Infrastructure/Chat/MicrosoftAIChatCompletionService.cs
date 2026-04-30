using Microsoft.Extensions.AI;
using Mjm.LocalDocs.Core.Abstractions;
using CoreChatTurn = Mjm.LocalDocs.Core.Abstractions.ChatTurn;
using AiChatMessage = Microsoft.Extensions.AI.ChatMessage;

namespace Mjm.LocalDocs.Infrastructure.Chat;

/// <summary>
/// Chat completion service backed by any Microsoft.Extensions.AI <see cref="IChatClient"/>.
/// Supports OpenAI, Azure OpenAI, and Ollama providers.
/// </summary>
public sealed class MicrosoftAIChatCompletionService : IChatCompletionService
{
    private readonly IChatClient _chatClient;

    public MicrosoftAIChatCompletionService(IChatClient chatClient)
    {
        _chatClient = chatClient;
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<string> CompleteStreamAsync(
        string systemPrompt,
        IEnumerable<CoreChatTurn> history,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var messages = new List<AiChatMessage>
        {
            new(ChatRole.System, systemPrompt)
        };

        messages.AddRange(history.Select(turn => new AiChatMessage(
            turn.Role == "assistant" ? ChatRole.Assistant : ChatRole.User,
            turn.Content)));

        await foreach (var update in _chatClient.GetStreamingResponseAsync(messages, cancellationToken: cancellationToken))
        {
            var text = update.Text;
            if (!string.IsNullOrEmpty(text))
                yield return text;
        }
    }
}
