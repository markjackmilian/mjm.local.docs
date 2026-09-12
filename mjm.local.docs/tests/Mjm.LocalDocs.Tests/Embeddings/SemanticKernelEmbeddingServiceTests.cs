using Microsoft.Extensions.AI;
using Mjm.LocalDocs.Infrastructure.Embeddings;

namespace Mjm.LocalDocs.Tests.Embeddings;

/// <summary>
/// Unit tests for <see cref="SemanticKernelEmbeddingService"/>, focused on the
/// sub-batching behaviour that keeps large documents under the provider's
/// per-request input limit.
/// </summary>
public sealed class SemanticKernelEmbeddingServiceTests
{
    [Fact]
    public async Task GenerateEmbeddingsAsync_SplitsIntoSubBatches_PreservingOrder()
    {
        // 150 texts with a batch size of 64 -> 3 calls of 64 / 64 / 22.
        var texts = Enumerable.Range(0, 150).Select(i => $"chunk-{i}").ToList();
        var generator = new RecordingEmbeddingGenerator();
        var sut = new SemanticKernelEmbeddingService(generator, embeddingDimension: 1, maxBatchSize: 64);

        var result = await sut.GenerateEmbeddingsAsync(texts);

        Assert.Equal(new[] { 64, 64, 22 }, generator.BatchSizes);
        Assert.Equal(150, result.Count);

        // The first component of each fake vector encodes the global input index,
        // so an aligned, in-order result is index i -> vector[0] == i.
        for (var i = 0; i < result.Count; i++)
        {
            Assert.Equal(i, (int)result[i].Span[0]);
        }
    }

    [Fact]
    public async Task GenerateEmbeddingsAsync_FewerThanBatchSize_MakesSingleCall()
    {
        var texts = Enumerable.Range(0, 10).Select(i => $"chunk-{i}").ToList();
        var generator = new RecordingEmbeddingGenerator();
        var sut = new SemanticKernelEmbeddingService(generator, embeddingDimension: 1, maxBatchSize: 64);

        var result = await sut.GenerateEmbeddingsAsync(texts);

        Assert.Equal(new[] { 10 }, generator.BatchSizes);
        Assert.Equal(10, result.Count);
    }

    [Fact]
    public void Constructor_RejectsNonPositiveBatchSize()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new SemanticKernelEmbeddingService(new RecordingEmbeddingGenerator(), maxBatchSize: 0));
    }

    /// <summary>
    /// Fake generator that records the size of every batch it receives and returns
    /// one embedding per input whose first component is the input's global index.
    /// </summary>
    private sealed class RecordingEmbeddingGenerator : IEmbeddingGenerator<string, Embedding<float>>
    {
        private int _produced;

        public List<int> BatchSizes { get; } = new();

        public Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(
            IEnumerable<string> values,
            EmbeddingGenerationOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            var batch = values.ToList();
            BatchSizes.Add(batch.Count);

            var embeddings = new GeneratedEmbeddings<Embedding<float>>(
                batch.Select(_ => new Embedding<float>(new float[] { _produced++ })));

            return Task.FromResult(embeddings);
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }
}
