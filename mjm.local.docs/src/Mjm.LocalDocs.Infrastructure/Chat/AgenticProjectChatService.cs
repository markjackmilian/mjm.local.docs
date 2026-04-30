using System.ComponentModel;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Mjm.LocalDocs.Core.Abstractions;
using Mjm.LocalDocs.Core.Models;
using Mjm.LocalDocs.Core.Services;
using AiChatMessage = Microsoft.Extensions.AI.ChatMessage;
using AiChatOptions = Microsoft.Extensions.AI.ChatOptions;
using CoreChatTurn = Mjm.LocalDocs.Core.Abstractions.ChatTurn;
using LocalDocsOptions = Mjm.LocalDocs.Core.Configuration.LocalDocsOptions;

namespace Mjm.LocalDocs.Infrastructure.Chat;

/// <summary>
/// Project chat service that supports both classic static RAG and agentic tool-calling modes.
/// In agentic mode, the LLM autonomously decides when and how to search the knowledge base,
/// and thinking steps are surfaced as <see cref="ThinkingEvent"/> in the event stream.
/// </summary>
public sealed class AgenticProjectChatService : IProjectChatService
{
    private const int MaxSearches = 5;

    private readonly ProjectChatService _staticService;
    private readonly DocumentService _documentService;
    private readonly IChatClient? _chatClient;
    private readonly int _maxContextChunks;
    private readonly ILogger<AgenticProjectChatService> _logger;

