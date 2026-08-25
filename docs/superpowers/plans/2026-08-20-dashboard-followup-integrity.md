# Dashboard Follow-Up — Version-State Integrity — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Close the six findings the whole-branch review raised against `feature/dashboard`, so the branch actually keeps the promises its own spec makes: no document state that the dashboard cannot see and no repair action that cannot reach, and no repair button that reports success without repairing.

**Architecture:** Six small changes to code the dashboard work already touched. `DocumentService` gains one private `StripIndexAsync` so the delete-chunks/delete-embeddings pair has a single home and a single order; `UpdateDocumentAsync` is reordered to strip before superseding; `ReindexDocumentAsync` reports how much it indexed and refuses a document that has an active child; `IDocumentRepository` gains two derived reads so an interrupted update becomes visible; and the health panel grows a second category with its own repair action.

**Tech Stack:** .NET 10, C#, Blazor Server (InteractiveServer), MudBlazor 8.15.0, EF Core 10 (SQLite + SQL Server), xUnit v3 + NSubstitute.

**Predecessor:** `docs/superpowers/plans/2026-08-20-dashboard-know-how-growth.md` (11 tasks, complete). Its spec is `docs/superpowers/specs/2026-08-20-dashboard-know-how-growth-design.md`.

## Global Constraints

- **Solution root:** `C:\Projects\mjm.local.docs\mjm.local.docs` (nested). **All `dotnet` commands and every `src/...` / `tests/...` path below is relative to that folder.** This plan lives in the outer repo under `docs/superpowers/plans/`.
- **Branch:** `feature/dashboard` (already checked out, 11 tasks of the predecessor plan committed).
- **Target framework:** `net10.0`; `Nullable` and `ImplicitUsings` enabled.
- **No EF migration, no schema change.** Both new signals are *derived* from `ParentDocumentId` and `IsSuperseded`. This is the same hard constraint as the predecessor plan, and it is the reason the interrupted-update state was chosen over a persisted status column in the first place.
- **No new NuGet packages.**
- **Commits:** concise messages matching repo style. **NEVER** add a `Co-Authored-By` trailer (user global instruction — overrides any default).
- **`DateTimeOffset` does not translate on the SQLite provider** — relational comparisons, `Max`/`Min` and `ORDER BY` all throw. Nothing in this plan compares dates in SQL; if a task tempts you to, stop and report.
- **UI language is English.**
- **No bUnit in this repo.** The one UI task is verified by `dotnet build` plus the manual check written into it.
- **Baseline before Task 1:** full suite 258 passed / 17 skipped (SQL Server, no server reachable) / 0 failed.
- **The strip order is `vector` then `chunks`, everywhere.** Established in Task 1 and not revisited: see that task for why, and do not reintroduce a second order.

---

## File Structure

**Modified:**

| File | Change |
|---|---|
| `src/Mjm.LocalDocs.Core/Services/DocumentService.cs` | `StripIndexAsync` private; `UpdateDocumentAsync` reordered; `ReindexDocumentAsync` returns `int` and refuses an active child |
| `src/Mjm.LocalDocs.Core/Abstractions/IDocumentRepository.cs` | +2 derived reads; one corrected doc comment |
| `src/Mjm.LocalDocs.Infrastructure/Persistence/Repositories/EfCoreDocumentRepository.cs` | Implement +2 |
| `src/Mjm.LocalDocs.Infrastructure/VectorStore/InMemoryDocumentRepository.cs` | Implement +2 |
| `src/Mjm.LocalDocs.Core/Models/Dashboard/DashboardReadModels.cs` | +1 record, `IndexHealth` gains a list |
| `src/Mjm.LocalDocs.Core/Services/DashboardMetricsService.cs` | Populate the new list |
| `src/Mjm.LocalDocs.Server/Components/Dashboard/IndexHealthPanel.razor` | Second category + repair action; truthful zero-chunk message |
| `tests/Mjm.LocalDocs.Tests/Services/DocumentServiceIndexingTests.cs` | New cases |
| `tests/Mjm.LocalDocs.Tests/Services/DashboardMetricsServiceTests.cs` | New cases |
| `tests/Mjm.LocalDocs.Tests/Repositories/DocumentRepositoryAggregateTests.cs` | New cases (abstract base — both fixtures inherit them) |
| `docs/superpowers/specs/2026-08-20-dashboard-know-how-growth-design.md` | Correct the fast-path claim |

---

## Task 1: One strip helper, one order, and fix the update that hides a document

**Files:**
- Modify: `src/Mjm.LocalDocs.Core/Services/DocumentService.cs`
- Test: `tests/Mjm.LocalDocs.Tests/Services/DocumentServiceIndexingTests.cs`

**Interfaces:**
- Consumes: nothing new.
- Produces: `private Task StripIndexAsync(string documentId, CancellationToken cancellationToken)`. Tasks 2 and 4 leave it alone; nothing outside `DocumentService` sees it.

**Why this task exists.** The delete-chunks/delete-embeddings pair currently appears at four sites in three different orders, and two of the comments argue opposite sides of the same question. Worse, `UpdateDocumentAsync` supersedes the old version *before* stripping its index — so an interruption between those two steps leaves a **superseded document that kept its chunks and embeddings**. That state is invisible to `GetActiveDocumentChunkTalliesAsync` (active documents only), invisible to the fast path (both totals still agree), and unrepairable, because `ReindexDocumentAsync` refuses superseded documents by design. It is the only state on the branch that neither detection nor repair can reach, and it sits in the path every real update takes.

