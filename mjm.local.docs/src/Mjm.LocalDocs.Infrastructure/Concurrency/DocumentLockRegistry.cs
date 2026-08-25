using System.Collections.Concurrent;
using Mjm.LocalDocs.Core.Abstractions;

namespace Mjm.LocalDocs.Infrastructure.Concurrency;

/// <summary>
/// In-process per-document mutual exclusion, backed by one semaphore per document id.
/// </summary>
/// <remarks>
/// <para>
/// Register as a singleton. Semaphores are never evicted: the registry holds one small object per
/// document touched since process start, which is a bounded and deliberate cost — evicting safely
/// would require reference counting that buys nothing at this scale.
/// </para>
/// <para>
/// In-process only. It does not coordinate across multiple instances of the application; nothing
/// in this application is deployed that way today, and a distributed lock would need a different
/// abstraction.
/// </para>
/// </remarks>
public sealed class DocumentLockRegistry : IDocumentLockRegistry
{
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new(StringComparer.Ordinal);

    /// <inheritdoc />
    public async Task<IAsyncDisposable> AcquireAsync(
        string documentId,
        CancellationToken cancellationToken = default)
    {
        var gate = _locks.GetOrAdd(documentId, _ => new SemaphoreSlim(1, 1));

        await gate.WaitAsync(cancellationToken);

        return new Release(gate);
    }

    private sealed class Release : IAsyncDisposable
    {
        private readonly SemaphoreSlim _gate;
        private bool _released;

        public Release(SemaphoreSlim gate) => _gate = gate;

        public ValueTask DisposeAsync()
        {
            // Guarded: the SemaphoreSlim is constructed with a max count of 1, so a second
            // Release() call would throw SemaphoreFullException. This guard makes a redundant
            // dispose a safe no-op instead.
            if (!_released)
            {
                _released = true;
                _gate.Release();
            }

            return ValueTask.CompletedTask;
        }
    }
}