    public AgenticProjectChatService(
        ProjectChatService staticService,
        DocumentService documentService,
        IChatClient? chatClient,
        IOptions<LocalDocsOptions> options,
        ILogger<AgenticProjectChatService> logger)
    {
        _staticService = staticService;
        _documentService = documentService;
        _chatClient = chatClient;
        _maxContextChunks = options.Value.Chat.MaxContextChunks;
        _logger = logger;
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<ChatStreamEvent> ChatAsync(
        string userMessage,
        string projectId,
        string projectName,
        IEnumerable<CoreChatTurn> history,
        bool agenticMode = false,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (!agenticMode)
        {
            _logger.LogDebug("[Chat] Classic RAG mode — project={ProjectId}", projectId);
            await foreach (var evt in _staticService.ChatAsync(
                userMessage, projectId, projectName, history, cancellationToken))
                yield return evt;
            yield break;
        }

        if (_chatClient is null)
        {
            _logger.LogWarning("[Chat] Agentic mode requested but IChatClient is null (provider may not support it)");
            yield return new TextTokenEvent(
                "⚠️ Agentic mode requires OpenAI, AzureOpenAI, or Ollama provider. " +
                "Configure a supported provider or switch to Classic mode.");
            yield break;
        }

        _logger.LogInformation("[Chat] Agentic mode — project={ProjectId}", projectId);

        await foreach (var evt in ChatAgenticAsync(
            userMessage, projectId, projectName, history, cancellationToken))
            yield return evt;
    }

    private async IAsyncEnumerable<ChatStreamEvent> ChatAgenticAsync(
        string userMessage,
        string projectId,
        string projectName,
        IEnumerable<CoreChatTurn> history,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        // Immediate feedback before the first LLM call
        yield return new ThinkingEvent("Analisi della domanda...", 0);

        // Build conversation messages
        var messages = new List<AiChatMessage>
        {
            new(ChatRole.System, BuildSystemPrompt(projectName))
        };
        messages.AddRange(history.Select(t => new AiChatMessage(
            t.Role == "assistant" ? ChatRole.Assistant : ChatRole.User, t.Content)));
        messages.Add(new AiChatMessage(ChatRole.User, userMessage));

        // Define the search tool schema (for the LLM to know it exists).
        // We do NOT use FunctionInvokingChatClient — tool calls are executed manually
        // so we can intercept them and yield ThinkingEvent steps.
        var searchTool = AIFunctionFactory.Create(
            async (
                [Description("Natural language query to find relevant information in the knowledge base")] string query,
                [Description("Maximum number of results to return (1 to 10)")] int limit,
                CancellationToken ct) =>
            {
                // Body never called — execution is handled manually in the loop below.
                _ = ct;
                return await Task.FromResult(string.Empty);
            },
            "search_docs",
            $"Searches the project knowledge base using semantic similarity. " +
            $"Call this whenever you need information to answer the user's question. " +
            $"You may call it multiple times with different queries (maximum {MaxSearches} calls).");

        var chatOptionsWithTools = new AiChatOptions { Tools = [searchTool] };
        var chatOptionsNoTools = new AiChatOptions();

        int searchCount = 0;
        int round = 0;

        // --- Tool-calling loop (non-streaming rounds) ---
        // Each round asks the LLM whether it needs to search.
        // If it does, we execute the search manually, yield a ThinkingEvent, and feed results back.
        while (searchCount < MaxSearches)
        {
            round++;
            _logger.LogDebug("[Agentic] Round {Round}: calling LLM with tools (searchCount={SearchCount}/{Max})",
                round, searchCount, MaxSearches);

            var response = await _chatClient!.GetResponseAsync(
                messages, chatOptionsWithTools, cancellationToken);

            var toolCalls = response.Messages
                .SelectMany(m => m.Contents)
                .OfType<FunctionCallContent>()
                .ToList();

            _logger.LogInformation("[Agentic] Round {Round}: LLM returned {ToolCallCount} tool call(s)",
                round, toolCalls.Count);

            if (toolCalls.Count == 0)
            {
                _logger.LogInformation(
                    "[Agentic] LLM decided no more searches needed after {SearchCount} search(es). Proceeding to final answer.",
                    searchCount);
                break;
            }

            // Add the assistant's tool-call message(s) to the conversation history
            messages.AddRange(response.Messages);

            foreach (var fc in toolCalls)
            {
                if (searchCount >= MaxSearches)
                {
                    _logger.LogInformation("[Agentic] Max searches ({Max}) reached — skipping remaining tool calls", MaxSearches);
                    break;
                }

                searchCount++;
                var query = ExtractQuery(fc);
                var limit = ExtractLimit(fc, _maxContextChunks);

                _logger.LogInformation("[Agentic] Search {N}/{Max}: query=\"{Query}\" limit={Limit}",
                    searchCount, MaxSearches, query, limit);

                yield return new ThinkingEvent(
                    $"Ricerca {searchCount}/{MaxSearches}: \"{query}\"",
                    searchCount);

                var results = await _documentService.SearchAsync(
                    query, projectId, Math.Clamp(limit, 1, 10), cancellationToken);

                _logger.LogInformation("[Agentic] Search {N}: found {ResultCount} result(s)",
                    searchCount, results.Count);

                var resultText = results.Count == 0
                    ? "No relevant documents found for this query."
                    : string.Join("\n\n", results.Select(r =>
                        $"[Source: {r.Chunk.FileName ?? r.Chunk.DocumentId} | Score: {r.Score:F2}]\n{r.Chunk.Content}"));

                messages.Add(new AiChatMessage(ChatRole.Tool,
                    [new FunctionResultContent(fc.CallId, resultText)]));
            }
        }

        // Status step: tells the user the agent is done searching and is now generating
        var summary = searchCount == 0
            ? "Contesto dalla cronologia sufficiente — elaborazione risposta..."
            : $"{searchCount} ricerca/e completata/e — elaborazione risposta...";
        yield return new ThinkingEvent(summary, 0);

        // --- Final streaming response (no tools — force the answer) ---
        _logger.LogInformation("[Agentic] Starting final streaming response (total searches={SearchCount})", searchCount);

        int tokenCount = 0;
        await foreach (var update in _chatClient!.GetStreamingResponseAsync(
            messages, chatOptionsNoTools, cancellationToken))
        {
            if (!string.IsNullOrEmpty(update.Text))
            {
                tokenCount++;
                yield return new TextTokenEvent(update.Text);
            }
        }

        _logger.LogInformation("[Agentic] Streaming complete ({TokenCount} tokens)", tokenCount);
    }

    private static string ExtractQuery(FunctionCallContent fc)
    {
        if (fc.Arguments?.TryGetValue("query", out var v) == true)
            return v?.ToString() ?? string.Empty;
        return string.Empty;
    }

    private static int ExtractLimit(FunctionCallContent fc, int defaultValue)
    {
        if (fc.Arguments?.TryGetValue("limit", out var v) == true)
        {
            if (v is System.Text.Json.JsonElement je &&
                je.ValueKind == System.Text.Json.JsonValueKind.Number)
                return je.GetInt32();
            if (int.TryParse(v?.ToString(), out var i))
                return i;
        }
        return defaultValue;
    }

    private static string BuildSystemPrompt(string projectName) => $"""
        You are an expert assistant for the project "{projectName}".
        You have access to a search_docs tool that searches the project knowledge base using semantic similarity.
        Before searching, check if the conversation history already provides enough context to answer.
        If you need more information, use the search_docs tool — you may call it up to {MaxSearches} times with different focused queries.
        Always cite the source filename when referencing specific content.
        If the knowledge base contains no relevant information, say so clearly.
        """;
}