**Which order, and why the existing comment is wrong.** The reindex pre-wipe comment claims chunks must go first because deleting embeddings first "would leave chunk rows behind, and a document with chunks reads as healthy while being unreachable." That premise is false: a document with chunks and no embeddings has `ChunkCount > 0`, so it *is* a probe candidate, and the probe finds every one of its chunk ids missing and reports it broken. Both orders are detectable. The real difference is what each leaves behind — chunks-first orphans the embeddings, and orphaned embeddings make `CountChunksAsync()` and `IVectorStore.CountAsync()` permanently unequal, which costs the fast path forever. So: **vector first, then chunks**, everywhere, and the misleading comment goes.

- [ ] **Step 1: Write the failing tests**

Append to `tests/Mjm.LocalDocs.Tests/Services/DocumentServiceIndexingTests.cs`:

```csharp
    [Fact]
    public async Task UpdateDocumentAsync_StripsThePreviousIndexBeforeSupersedingIt()
    {
        var existing = CreateDocument("doc-1");
        var newVersion = CreateDocument("doc-2", parentDocumentId: "doc-1");

        _repository.GetDocumentAsync("doc-1", Arg.Any<CancellationToken>()).Returns(existing);
        _repository.AddDocumentAsync(Arg.Any<Document>(), Arg.Any<CancellationToken>()).Returns(newVersion);
        GivenChunks("doc-2", 1);
        _embeddingService.GenerateEmbeddingsAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>())
            .Returns([new float[] { 0.1f }]);

        await _sut.UpdateDocumentAsync("doc-1", newVersion);

        // Superseding first would leave a superseded document that kept its index if the deletes
        // never ran: invisible to the active-only tallies, invisible to the count comparison, and
        // unrepairable, since reindexing a superseded document is refused by design.
        Received.InOrder(() =>
        {
            _vectorStore.DeleteByDocumentIdAsync("doc-1", Arg.Any<CancellationToken>());
            _repository.DeleteChunksByDocumentAsync("doc-1", Arg.Any<CancellationToken>());
            _repository.SupersedeDocumentAsync("doc-1", Arg.Any<CancellationToken>());
        });
    }

    [Fact]
    public async Task UpdateDocumentAsync_WhenStrippingThePreviousIndexFails_LeavesItActiveAndRepairable()
    {
        var existing = CreateDocument("doc-1");
        var newVersion = CreateDocument("doc-2", parentDocumentId: "doc-1");

        _repository.GetDocumentAsync("doc-1", Arg.Any<CancellationToken>()).Returns(existing);
        _repository.AddDocumentAsync(Arg.Any<Document>(), Arg.Any<CancellationToken>()).Returns(newVersion);
        GivenChunks("doc-2", 1);
        _embeddingService.GenerateEmbeddingsAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>())
            .Returns([new float[] { 0.1f }]);
        _repository.DeleteChunksByDocumentAsync("doc-1", Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("db went away"));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _sut.UpdateDocumentAsync("doc-1", newVersion));

        // The old version must still be active, so the interrupted update stays derivable and a
        // later reindex of the new version can still close it.
        await _repository.DidNotReceive().SupersedeDocumentAsync("doc-1", Arg.Any<CancellationToken>());
    }
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/Mjm.LocalDocs.Tests/Mjm.LocalDocs.Tests.csproj --filter "FullyQualifiedName~DocumentServiceIndexingTests"`
Expected: FAIL. `UpdateDocumentAsync_StripsThePreviousIndexBeforeSupersedingIt` fails on the `Received.InOrder` assertion, because supersede currently runs first.

- [ ] **Step 3: Add the helper**

In `DocumentService.cs`, add next to the other privates:

```csharp
    /// <summary>
    /// Removes a document's chunks and their embeddings, leaving the document row itself intact.
    /// </summary>
    /// <remarks>
    /// The single home for this pair, and the single decision about its order. Embeddings go
    /// first: an interruption then leaves chunk rows without embeddings, which the health probe
    /// reports as broken and a reindex repairs. Deleting chunks first would instead orphan the
    /// embeddings, and orphans make the chunk and embedding totals permanently unequal, which
    /// costs the health check's fast path on every dashboard load from then on.
    /// </remarks>
    private async Task StripIndexAsync(string documentId, CancellationToken cancellationToken)
    {
        await _vectorStore.DeleteByDocumentIdAsync(documentId, cancellationToken);
        await _repository.DeleteChunksByDocumentAsync(documentId, cancellationToken);
    }
```

- [ ] **Step 4: Route the three cancellable sites through it**

In `UpdateDocumentAsync`, replace steps 3 and 4 — the `SupersedeDocumentAsync` call followed by the two deletes — with:

```csharp
        // 3. Strip the previous version's index BEFORE retiring it. Superseding first would, on
        //    an interruption, leave a superseded document that kept its chunks: absent from the
        //    active-only tallies, invisible to the count comparison, and refused by reindex.
        await StripIndexAsync(existingDocumentId, cancellationToken);

        // 4. Retire the previous version.
        await _repository.SupersedeDocumentAsync(existingDocumentId, cancellationToken);
```

In `ReindexDocumentAsync`, replace the two pre-wipe lines and their comment with:

```csharp
            // Clean slate so a retry after a partial failure cannot duplicate chunks. Inside the
            // try, so a failure here is compensated and wrapped rather than escaping raw.
            await StripIndexAsync(documentId, cancellationToken);
```

In `CloseInterruptedUpdateAsync`, replace the two delete calls with:

```csharp
            await StripIndexAsync(parent.Id, CancellationToken.None);
```

