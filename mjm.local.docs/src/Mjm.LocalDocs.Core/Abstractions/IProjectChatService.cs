namespace Mjm.LocalDocs.Core.Abstractions;

/// <summary>
/// Orchestrates chat for a project, supporting both static RAG and agentic tool-calling modes.
/// </summary>
public interface IProjectChatService
{
    /// <summary>
    /// Streams a chat response grounded on the project's knowledge base.
    /// </summary>
    /// <param name="userMessage">The latest user message.</param>
    /// <param name="projectId">The project whose documents form the context.</param>
    /// <param name="projectName">The project name, used in the system prompt.</param>
    /// <param name="history">Previous turns (excluding the current user message).</param>
    /// <param name="agenticMode">
    /// When true, the LLM autonomously calls search tools as needed (requires OpenAI, AzureOpenAI, or Ollama provider).
    /// When false, uses classic static RAG (search once, build context, respond).
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>An async stream of text tokens forming the assistant response.</returns>
    IAsyncEnumerable<string> ChatAsync(
        string userMessage,
        string projectId,
        string projectName,
        IEnumerable<ChatTurn> history,
        bool agenticMode = false,
        CancellationToken cancellationToken = default);
}
