using Mjm.LocalDocs.Core.Abstractions;
using Mjm.LocalDocs.Infrastructure.Concurrency;

namespace Mjm.LocalDocs.Tests.Concurrency;

/// <summary>
/// Unit tests for <see cref="DocumentLockRegistry"/>.
/// </summary>
public sealed class DocumentLockRegistryTests
{
    private readonly IDocumentLockRegistry _sut = new DocumentLockRegistry();

    [Fact]
    public async Task AcquireAsync_ForOneDocument_BlocksASecondCallerUntilReleased()
    {
        var first = await _sut.AcquireAsync("doc-1");

        var second = _sut.AcquireAsync("doc-1");
        Assert.False(second.IsCompleted);

        await first.DisposeAsync();

        // Bounded wait: if the release did not hand the lock over, this fails rather than hangs.
        var handedOver = await Task.WhenAny(second, Task.Delay(TimeSpan.FromSeconds(5)));
        Assert.Same(second, handedOver);

        await (await second).DisposeAsync();
    }

    [Fact]
    public async Task AcquireAsync_ForDifferentDocuments_DoesNotBlock()
    {
        var first = await _sut.AcquireAsync("doc-1");

        // Operations on different documents cannot conflict, so they must not serialise.
        var second = await _sut.AcquireAsync("doc-2");

        await second.DisposeAsync();
        await first.DisposeAsync();
    }

    [Fact]
    public async Task AcquireAsync_AfterRelease_CanBeTakenAgain()
    {
        await (await _sut.AcquireAsync("doc-1")).DisposeAsync();
        await (await _sut.AcquireAsync("doc-1")).DisposeAsync();
    }

    [Fact]
    public async Task AcquireAsync_WhenCancelledWhileWaiting_ThrowsAndLeavesTheLockHeld()
    {
        var held = await _sut.AcquireAsync("doc-1");

        using var cts = new CancellationTokenSource();
        var waiting = _sut.AcquireAsync("doc-1", cts.Token);
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waiting);

        // The cancelled waiter must not have consumed the permit the holder still owns.
        await held.DisposeAsync();
        await (await _sut.AcquireAsync("doc-1")).DisposeAsync();
    }

    [Fact]
    public async Task AcquireAsync_SerialisesConcurrentCallersOnOneDocument()
    {
        var inFlight = 0;
        var maxObserved = 0;

        async Task Contend()
        {
            await using var _ = await _sut.AcquireAsync("doc-1");

            var now = Interlocked.Increment(ref inFlight);
            maxObserved = Math.Max(maxObserved, now);
            await Task.Delay(10);
            Interlocked.Decrement(ref inFlight);
        }

        await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Contend()));

        // The whole point: never two at once inside the guarded region.
        Assert.Equal(1, maxObserved);
    }
}