Leave `DiscardPartialIndexAsync` exactly as it is. It deliberately guards each deletion independently and swallows both, because it runs while an exception is already in flight and must never mask it — that is a different contract from the helper, and collapsing them would lose it.

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test tests/Mjm.LocalDocs.Tests/Mjm.LocalDocs.Tests.csproj --filter "FullyQualifiedName~DocumentService"`
Expected: PASS. Note that `ReindexDocumentAsync_WipesBeforeRebuildingSoRetriesAreIdempotent` and the closure-order test still pass unchanged — the helper preserves both behaviours.

- [ ] **Step 6: Commit**

```bash
git add src/Mjm.LocalDocs.Core/Services/DocumentService.cs tests/Mjm.LocalDocs.Tests/Services/DocumentServiceIndexingTests.cs
git commit -m "Strip a document's index before superseding it"
```

---

## Task 2: A reindex that produced nothing must not claim success

**Files:**
- Modify: `src/Mjm.LocalDocs.Core/Services/DocumentService.cs`
- Modify: `src/Mjm.LocalDocs.Server/Components/Dashboard/IndexHealthPanel.razor`
- Test: `tests/Mjm.LocalDocs.Tests/Services/DocumentServiceIndexingTests.cs`

**Interfaces:**
- Consumes: `StripIndexAsync` (Task 1).
- Produces: `ReindexDocumentAsync` returns `Task<int>` — the number of chunks indexed. Task 4 keeps that signature.

**Why this task exists.** `IndexDocumentAsync` returns 0 when the extracted text yields no chunks: not a failure, but not a searchable document either. `ReindexDocumentAsync` already refuses to close an interrupted update in that case, but it returns normally, so the panel prints "*X* is searchable again" and then lists X as broken again on the very next refresh — forever. `SimpleDocumentProcessor` returns zero chunks for whitespace-only text and neither upload path rejects empty extraction, so an image-only PDF lands here by the ordinary route. A repair button that always claims to have fixed things is worse than no button.

- [ ] **Step 1: Write the failing test**

Append to `DocumentServiceIndexingTests.cs`:

```csharp
    [Fact]
    public async Task ReindexDocumentAsync_ReturnsTheNumberOfChunksItIndexed()
    {
        _repository.GetDocumentAsync("doc-1", Arg.Any<CancellationToken>()).Returns(CreateDocument());
        GivenChunks("doc-1", 3);
        _embeddingService.GenerateEmbeddingsAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>())
            .Returns([new float[] { 0.1f }, new float[] { 0.2f }, new float[] { 0.3f }]);

        var indexed = await _sut.ReindexDocumentAsync("doc-1");

        Assert.Equal(3, indexed);
    }

    [Fact]
    public async Task ReindexDocumentAsync_WhenTextYieldsNoChunks_ReturnsZeroRatherThanReportingSuccess()
    {
        _repository.GetDocumentAsync("doc-1", Arg.Any<CancellationToken>()).Returns(CreateDocument());
        // Extracted text that produces nothing — an image-only PDF, for instance.
        GivenChunks("doc-1", 0);

        var indexed = await _sut.ReindexDocumentAsync("doc-1");

        // Zero is how the caller learns the document is still not searchable, so it can say so
        // instead of claiming a repair that did not happen.
        Assert.Equal(0, indexed);
    }
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test tests/Mjm.LocalDocs.Tests/Mjm.LocalDocs.Tests.csproj --filter "FullyQualifiedName~ReindexDocumentAsync_Returns"`
Expected: BUILD FAILURE — cannot assign `void`/`Task` to `var indexed`.

- [ ] **Step 3: Return the count**

Change `ReindexDocumentAsync`'s signature and its two return points:

```csharp
    public async Task<int> ReindexDocumentAsync(
        string documentId,
        CancellationToken cancellationToken = default)
```

Add to its XML docs, above the existing `<exception>` lines:

```csharp
    /// <returns>
    /// The number of chunks indexed. Zero means the document's extracted text produced nothing
    /// to index — the reindex did not fail, but the document is still not searchable and no
    /// amount of retrying will change that. Callers must not report zero as a repair.
    /// </returns>
```

Then replace the zero-chunk early return with `return 0;` and end the method with `return chunkCount;`:

```csharp
        // Only a reindex that actually produced an index may close the update. Extracted text
        // that yields no chunks does not throw, but it does not produce a searchable document
        // either — and superseding the parent here would strip the chain's only working index,
        // on the very button the dashboard offers for zero-chunk documents.
        if (chunkCount == 0)
            return 0;

        await CloseInterruptedUpdateAsync(document);

        return chunkCount;
```

- [ ] **Step 4: Tell the truth in the panel**

In `IndexHealthPanel.razor`, replace the success toast inside `ReindexAsync` with:

```csharp
            if (indexed == 0)
            {
                Snackbar.Add(
                    $"{document.FileName} has no extractable text, so reindexing cannot make it searchable.",
                    Severity.Warning);
            }
            else
            {
                Snackbar.Add($"{document.FileName} is searchable again.", Severity.Success);
            }
```

and capture the result at the call site:

```csharp
            int indexed;
            try
            {
                indexed = await Documents.ReindexDocumentAsync(document.DocumentId);
            }
