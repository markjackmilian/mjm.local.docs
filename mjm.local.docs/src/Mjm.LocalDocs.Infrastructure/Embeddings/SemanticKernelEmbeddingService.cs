using Microsoft.Extensions.AI;
using Mjm.LocalDocs.Core.Abstractions;

namespace Mjm.LocalDocs.Infrastructure.Embeddings;

/// <summary>
/// Embedding service implementation using Microsoft.Extensions.AI abstractions.
/// Works with any IEmbeddingGenerator provider (OpenAI, Ollama, etc.).
/// </summary>
public sealed class SemanticKernelEmbeddingService : IEmbeddingService
{
    private readonly IEmbeddingGenerator<string, Embedding<float>> _embeddingGenerator;
    private readonly int _maxBatchSize;

    public SemanticKernelEmbeddingService(
        IEmbeddingGenerator<string, Embedding<float>> embeddingGenerator,
        int embeddingDimension = 1536,
        int maxBatchSize = 64)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maxBatchSize, 1);

        _embeddingGenerator = embeddingGenerator;
        EmbeddingDimension = embeddingDimension;
        _maxBatchSize = maxBatchSize;
    }

    /// <inheritdoc />
    public int EmbeddingDimension { get; }

    /// <inheritdoc />
    public async Task<ReadOnlyMemory<float>> GenerateEmbeddingAsync(
        string text, 
        CancellationToken cancellationToken = default)
    {
        var result = await _embeddingGenerator.GenerateAsync(
            [text], 
            cancellationToken: cancellationToken);
        
        return result[0].Vector;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ReadOnlyMemory<float>>> GenerateEmbeddingsAsync(
        IEnumerable<string> texts, 
        CancellationToken cancellationToken = default)
    {
        var textList = texts.ToList();
        var results = new List<ReadOnlyMemory<float>>(textList.Count);

        // Split large documents into sub-batches so we never exceed the provider's
        // per-request input/token limits. Chunk preserves order, so the returned
        // vectors stay aligned with the input texts.
        foreach (var batch in textList.Chunk(_maxBatchSize))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var result = await _embeddingGenerator.GenerateAsync(
                batch,
                cancellationToken: cancellationToken);

            results.AddRange(result.Select(e => e.Vector));
        }

        return results;
    }
}
