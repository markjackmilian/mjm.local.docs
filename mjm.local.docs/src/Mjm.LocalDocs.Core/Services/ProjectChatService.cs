using System.Runtime.CompilerServices;
using System.Text;
using Mjm.LocalDocs.Core.Abstractions;
using Mjm.LocalDocs.Core.Configuration;
using Microsoft.Extensions.Options;

namespace Mjm.LocalDocs.Core.Services;

/// <summary>
/// Orchestrates RAG-based chat for a project.
/// Retrieves relevant document chunks and streams a completion from the configured LLM.
/// </summary>
public sealed class ProjectChatService
{
    private readonly DocumentService _documentService;
    private readonly IChatCompletionService _chatCompletionService;
    private readonly int _maxContextChunks;

    public ProjectChatService(
        DocumentService documentService,
        IChatCompletionService chatCompletionService,
        IOptions<LocalDocsOptions> options)
    {
        _documentService = documentService;
        _chatCompletionService = chatCompletionService;
        _maxContextChunks = options.Value.Chat.MaxContextChunks;
    }

    /// <summary>
    /// Streams a chat completion response grounded on the project's knowledge base.
    /// </summary>
    /// <param name="userMessage">The latest user message.</param>
    /// <param name="projectId">The project whose documents form the context.</param>
    /// <param name="projectName">The project name, used in the system prompt.</param>
    /// <param name="history">Previous turns (excluding the current user message).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>An async stream of text tokens forming the assistant response.</returns>
    public async IAsyncEnumerable<string> ChatAsync(
        string userMessage,
        string projectId,
        string projectName,
        IEnumerable<ChatTurn> history,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        // 1. Retrieve relevant chunks from the project knowledge base
        var searchResults = await _documentService.SearchAsync(
            userMessage, projectId, _maxContextChunks, cancellationToken);

        // 2. Build context from retrieved chunks
        var context = new StringBuilder();
        if (searchResults.Count > 0)
        {
            foreach (var result in searchResults)
            {
                context.AppendLine($"[Source: {result.Chunk.FileName ?? result.Chunk.DocumentId} | Score: {result.Score:F2}]");
                context.AppendLine(result.Chunk.Content);
                context.AppendLine();
            }
        }
        else
        {
            context.AppendLine("No relevant documents found in the knowledge base for this query.");
        }

        // 3. Build system prompt
        var systemPrompt = BuildSystemPrompt(projectName, context.ToString());

        // 4. Append the current user message to history for the LLM call
        var fullHistory = history
            .Append(new ChatTurn("user", userMessage))
            .ToList();

        // 5. Stream the response
        await foreach (var token in _chatCompletionService.CompleteStreamAsync(
            systemPrompt, fullHistory, cancellationToken))
        {
            yield return token;
        }
    }

    private static string BuildSystemPrompt(string projectName, string context)
    {
        return $"""
            You are an expert assistant for the project "{projectName}".
            Answer the user's question using exclusively the context below, extracted from the project knowledge base.
            If the answer cannot be derived from the context, say so clearly — do not make up information.
            Always cite the source filename when referencing specific content.

            --- CONTEXT ---
            {context}
            --- END CONTEXT ---
            """;
    }
}