```

Everything else in the handler — the `_busy` guard, both catch clauses with their early returns, the separate refresh handler, the `finally` — stays exactly as it is.

- [ ] **Step 5: Run the tests and build**

Run: `dotnet test tests/Mjm.LocalDocs.Tests/Mjm.LocalDocs.Tests.csproj --filter "FullyQualifiedName~DocumentService"`
Expected: PASS.

Run: `dotnet build`
Expected: 0 errors. Grep the output for `IndexHealthPanel.razor` and report what you find.

- [ ] **Step 6: Commit**

```bash
git add src/Mjm.LocalDocs.Core/Services/DocumentService.cs src/Mjm.LocalDocs.Server/Components/Dashboard/IndexHealthPanel.razor tests/Mjm.LocalDocs.Tests/Services/DocumentServiceIndexingTests.cs
git commit -m "Report a zero-chunk reindex as unfixable rather than repaired"
```

---

## Task 3: Derive the interrupted-update signal the spec promised

**Files:**
- Modify: `src/Mjm.LocalDocs.Core/Models/Dashboard/DashboardReadModels.cs`
- Modify: `src/Mjm.LocalDocs.Core/Abstractions/IDocumentRepository.cs`
- Modify: `src/Mjm.LocalDocs.Infrastructure/Persistence/Repositories/EfCoreDocumentRepository.cs`
- Modify: `src/Mjm.LocalDocs.Infrastructure/VectorStore/InMemoryDocumentRepository.cs`
- Modify: `src/Mjm.LocalDocs.Core/Services/DashboardMetricsService.cs`
- Test: `tests/Mjm.LocalDocs.Tests/Repositories/DocumentRepositoryAggregateTests.cs`
- Test: `tests/Mjm.LocalDocs.Tests/Services/DashboardMetricsServiceTests.cs`

**Interfaces:**
- Consumes: nothing from Tasks 1-2.
- Produces: `record InterruptedUpdate(string DocumentId, string FileName, string ParentDocumentId, string ParentFileName)`; `IDocumentRepository.GetInterruptedUpdatesAsync(CancellationToken) → Task<IReadOnlyList<InterruptedUpdate>>`; `IndexHealth` gains a fourth member `IReadOnlyList<InterruptedUpdate> InterruptedUpdates`. Tasks 4 and 5 consume both.

**Why this task exists.** The spec justified leaving a failed update pending — rather than superseding anyway, or discarding the new version — precisely on the grounds that the resulting state is derivable: "a document whose `ParentDocumentId` points at a still-active document *is* an interrupted update — detectable with no new column." Nothing derives it. So an update interrupted *after* the new version indexed successfully — a failed supersede, an app-pool recycle, a dropped connection — leaves both versions active and both indexed, answering the same query, with no signal anywhere and nothing prompting a repair. Only the subset of interrupted updates that also broke the index is currently visible. This task builds the read the design was sold on.

- [ ] **Step 1: Add the record and extend `IndexHealth`**

In `DashboardReadModels.cs`, add after `DocumentChunkTally`:

```csharp
/// <summary>
/// An update that never completed: a document that is still active even though a newer version
/// of it is also active. Both answer the same searches until the update is closed.
/// </summary>
/// <param name="DocumentId">The newer version, still active.</param>
/// <param name="FileName">The newer version's file name, for display.</param>
/// <param name="ParentDocumentId">The older version, which should have been retired.</param>
/// <param name="ParentFileName">The older version's file name, for display.</param>
public sealed record InterruptedUpdate(
    string DocumentId,
    string FileName,
    string ParentDocumentId,
    string ParentFileName);
```

Then extend `IndexHealth` — add the member and document it:

```csharp
/// <param name="InterruptedUpdates">
/// Updates that never completed. Distinct from <paramref name="Broken"/>: these documents are
/// perfectly searchable, which is the problem — so is the older version they were meant to
/// replace, and both answer the same query.
/// </param>
public sealed record IndexHealth(
    int ActiveDocuments,
    int FullyIndexed,
    IReadOnlyList<DocumentChunkTally> Broken,
    IReadOnlyList<InterruptedUpdate> InterruptedUpdates);
```

- [ ] **Step 2: Write the failing repository tests**

Append to the abstract base `tests/Mjm.LocalDocs.Tests/Repositories/DocumentRepositoryAggregateTests.cs` — both fixtures inherit them, so nothing is added to either subclass:

```csharp
    [Fact]
    public async Task GetInterruptedUpdatesAsync_WhenTheParentWasRetired_ReturnsNothing()
    {
        await SeedDocumentAsync("doc-1", isSuperseded: true);
        await SeedDocumentAsync("doc-2", parentDocumentId: "doc-1");

        var interrupted = await Sut.GetInterruptedUpdatesAsync();

        Assert.Empty(interrupted);
    }

    [Fact]
    public async Task GetInterruptedUpdatesAsync_WhenBothVersionsAreActive_ReturnsThePair()
    {
        await SeedDocumentAsync("doc-1");
        await SeedDocumentAsync("doc-2", parentDocumentId: "doc-1");

        var interrupted = await Sut.GetInterruptedUpdatesAsync();

        var pair = Assert.Single(interrupted);
        Assert.Equal("doc-2", pair.DocumentId);
        Assert.Equal("doc-1", pair.ParentDocumentId);
        Assert.Equal("doc-2.txt", pair.FileName);
        Assert.Equal("doc-1.txt", pair.ParentFileName);
    }

    [Fact]
    public async Task GetInterruptedUpdatesAsync_IgnoresADocumentWithNoParent()
    {
        await SeedDocumentAsync("doc-1");

        var interrupted = await Sut.GetInterruptedUpdatesAsync();

        Assert.Empty(interrupted);
    }

    [Fact]
    public async Task GetInterruptedUpdatesAsync_IgnoresASupersededChild()
    {
        // A retired child whose parent is somehow still active is not an interrupted update the
        // reindex path can close, so it must not be offered as one.
        await SeedDocumentAsync("doc-1");
        await SeedDocumentAsync("doc-2", parentDocumentId: "doc-1", isSuperseded: true);

        var interrupted = await Sut.GetInterruptedUpdatesAsync();

        Assert.Empty(interrupted);
    }

    [Fact]
    public async Task GetInterruptedUpdatesAsync_ReportsEachLinkOfADoublyInterruptedChain()
    {
        await SeedDocumentAsync("doc-1");
        await SeedDocumentAsync("doc-2", parentDocumentId: "doc-1");
        await SeedDocumentAsync("doc-3", parentDocumentId: "doc-2");

        var interrupted = await Sut.GetInterruptedUpdatesAsync();

        Assert.Equal(2, interrupted.Count);
        Assert.Contains(interrupted, i => i.DocumentId == "doc-2" && i.ParentDocumentId == "doc-1");
        Assert.Contains(interrupted, i => i.DocumentId == "doc-3" && i.ParentDocumentId == "doc-2");
    }
