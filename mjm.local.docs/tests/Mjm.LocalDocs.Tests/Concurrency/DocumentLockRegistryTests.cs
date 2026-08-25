using Mjm.LocalDocs.Core.Abstractions;
using Mjm.LocalDocs.Infrastructure.Concurrency;

namespace Mjm.LocalDocs.Tests.Concurrency;

/// <summary>
/// Unit tests for <see cref="DocumentLockRegistry"/>.
/// </summary>
public sealed class DocumentLockRegistryTests
{
    private readonly IDocumentLockRegistry _sut = new DocumentLockRegistry();

    /// <summary>
    /// Helper to await an AcquireAsync call with a bounded timeout.
    /// Fails the test with a clear message if the timeout is exceeded, ensuring the test
    /// fails fast instead of hanging on a regression like a deadlock.
    /// </summary>
    private static async Task<IAsyncDisposable> AwaitWithTimeout(Task<IAsyncDisposable> acquireTask)
    {
        var completed = await Task.WhenAny(acquireTask, Task.Delay(TimeSpan.FromSeconds(5)));
        Assert.True(acquireTask.IsCompleted, "Lock acquisition timed out after 5 seconds; likely deadlock.");

        return await acquireTask;
    }

    [Fact]
    public async Task AcquireAsync_ForOneDocument_BlocksASecondCallerUntilReleased()
    {
        var first = await _sut.AcquireAsync("doc-1");

        var second = _sut.AcquireAsync("doc-1");
        Assert.False(second.IsCompleted);

        await first.DisposeAsync();

        var secondHandle = await AwaitWithTimeout(second);
        await secondHandle.DisposeAsync();
    }

    [Fact]
    public async Task AcquireAsync_ForDifferentDocuments_DoesNotBlock()
    {
        var first = await _sut.AcquireAsync("doc-1");

        // Operations on different documents cannot conflict, so they must not serialise.
        // The lock on doc-2 must be available while doc-1 is still held.
        var second = _sut.AcquireAsync("doc-2");
        Assert.True(second.IsCompleted, "Lock on different document should be immediately available.");

        var secondHandle = await second;
        await secondHandle.DisposeAsync();
        await first.DisposeAsync();
    }

    [Fact]
    public async Task AcquireAsync_AfterRelease_CanBeTakenAgain()
    {
        var first = await AwaitWithTimeout(_sut.AcquireAsync("doc-1"));
        await first.DisposeAsync();

        // After disposal, the same lock must be immediately available.
        var second = _sut.AcquireAsync("doc-1");
        Assert.True(second.IsCompleted, "Lock should be available again after first holder released it.");

        var secondHandle = await second;
        await secondHandle.DisposeAsync();
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
        var third = _sut.AcquireAsync("doc-1");
        var thirdHandle = await AwaitWithTimeout(third);
        await thirdHandle.DisposeAsync();
    }

    [Fact]
    public async Task AcquireAsync_SerialisesConcurrentCallersOnOneDocument()
    {
        var inFlight = 0;
        var maxObserved = 0;

        async Task Contend()
        {
            await using var _ = await AwaitWithTimeout(_sut.AcquireAsync("doc-1"));

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
