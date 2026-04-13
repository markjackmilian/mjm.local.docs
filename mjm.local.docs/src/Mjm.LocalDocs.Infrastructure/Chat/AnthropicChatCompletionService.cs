using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Mjm.LocalDocs.Core.Abstractions;
using Mjm.LocalDocs.Core.Configuration;

namespace Mjm.LocalDocs.Infrastructure.Chat;

/// <summary>
/// Chat completion service for Anthropic Claude models.
/// Uses the Anthropic Messages API with server-sent events (SSE) streaming.
/// </summary>
public sealed class AnthropicChatCompletionService : IChatCompletionService
{
    private const string AnthropicApiUrl = "https://api.anthropic.com/v1/messages";
    private const string AnthropicVersion = "2023-06-01";

    private readonly string _apiKey;
    private readonly string _model;
    private readonly int _maxTokens;
    private readonly HttpClient _httpClient;

    public AnthropicChatCompletionService(AnthropicChatOptions options, HttpClient? httpClient = null)
    {
        _apiKey = options.ApiKey
            ?? Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY")
            ?? throw new InvalidOperationException(
                "Anthropic chat provider requires an API key. " +
                "Configure 'LocalDocs:Chat:Anthropic:ApiKey' in appsettings.json " +
                "or set the ANTHROPIC_API_KEY environment variable.");

        _model = options.Model;
        _maxTokens = options.MaxTokens;
        _httpClient = httpClient ?? new HttpClient();
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<string> CompleteStreamAsync(
        string systemPrompt,
        IEnumerable<ChatTurn> history,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var messages = history
            .Select(t => new { role = t.Role, content = t.Content })
            .ToList<object>();

        var requestBody = new
        {
            model = _model,
            max_tokens = _maxTokens,
            system = systemPrompt,
            messages,
            stream = true
        };

        var json = JsonSerializer.Serialize(requestBody);
        using var request = new HttpRequestMessage(HttpMethod.Post, AnthropicApiUrl);
        request.Headers.Add("x-api-key", _apiKey);
        request.Headers.Add("anthropic-version", AnthropicVersion);
        request.Content = new StringContent(json, Encoding.UTF8, "application/json");

        using var response = await _httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);

        response.EnsureSuccessStatusCode();

        using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var reader = new StreamReader(stream);

        while (!reader.EndOfStream)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var line = await reader.ReadLineAsync(cancellationToken);
            if (string.IsNullOrEmpty(line) || !line.StartsWith("data: "))
                continue;

            var data = line["data: ".Length..];
            if (data == "[DONE]")
                break;

            string? token = null;
            try
            {
                using var doc = JsonDocument.Parse(data);
                var root = doc.RootElement;

                if (!root.TryGetProperty("type", out var typeEl))
                    continue;

                if (typeEl.GetString() != "content_block_delta")
                    continue;

                if (!root.TryGetProperty("delta", out var delta))
                    continue;

                if (!delta.TryGetProperty("type", out var deltaType) ||
                    deltaType.GetString() != "text_delta")
                    continue;

                if (delta.TryGetProperty("text", out var textEl))
                    token = textEl.GetString();
            }
            catch (JsonException)
            {
                // Malformed SSE data — skip
                continue;
            }

            if (!string.IsNullOrEmpty(token))
                yield return token;
        }
    }
}