```

- [ ] **Step 3: Run the tests to verify they fail**

Run: `dotnet test tests/Mjm.LocalDocs.Tests/Mjm.LocalDocs.Tests.csproj --filter "FullyQualifiedName~DocumentRepositoryAggregateTests"`
Expected: BUILD FAILURE — `'IDocumentRepository' does not contain a definition for 'GetInterruptedUpdatesAsync'`.

- [ ] **Step 4: Add the interface member**

In `IDocumentRepository.cs`, append inside the existing `#region Dashboard Aggregates`:

```csharp
    /// <summary>
    /// Gets every update that never completed: an active document whose
    /// <see cref="Document.ParentDocumentId"/> names another document that is also still active.
    /// </summary>
    /// <remarks>
    /// Derived rather than stored, which is what allowed a failed update to leave the previous
    /// version searchable instead of retiring it blind. Both versions answer the same query until
    /// the update is closed, so this is a correctness signal, not a tidiness one.
    /// </remarks>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>One record per unclosed parent-child link, in no guaranteed order.</returns>
    Task<IReadOnlyList<InterruptedUpdate>> GetInterruptedUpdatesAsync(
        CancellationToken cancellationToken = default);
```

- [ ] **Step 5: Implement in `EfCoreDocumentRepository`**

Append inside its `#region Dashboard Aggregates`:

```csharp
    /// <inheritdoc />
    public async Task<IReadOnlyList<InterruptedUpdate>> GetInterruptedUpdatesAsync(
        CancellationToken cancellationToken = default)
    {
        // A self-join over the two flags. Only four scalar columns are projected, so this never
        // touches ExtractedText or FileContent.
        return await _context.Documents
            .AsNoTracking()
            .Where(child => !child.IsSuperseded && child.ParentDocumentId != null)
            .Join(
                _context.Documents.AsNoTracking().Where(parent => !parent.IsSuperseded),
                child => child.ParentDocumentId,
                parent => parent.Id,
                (child, parent) => new InterruptedUpdate(
                    child.Id,
                    child.FileName,
                    parent.Id,
                    parent.FileName))
            .ToListAsync(cancellationToken);
    }
```

- [ ] **Step 6: Implement in `InMemoryDocumentRepository`**

Append inside its `#region Dashboard Aggregates`:

```csharp
    /// <inheritdoc />
    public Task<IReadOnlyList<InterruptedUpdate>> GetInterruptedUpdatesAsync(
        CancellationToken cancellationToken = default)
    {
        var active = _documents.Values
            .Where(d => !d.IsSuperseded)
            .ToDictionary(d => d.Id, StringComparer.Ordinal);

        var interrupted = active.Values
            .Where(child => child.ParentDocumentId is not null
                            && active.ContainsKey(child.ParentDocumentId))
            .Select(child => new InterruptedUpdate(
                child.Id,
                child.FileName,
                child.ParentDocumentId!,
                active[child.ParentDocumentId!].FileName))
            .ToList();

        return Task.FromResult<IReadOnlyList<InterruptedUpdate>>(interrupted);
    }
```

- [ ] **Step 7: Write the failing service test**

Append to `tests/Mjm.LocalDocs.Tests/Services/DashboardMetricsServiceTests.cs`:

```csharp
    [Fact]
    public async Task GetIndexHealthAsync_CarriesInterruptedUpdates()
    {
        _repository.GetActiveDocumentChunkTalliesAsync(Arg.Any<CancellationToken>())
            .Returns([Tally("doc-1", 2), Tally("doc-2", 2)]);
        _repository.CountChunksAsync(Arg.Any<CancellationToken>()).Returns(4L);
        _vectorStore.CountAsync(Arg.Any<CancellationToken>()).Returns(4L);
        _repository.GetInterruptedUpdatesAsync(Arg.Any<CancellationToken>())
            .Returns([new InterruptedUpdate("doc-2", "doc-2.txt", "doc-1", "doc-1.txt")]);

        var health = await CreateSut().GetIndexHealthAsync();

        // Both documents are perfectly indexed — that is exactly why this needs its own list
        // rather than being folded into Broken.
        Assert.Empty(health.Broken);
        Assert.Equal("doc-2", Assert.Single(health.InterruptedUpdates).DocumentId);
    }
```

- [ ] **Step 8: Populate the list in the service**

In `DashboardMetricsService.GetIndexHealthAsync`, read the new signal alongside the tallies and pass it into the result. Add near the top of the method:

```csharp
        var interrupted = await _repository.GetInterruptedUpdatesAsync(cancellationToken);
```

and change the return statement to:

```csharp
        return new IndexHealth(activeCount, activeCount - ordered.Count, ordered, interrupted);
```

Note that this read is unconditional — it is one cheap self-join, and unlike the embedding probe it is not gated on the fast path, because there is no cheap pre-check that could tell you whether any unclosed link exists.

- [ ] **Step 9: Run the tests to verify they pass**

Run: `dotnet test`
Expected: PASS. Every existing construction of `IndexHealth` needs the fourth argument — the compiler will find them; pass an empty list where a test does not care about the new signal, and do not weaken any existing assertion to accommodate it.

- [ ] **Step 10: Commit**

