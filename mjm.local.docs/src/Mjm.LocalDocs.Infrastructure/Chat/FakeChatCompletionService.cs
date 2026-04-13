using Mjm.LocalDocs.Core.Abstractions;

namespace Mjm.LocalDocs.Infrastructure.Chat;

/// <summary>
/// Fake chat completion service for development and testing.
/// Simulates streaming responses without making any external API calls.
/// NOT suitable for production use.
/// </summary>
public sealed class FakeChatCompletionService : IChatCompletionService
{
    private const string FakeResponse =
        "This is a simulated response from the Fake chat provider. " +
        "To use a real language model, configure a provider (OpenAI, AzureOpenAI, Anthropic, or Ollama) " +
        "under the LocalDocs:Chat section in appsettings.json and set Enabled to true.";

    /// <inheritdoc />
    public async IAsyncEnumerable<string> CompleteStreamAsync(
        string systemPrompt,
        IEnumerable<ChatTurn> history,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        foreach (var word in FakeResponse.Split(' '))
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return word + " ";
            await Task.Delay(40, cancellationToken);
        }
    }
}
