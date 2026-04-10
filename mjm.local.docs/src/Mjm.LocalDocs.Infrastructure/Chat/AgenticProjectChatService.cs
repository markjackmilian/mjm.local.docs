using System.ComponentModel;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using Mjm.LocalDocs.Core.Abstractions;
using Mjm.LocalDocs.Core.Services;
using AiChatMessage = Microsoft.Extensions.AI.ChatMessage;
using AiChatOptions = Microsoft.Extensions.AI.ChatOptions;
using CoreChatTurn = Mjm.LocalDocs.Core.Abstractions.ChatTurn;
using LocalDocsOptions = Mjm.LocalDocs.Core.Configuration.LocalDocsOptions;

namespace Mjm.LocalDocs.Infrastructure.Chat;

/// <summary>
/// Project chat service that supports both classic static RAG and agentic tool-calling modes.
/// In agentic mode, the LLM autonomously decides when and how to search the knowledge base.
/// </summary>
public sealed class AgenticProjectChatService : IProjectChatService
{
    private readonly ProjectChatService _staticService;
    private readonly DocumentService _documentService;
    private readonly IChatClient? _chatClient;
    private readonly int _maxContextChunks;

    public AgenticProjectChatService(
        ProjectChatService staticService,
        DocumentService documentService,
        IChatClient? chatClient,
        IOptions<LocalDocsOptions> options)
    {
        _staticService = staticService;
        _documentService = documentService;
        _chatClient = chatClient;
        _maxContextChunks = options.Value.Chat.MaxContextChunks;
    }


    /// <inheritdoc />
    public async IAsyncEnumerable<string> ChatAsync(
        string userMessage,
        string projectId,
        string projectName,
        IEnumerable<CoreChatTurn> history,
        bool agenticMode = false,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (!agenticMode)
        {
            await foreach (var token in _staticService.ChatAsync(
                userMessage, projectId, projectName, history, cancellationToken))
                yield return token;
            yield break;
        }

        if (_chatClient is null)
        {
            yield return "⚠️ Agentic mode requires OpenAI, AzureOpenAI, or Ollama provider. " +
                         "Configure a supported provider or switch to Classic mode.";
            yield break;
        }

        await foreach (var token in ChatAgenticAsync(
            userMessage, projectId, projectName, history, cancellationToken))
            yield return token;
    }

    private async IAsyncEnumerable<string> ChatAgenticAsync(
        string userMessage,
        string projectId,
        string projectName,
        IEnumerable<CoreChatTurn> history,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var searchTool = AIFunctionFactory.Create(
            async (
                [Description("Natural language query to find relevant information in the knowledge base")] string query,
                [Description("Maximum number of results to return (1 to 10)")] int limit,
                CancellationToken ct) =>
            {
                var results = await _documentService.SearchAsync(
                    query, projectId, Math.Clamp(limit, 1, 10), ct);

                if (results.Count == 0)
                    return "No relevant documents found for this query.";

                return string.Join("\n\n", results.Select(r =>
                    $"[Source: {r.Chunk.FileName ?? r.Chunk.DocumentId} | Score: {r.Score:F2}]\n{r.Chunk.Content}"));
            },
            "search_docs",
            "Searches the project knowledge base using semantic similarity. " +
            "Call this to find relevant information needed to answer the user's question. " +
            "You may call it multiple times with different queries.");

        var messages = new List<AiChatMessage>
        {
            new(ChatRole.System, BuildSystemPrompt(projectName))
        };
        messages.AddRange(history.Select(t => new AiChatMessage(
            t.Role == "assistant" ? ChatRole.Assistant : ChatRole.User, t.Content)));
        messages.Add(new AiChatMessage(ChatRole.User, userMessage));

        var agenticClient = new FunctionInvokingChatClient(_chatClient!);
        var chatOptions = new AiChatOptions { Tools = [searchTool] };

        await foreach (var update in agenticClient.GetStreamingResponseAsync(
            messages, chatOptions, cancellationToken))
        {
            if (!string.IsNullOrEmpty(update.Text))
                yield return update.Text;
        }
    }

    private static string BuildSystemPrompt(string projectName) => $"""
        You are an expert assistant for the project "{projectName}".
        You have access to a search_docs tool that searches the project knowledge base using semantic similarity.
        Use it whenever you need information to answer the user's question.
        You may call the tool multiple times with different queries if needed.
        Always cite the source filename when referencing specific content.
        If the knowledge base contains no relevant information, say so clearly.
        """;
}