```bash
git add src/Mjm.LocalDocs.Core/Models/Dashboard/DashboardReadModels.cs src/Mjm.LocalDocs.Core/Abstractions/IDocumentRepository.cs src/Mjm.LocalDocs.Infrastructure src/Mjm.LocalDocs.Core/Services/DashboardMetricsService.cs tests/Mjm.LocalDocs.Tests
git commit -m "Derive updates that never completed"
```

---

## Task 4: Reindex refuses a document that has a newer active version

**Files:**
- Modify: `src/Mjm.LocalDocs.Core/Abstractions/IDocumentRepository.cs`
- Modify: `src/Mjm.LocalDocs.Infrastructure/Persistence/Repositories/EfCoreDocumentRepository.cs`
- Modify: `src/Mjm.LocalDocs.Infrastructure/VectorStore/InMemoryDocumentRepository.cs`
- Modify: `src/Mjm.LocalDocs.Core/Services/DocumentService.cs`
- Test: `tests/Mjm.LocalDocs.Tests/Repositories/DocumentRepositoryAggregateTests.cs`
- Test: `tests/Mjm.LocalDocs.Tests/Services/DocumentServiceIndexingTests.cs`

**Interfaces:**
- Consumes: `ReindexDocumentAsync`'s `Task<int>` signature (Task 2).
- Produces: `IDocumentRepository.HasActiveChildAsync(string documentId, CancellationToken) → Task<bool>`.

**Why this task exists.** `ReindexDocumentAsync` closes the chain *upward*, retiring still-active ancestors. It has no notion of an active *descendant*. In the state Task 3 now surfaces — v1 and v2 both active — reindexing **v1** rebuilds v1's index and walks v1's ancestors, of which there are none. Both versions stay active and indexed. So the repair affordance can re-arm the very duplicate it exists to remove, silently. The fix is to refuse: the caller should reindex the newer version, which closes the chain properly.

- [ ] **Step 1: Write the failing tests**

Append to the abstract base `DocumentRepositoryAggregateTests.cs`:

```csharp
    [Fact]
    public async Task HasActiveChildAsync_WithAnActiveNewerVersion_ReturnsTrue()
    {
        await SeedDocumentAsync("doc-1");
        await SeedDocumentAsync("doc-2", parentDocumentId: "doc-1");

        Assert.True(await Sut.HasActiveChildAsync("doc-1"));
    }

    [Fact]
    public async Task HasActiveChildAsync_WhenTheNewerVersionWasRetired_ReturnsFalse()
    {
        await SeedDocumentAsync("doc-1");
        await SeedDocumentAsync("doc-2", parentDocumentId: "doc-1", isSuperseded: true);

        Assert.False(await Sut.HasActiveChildAsync("doc-1"));
    }

    [Fact]
    public async Task HasActiveChildAsync_WithNoChildAtAll_ReturnsFalse()
    {
        await SeedDocumentAsync("doc-1");

        Assert.False(await Sut.HasActiveChildAsync("doc-1"));
    }
```

Append to `DocumentServiceIndexingTests.cs`:

```csharp
    [Fact]
    public async Task ReindexDocumentAsync_RefusesADocumentThatHasANewerActiveVersion()
    {
        _repository.GetDocumentAsync("doc-1", Arg.Any<CancellationToken>()).Returns(CreateDocument("doc-1"));
        _repository.HasActiveChildAsync("doc-1", Arg.Any<CancellationToken>()).Returns(true);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _sut.ReindexDocumentAsync("doc-1"));

        // Rebuilding this one would leave two active indexed versions answering the same query:
        // the caller has to reindex the newer version, which closes the chain.
        await _repository.DidNotReceive().DeleteChunksByDocumentAsync("doc-1", Arg.Any<CancellationToken>());
        await _vectorStore.DidNotReceive().DeleteByDocumentIdAsync("doc-1", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ReindexDocumentAsync_WithNoActiveChild_ProceedsNormally()
    {
        _repository.GetDocumentAsync("doc-1", Arg.Any<CancellationToken>()).Returns(CreateDocument("doc-1"));
        _repository.HasActiveChildAsync("doc-1", Arg.Any<CancellationToken>()).Returns(false);
        GivenChunks("doc-1", 1);
        _embeddingService.GenerateEmbeddingsAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>())
            .Returns([new float[] { 0.1f }]);

        var indexed = await _sut.ReindexDocumentAsync("doc-1");

        Assert.Equal(1, indexed);
    }
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/Mjm.LocalDocs.Tests/Mjm.LocalDocs.Tests.csproj --filter "FullyQualifiedName~HasActiveChildAsync"`
Expected: BUILD FAILURE — `'IDocumentRepository' does not contain a definition for 'HasActiveChildAsync'`.

- [ ] **Step 3: Add the interface member**

Append inside `IDocumentRepository.cs`'s `#region Dashboard Aggregates`:

```csharp
    /// <summary>
    /// Checks whether an active document names the given document as its parent.
    /// </summary>
    /// <param name="documentId">The candidate parent.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True when a newer version of this document is still active.</returns>
    Task<bool> HasActiveChildAsync(
        string documentId,
        CancellationToken cancellationToken = default);
```

- [ ] **Step 4: Implement in both repositories**

`EfCoreDocumentRepository`:

```csharp
    /// <inheritdoc />
    public Task<bool> HasActiveChildAsync(
        string documentId,
        CancellationToken cancellationToken = default)
    {
        return _context.Documents
            .AsNoTracking()
            .AnyAsync(d => !d.IsSuperseded && d.ParentDocumentId == documentId, cancellationToken);
    }
```

`InMemoryDocumentRepository`:

```csharp
    /// <inheritdoc />
    public Task<bool> HasActiveChildAsync(
        string documentId,
        CancellationToken cancellationToken = default)
    {
        var hasChild = _documents.Values.Any(d =>
            !d.IsSuperseded
            && string.Equals(d.ParentDocumentId, documentId, StringComparison.Ordinal));

        return Task.FromResult(hasChild);
    }
```

- [ ] **Step 5: Refuse in `ReindexDocumentAsync`**

Add immediately after the existing superseded check, before any destructive call:

```csharp
        if (await _repository.HasActiveChildAsync(documentId, cancellationToken))
        {
            throw new InvalidOperationException(
                $"Document '{documentId}' has a newer version that is still active. " +
                "Reindex that newer version instead — doing so also retires this one.");
        }
```

Both guards must precede the strip. A refused reindex deletes nothing.

- [ ] **Step 6: Run the tests to verify they pass**

Run: `dotnet test`
Expected: PASS. Existing reindex tests that do not stub `HasActiveChildAsync` still pass, because an NSubstitute `Task<bool>` defaults to `false`.

- [ ] **Step 7: Commit**

```bash
git add src/Mjm.LocalDocs.Core src/Mjm.LocalDocs.Infrastructure tests/Mjm.LocalDocs.Tests
git commit -m "Refuse to reindex a version that has been superseded in fact"
```

---

## Task 5: Surface interrupted updates in the health panel

**Files:**
- Modify: `src/Mjm.LocalDocs.Server/Components/Dashboard/IndexHealthPanel.razor`

**Interfaces:**
- Consumes: `IndexHealth.InterruptedUpdates` (Task 3); `ReindexDocumentAsync` returning `Task<int>` (Task 2).
- Produces: nothing.

**Why this task exists.** Task 3 derives the signal; without this task nothing shows it. The repair is the existing reindex of the *newer* version, which rebuilds it and then retires every still-active ancestor — so the action already exists and just needs an entry point.

There are no automated tests for this file; the repo has no component test framework. Verification is `dotnet build` plus the manual check in Step 3.

- [ ] **Step 1: Add the section**

In `IndexHealthPanel.razor`, after the broken-documents block and inside the same `else` branch, add:

```razor
        @if (Health.InterruptedUpdates.Count > 0)
        {
            <MudText Style="font-size: 0.75rem; font-weight: 600; color: var(--ld-text-primary); margin: 12px 0 6px;">
                Updates that never finished
            </MudText>
            <MudText Style="font-size: 0.6875rem; color: var(--ld-text-muted); margin-bottom: 6px;">
                Both versions are searchable, so both answer the same query.
            </MudText>

            @foreach (var update in Health.InterruptedUpdates)
            {
                <div class="d-flex align-center justify-space-between" style="gap: 8px; padding: 6px 0; border-top: 1px solid var(--ld-border);">
                    <MudTooltip Text="@($"{update.FileName} should have replaced {update.ParentFileName}")">
                        <MudText Style="font-size: 0.75rem; color: var(--ld-text-secondary); overflow: hidden; text-overflow: ellipsis; white-space: nowrap; max-width: 150px;">
                            @update.FileName
                        </MudText>
                    </MudTooltip>
                    <MudButton Variant="Variant.Text"
                               Size="Size.Small"
                               Disabled="@_busy"
                               OnClick="@(() => CompleteUpdateAsync(update))">
                        Finish
                    </MudButton>
                </div>
            }
        }
```

The all-clear line's condition must now account for both lists, so change it to:

```razor
        @if (Health.Broken.Count == 0 && Health.InterruptedUpdates.Count == 0)
```

- [ ] **Step 2: Add the handler**

Add to the `@code` block, next to `ReindexAsync`:

```csharp
    private async Task CompleteUpdateAsync(InterruptedUpdate update)
    {
        // Same round-trip race as the other handlers, and the same reason it matters: this calls
        // the wipe-and-rebuild path, which is safe to retry sequentially but not concurrently.
        if (_busy)
            return;

        _busy = true;
        try
        {
            try
            {
                // Reindexing the newer version rebuilds it and then retires every still-active
                // ancestor, which is exactly what closing the update means.
                await Documents.ReindexDocumentAsync(update.DocumentId);
            }
            catch (DocumentIndexingException ex)
            {
                Snackbar.Add(
                    $"Could not finish the update of {update.FileName}: {ex.InnerException?.Message ?? ex.Message}",
                    Severity.Error);
                return;
            }
            catch (Exception ex)
            {
                Snackbar.Add($"Could not finish the update of {update.FileName}: {ex.Message}", Severity.Error);
                return;
            }

            Snackbar.Add($"{update.ParentFileName} has been retired.", Severity.Success);

            try
            {
                var health = await Metrics.GetIndexHealthAsync(forceFullReconciliation: true);
                await HealthChanged.InvokeAsync(health);
            }
            catch (Exception ex)
            {
                Snackbar.Add($"Finished, but the panel could not refresh: {ex.Message}", Severity.Warning);
            }
        }
        finally
        {
            _busy = false;
        }
    }
```

- [ ] **Step 3: Verify**

Run: `dotnet build`
Expected: 0 errors. Grep the output for `IndexHealthPanel.razor` and report what you find.

Manual check, from the solution root. The application's user secrets on the development machine point at SQL Server, Azure Blob and OpenAI, so override them and work against a **copy** of the database — never the real `localdocs.db`:

```bash
cp src/Mjm.LocalDocs.Server/localdocs.db /tmp/verify.db
ASPNETCORE_ENVIRONMENT=Development \
LocalDocs__Storage__Provider=Sqlite \
LocalDocs__Embeddings__Provider=Fake \
LocalDocs__FileStorage__Provider=Database \
ConnectionStrings__LocalDocs="Data Source=/tmp/verify.db" \
dotnet run --project src/Mjm.LocalDocs.Server/Mjm.LocalDocs.Server.csproj --urls http://localhost:5024
```

Then, in the copy, make an interrupted update by hand — pick any document, insert a second row naming it as parent, and leave both active. Log in at `http://localhost:5024` with `admin` / `admin` and confirm: the panel shows "Updates that never finished" with the newer file listed, the all-clear line is gone, clicking **Finish** retires the older version, and the section disappears on refresh.

- [ ] **Step 4: Commit**

```bash
git add src/Mjm.LocalDocs.Server/Components/Dashboard/IndexHealthPanel.razor
git commit -m "Offer to finish an update that never completed"
```

---

## Task 6: Correct the invariant that project deletion falsifies

**Files:**
- Modify: `src/Mjm.LocalDocs.Core/Abstractions/IDocumentRepository.cs`
- Modify: `docs/superpowers/specs/2026-08-20-dashboard-know-how-growth-design.md`

**Interfaces:** none — documentation only.

**Why this task exists.** `CountChunksAsync`'s doc comment states that "In a healthy system this equals `IVectorStore.CountAsync`, because chunks exist only for active documents." That is false by construction: all three project-delete call sites go through `IProjectRepository.DeleteAsync`, which cascades through EF to documents and chunks — but `chunk_embeddings` is not an EF-mapped table, so every project deletion orphans every embedding it held. The real development database carries 36 embeddings for 33 chunks for exactly this reason. Fixing the leak is separate work; shipping a public contract that asserts something untrue is not acceptable either way, and the honest statement is also the one that explains why the design is sound.

Note for whoever picks up the leak: the three call sites are `McpTools/ProjectTools.cs:121`, `Components/Pages/Projects/ProjectList.razor:175`, and `Components/Pages/Projects/ProjectDetail.razor:468`.

- [ ] **Step 1: Correct the interface comment**

In `IDocumentRepository.cs`, replace `CountChunksAsync`'s summary with:

```csharp
    /// <summary>
    /// Counts all persisted chunks.
    /// </summary>
    /// <remarks>
    /// Compared against <see cref="IVectorStore.CountAsync"/> as a cheap pre-check for the
    /// health probe. The two are NOT guaranteed equal even on a healthy knowledge base: deleting
    /// a project cascades through EF to its documents and chunks, but the embedding store is not
    /// an EF table, so its rows are orphaned and the totals diverge permanently. A divergence
    /// therefore means "run the full probe", never "something is broken" — the probe is what
    /// decides. The consequence of orphans is a slower dashboard, never a wrong one.
    /// </remarks>
```

- [ ] **Step 2: Correct the spec**

In `docs/superpowers/specs/2026-08-20-dashboard-know-how-growth-design.md`, find the fast-path section's declared limitation and replace it with:

```markdown
**Declared limitations.** Equal totals over different sets would slip past the fast path — a pathological coincidence, and the panel's "Review" button forces full reconciliation regardless. More importantly, the totals are *not* reliably equal on a healthy knowledge base: deleting a project cascades through EF to its documents and chunks, but `chunk_embeddings` is not an EF-mapped table, so every project deletion orphans the embeddings it held and the two counts diverge permanently. The fast path therefore degrades to "always take the slow route" on any database where a project was ever deleted — correct, just not fast. Fixing the leak is out of scope here; the property that matters is that divergence can only cost performance, never correctness.
```

- [ ] **Step 3: Verify**

Run: `dotnet build`
Expected: 0 errors. This task changes only comments and markdown, so a green build is the whole check.

- [ ] **Step 4: Commit**

```bash
git add src/Mjm.LocalDocs.Core/Abstractions/IDocumentRepository.cs docs/superpowers/specs/2026-08-20-dashboard-know-how-growth-design.md
git commit -m "Stop claiming chunk and embedding counts always agree"
```

---

## Self-Review

**Finding coverage.** Each of the review's six Important findings maps to a task: zero-chunk reindex reporting success → Task 2; the derived interrupted-update signal → Tasks 3 and 5; reindex re-arming a duplicate → Task 4; `UpdateDocumentAsync`'s ordering → Task 1; the four-sites-three-orders inconsistency → Task 1; the false invariant → Task 6. The two Minor findings promoted to fix-before-merge are folded in: `GetInterruptedUpdatesAsync`'s multi-document coverage lands with Task 3's five cases, and Task 3's chain test exercises `GetChunkOwnershipAsync`-style cross-attribution over more than one id.

**Deliberately not in this plan**, and recorded so their absence is a decision rather than an oversight: stopping project deletion from orphaning embeddings; server-side serialisation of `ReindexDocumentAsync` (which needs a singleton lock registry, since `DocumentService` is registered scoped, so a field on it would be per-request and useless); and `SearchAsync`'s own N+1 with full-entity loads. All three are pre-existing defects this work merely revealed, each with real design alternatives, and bundling them here would double the branch.

**Placeholder scan.** No TBD, no TODO. Every code step carries the code to write.

**Type consistency.** `InterruptedUpdate(DocumentId, FileName, ParentDocumentId, ParentFileName)` is defined in Task 3 and read in Tasks 3 and 5. `IndexHealth` gains its fourth member in Task 3 and every construction site is updated there. `ReindexDocumentAsync` becomes `Task<int>` in Task 2 and keeps that signature in Tasks 4 and 5. `HasActiveChildAsync(string, CancellationToken) → Task<bool>` is defined and consumed in Task 4.

**One ordering dependency worth stating.** Task 2 must precede Task 5, because the panel's new handler and its zero-chunk message both depend on `ReindexDocumentAsync` returning a count. Task 3 must precede Tasks 4 and 5. Task 1 and Task 6 are independent of the rest.
