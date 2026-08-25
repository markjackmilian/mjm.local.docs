# Making File Loss Visible — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Close the last two defects the reviews on this branch left open. A document whose original file has been destroyed currently reads as merely "needs reindexing" and goes green after a reindex, so the loss is invisible; and the vector stores delete embeddings with an unescaped `LIKE` pattern whose `_` is a wildcard.

**Architecture:** The health check gains a third category alongside broken indexes and unfinished updates, populated only during the forced reconciliation the Review button already triggers — checking every document's file on every page load would mean one network round trip per document against a blob account. The `LIKE` fix escapes the pattern in the two SQL vector stores.

**Tech Stack:** .NET 10, C#, Blazor Server (InteractiveServer), MudBlazor 8.15.0, EF Core 10 (SQLite + SQL Server), xUnit v3 + NSubstitute.

**Predecessors:** `2026-08-20-dashboard-know-how-growth.md` (11 tasks), `2026-08-20-dashboard-followup-integrity.md` (6 tasks), `2026-08-20-storage-leaks-and-concurrency.md` (5 tasks plus a 2-task addendum). All complete. Both defects here were recorded by those plans' reviews and deliberately deferred; this plan closes them.

## Global Constraints

- **Solution root:** `C:\Projects\mjm.local.docs\mjm.local.docs` (nested). **All `dotnet` commands and every `src/...` / `tests/...` path below is relative to that folder.** This plan lives in the outer repo under `docs/superpowers/plans/`.
- **Branch:** `feature/dashboard`, already checked out. Do not create a branch.
- **Target framework:** `net10.0`; `Nullable` and `ImplicitUsings` enabled.
- **No EF migration, no schema change. No new NuGet packages.**
- **Never load `FileContent` or `ExtractedText` to answer a question about identity or membership.**
- **Commits:** concise messages matching repo style. **NEVER** add a `Co-Authored-By` trailer. Commit with **explicit paths** — a directory-wide `git add` swept an unrelated document into a commit twice on this branch.
- **No bUnit in this repo.** The panel change is verified by `dotnet build` plus reading.
- **Baseline before Task 1:** 327 passed / 17 skipped (SQL Server, no server reachable) / 0 failed.
- **Report evidence rule:** any claim about build warnings must come from `dotnet build --no-incremental`, and grepping only `src/` filenames cannot detect new warnings in `tests/`. If you did not check, say so. A report on this branch once presented a paraphrased failure message as though quoted — do not do that.

---

## Task 1: Show the documents whose original file is gone

**Files:**
- Modify: `src/Mjm.LocalDocs.Core/Models/Dashboard/DashboardReadModels.cs`
- Modify: `src/Mjm.LocalDocs.Core/Abstractions/IDocumentRepository.cs`
- Modify: `src/Mjm.LocalDocs.Infrastructure/Persistence/Repositories/EfCoreDocumentRepository.cs`
- Modify: `src/Mjm.LocalDocs.Infrastructure/VectorStore/InMemoryDocumentRepository.cs`
- Modify: `src/Mjm.LocalDocs.Core/Services/DashboardMetricsService.cs`
- Modify: `src/Mjm.LocalDocs.Core/DependencyInjection/ServiceCollectionExtensions.cs`
- Modify: `src/Mjm.LocalDocs.Server/Components/Dashboard/IndexHealthPanel.razor`
- Test: `tests/Mjm.LocalDocs.Tests/Repositories/DocumentRepositoryAggregateTests.cs`
- Test: `tests/Mjm.LocalDocs.Tests/Services/DashboardMetricsServiceTests.cs`

**Interfaces:**
- Produces: `record MissingFile(string DocumentId, string FileName, string StorageLocation)`; `IDocumentRepository.GetActiveExternalFilesAsync(CancellationToken) → Task<IReadOnlyList<MissingFile>>` (every active document that *claims* an external file — the caller decides which of them are actually missing); `IndexHealth` gains `IReadOnlyList<MissingFile> MissingFiles`; `DashboardMetricsService`'s constructor gains an optional `IDocumentFileStorage?`.

**Why this task exists.** A project deletion that fails part-way deletes the external files of every document it reached before it threw, and leaves those documents' rows and `ExtractedText` intact. The health check only ever compares chunk count to embedding count, so those documents show up as "uploaded but not searchable", the Reindex button rebuilds their embeddings from the text that survived, the panel turns green — and every download of them 404s forever. The user's only signal was one transient snackbar at the moment of failure.

This is the one state on the branch that no derived signal can see, and it is the worst kind: the dashboard actively reassures the user that it is fixed.

**Why only on the forced reconciliation.** Every other check in this panel is a database read. This one is a storage call per document — against Azure Blob, one network round trip each. Running it on every dashboard load would make the home page's cost scale with the document count times network latency. The panel already distinguishes the cheap path taken on load from the full reconciliation the Review button pays for; this belongs in the second. Say so in the code, because the asymmetry will otherwise look like an oversight.

**Note on the Database provider.** With `FileStorage: Database` the content lives in the row and `FileStorageLocation` is null for every document, so the new read returns nothing and the check is free. It matters for `FileSystem` and `AzureBlob` — and the development machine's own configuration selects `AzureBlob`, so this is live rather than theoretical.

- [ ] **Step 1: Add the record and extend `IndexHealth`**

In `DashboardReadModels.cs`, append:

```csharp
/// <summary>
/// An active document whose original file is stored outside the database.
/// </summary>
/// <param name="DocumentId">The document identifier.</param>
/// <param name="FileName">The document's file name, for display.</param>
/// <param name="StorageLocation">The external storage path the row points at.</param>
public sealed record MissingFile(string DocumentId, string FileName, string StorageLocation);
```

Then extend `IndexHealth` with a fifth member and document it:

```csharp
/// <param name="MissingFiles">
/// Active documents whose row points at an external file that no longer exists. Distinct from
/// <paramref name="Broken"/> in the direction that matters: these documents are perfectly
/// searchable and cannot be repaired by reindexing, because the text survived and the file did
/// not. Populated only by a forced reconciliation — see the remarks on
/// <c>DashboardMetricsService.GetIndexHealthAsync</c>.
/// </param>
```

- [ ] **Step 2: Write the failing repository tests**

Append to the abstract base `tests/Mjm.LocalDocs.Tests/Repositories/DocumentRepositoryAggregateTests.cs`. Both fixtures inherit them, so each case runs against real SQLite and against the in-memory repository. Add nothing to either subclass.

`SeedDocumentAsync` does not set `FileStorageLocation`, so you need a second helper for this. Add it beside the existing one:

```csharp
    protected async Task SeedExternalDocumentAsync(
        string id,
        string storageLocation,
        bool isSuperseded = false)
    {
        await EnsureProjectAsync("proj-1");

        await Sut.AddDocumentAsync(new Document
        {
            Id = id,
            ProjectId = "proj-1",
            FileName = $"{id}.pdf",
            FileExtension = ".pdf",
            FileSizeBytes = 10,
            ExtractedText = "text",
            FileStorageLocation = storageLocation,
            IsSuperseded = isSuperseded,
            CreatedAt = Jan
        });
    }
```

Then the cases:

```csharp
    [Fact]
    public async Task GetActiveExternalFilesAsync_ReturnsDocumentsWithAnExternalPath()
    {
        await SeedExternalDocumentAsync("doc-1", "proj-1/doc-1.pdf");

        var files = await Sut.GetActiveExternalFilesAsync();

        var file = Assert.Single(files);
        Assert.Equal("doc-1", file.DocumentId);
        Assert.Equal("doc-1.pdf", file.FileName);
        Assert.Equal("proj-1/doc-1.pdf", file.StorageLocation);
    }

    [Fact]
    public async Task GetActiveExternalFilesAsync_IgnoresDocumentsStoredInTheDatabase()
    {
        // A null FileStorageLocation means the content is in the row, so there is no file to lose.
        await SeedDocumentAsync("doc-1");

        var files = await Sut.GetActiveExternalFilesAsync();

        Assert.Empty(files);
    }

    [Fact]
    public async Task GetActiveExternalFilesAsync_IgnoresSupersededDocuments()
    {
        // A superseded version is not offered for download, so a missing file behind it is not a
        // loss the user can act on.
        await SeedExternalDocumentAsync("doc-1", "proj-1/doc-1.pdf", isSuperseded: true);

        var files = await Sut.GetActiveExternalFilesAsync();

        Assert.Empty(files);
    }
```

- [ ] **Step 3: Run the tests to verify they fail**

Run: `dotnet test tests/Mjm.LocalDocs.Tests/Mjm.LocalDocs.Tests.csproj --filter "FullyQualifiedName~GetActiveExternalFilesAsync"`
Expected: BUILD FAILURE — `'IDocumentRepository' does not contain a definition for 'GetActiveExternalFilesAsync'`.

- [ ] **Step 4: Add the interface member**

Append inside `IDocumentRepository.cs`'s existing `#region Dashboard Aggregates`:

```csharp
    /// <summary>
    /// Gets every active document whose row points at an externally-stored file.
    /// </summary>
    /// <remarks>
    /// Returns the documents that *claim* an external file; whether each file still exists is the
    /// caller's question to ask, because only the caller knows which storage provider is
    /// configured. Three scalar columns, no text and no blobs. Superseded versions are excluded:
    /// they are not offered for download, so a missing file behind one is not a loss the user can
    /// act on.
    /// </remarks>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>One record per active document with an external path, in no guaranteed order.</returns>
    Task<IReadOnlyList<MissingFile>> GetActiveExternalFilesAsync(
        CancellationToken cancellationToken = default);
```

- [ ] **Step 5: Implement in both repositories**

`EfCoreDocumentRepository`, inside its `#region Dashboard Aggregates`:

```csharp
    /// <inheritdoc />
    public async Task<IReadOnlyList<MissingFile>> GetActiveExternalFilesAsync(
        CancellationToken cancellationToken = default)
    {
        return await _context.Documents
            .AsNoTracking()
            .Where(d => !d.IsSuperseded && d.FileStorageLocation != null)
            .Select(d => new MissingFile(d.Id, d.FileName, d.FileStorageLocation!))
            .ToListAsync(cancellationToken);
    }
```

`InMemoryDocumentRepository`, inside its `#region Dashboard Aggregates`:

```csharp
    /// <inheritdoc />
    public Task<IReadOnlyList<MissingFile>> GetActiveExternalFilesAsync(
        CancellationToken cancellationToken = default)
    {
        var files = _documents.Values
            .Where(d => !d.IsSuperseded && !string.IsNullOrEmpty(d.FileStorageLocation))
            .Select(d => new MissingFile(d.Id, d.FileName, d.FileStorageLocation!))
            .ToList();

        return Task.FromResult<IReadOnlyList<MissingFile>>(files);
    }
```

Note the deliberate difference: EF checks `!= null` because that is what translates to `IS NOT NULL`, while the in-memory version also rejects the empty string, which `Document` permits but no writer produces. Both agree on every value the application actually stores.

- [ ] **Step 6: Write the failing service tests**

Append to `tests/Mjm.LocalDocs.Tests/Services/DashboardMetricsServiceTests.cs`. The fixture needs the storage substitute — add it beside the others and thread it through `CreateSut` as the last argument:

```csharp
    private readonly IDocumentFileStorage _fileStorage = Substitute.For<IDocumentFileStorage>();
```

```csharp
    [Fact]
    public async Task GetIndexHealthAsync_WithoutForcing_DoesNotTouchFileStorage()
    {
        _repository.GetActiveDocumentChunkTalliesAsync(Arg.Any<CancellationToken>())
            .Returns([Tally("doc-1", 1)]);
        _repository.CountChunksAsync(Arg.Any<CancellationToken>()).Returns(1L);
        _vectorStore.CountAsync(Arg.Any<CancellationToken>()).Returns(1L);

        var health = await CreateSut().GetIndexHealthAsync();

        // One storage round trip per document, against a blob account, is not something the home
        // page pays on every load.
        Assert.Empty(health.MissingFiles);
        await _repository.DidNotReceive().GetActiveExternalFilesAsync(Arg.Any<CancellationToken>());
        await _fileStorage.DidNotReceive().FileExistsAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetIndexHealthAsync_WhenForced_ReportsAFileThatNoLongerExists()
    {
        _repository.GetActiveDocumentChunkTalliesAsync(Arg.Any<CancellationToken>())
            .Returns([Tally("doc-1", 1)]);
        _repository.CountChunksAsync(Arg.Any<CancellationToken>()).Returns(1L);
        _vectorStore.CountAsync(Arg.Any<CancellationToken>()).Returns(1L);
        _repository.GetActiveExternalFilesAsync(Arg.Any<CancellationToken>())
            .Returns([new MissingFile("doc-1", "doc-1.pdf", "proj-1/doc-1.pdf")]);
        _fileStorage.FileExistsAsync("doc-1", "proj-1/doc-1.pdf", Arg.Any<CancellationToken>())
            .Returns(false);

        var health = await CreateSut().GetIndexHealthAsync(forceFullReconciliation: true);

        // Searchable, fully indexed, and undownloadable. Nothing else in this payload can say so.
        Assert.Empty(health.Broken);
        Assert.Equal("doc-1", Assert.Single(health.MissingFiles).DocumentId);
    }

    [Fact]
    public async Task GetIndexHealthAsync_WhenForced_IgnoresFilesThatStillExist()
    {
        _repository.GetActiveDocumentChunkTalliesAsync(Arg.Any<CancellationToken>())
            .Returns([Tally("doc-1", 1)]);
        _repository.CountChunksAsync(Arg.Any<CancellationToken>()).Returns(1L);
        _vectorStore.CountAsync(Arg.Any<CancellationToken>()).Returns(1L);
        _repository.GetActiveExternalFilesAsync(Arg.Any<CancellationToken>())
            .Returns([new MissingFile("doc-1", "doc-1.pdf", "proj-1/doc-1.pdf")]);
        _fileStorage.FileExistsAsync("doc-1", "proj-1/doc-1.pdf", Arg.Any<CancellationToken>())
            .Returns(true);

        var health = await CreateSut().GetIndexHealthAsync(forceFullReconciliation: true);

        Assert.Empty(health.MissingFiles);
    }

    [Fact]
    public async Task GetIndexHealthAsync_WithNoFileStorageConfigured_ReportsNoMissingFiles()
    {
        _repository.GetActiveDocumentChunkTalliesAsync(Arg.Any<CancellationToken>())
            .Returns([Tally("doc-1", 1)]);
        _repository.CountChunksAsync(Arg.Any<CancellationToken>()).Returns(1L);
        _vectorStore.CountAsync(Arg.Any<CancellationToken>()).Returns(1L);

        var sut = new DashboardMetricsService(_repository, _vectorStore, new FixedTimeProvider(Now), fileStorage: null);

        var health = await sut.GetIndexHealthAsync(forceFullReconciliation: true);

        // Nothing to check, and no read wasted asking.
        Assert.Empty(health.MissingFiles);
        await _repository.DidNotReceive().GetActiveExternalFilesAsync(Arg.Any<CancellationToken>());
    }
```

- [ ] **Step 7: Check the files in the service**

Give `DashboardMetricsService` the optional storage dependency — last parameter, after `timeProvider`, so no existing call shifts:

```csharp
    private readonly IDocumentFileStorage? _fileStorage;
```

```csharp
    /// <param name="fileStorage">
    /// External file storage, or null when file content is held in the database. When null there
    /// is no external file to lose, so the missing-file check is skipped entirely.
    /// </param>
```

Then, inside `GetIndexHealthAsync`, add the check under the same `forceFullReconciliation` condition the embedding probe already uses — and extend that method's `<remarks>` to say why this one is not on the cheap path:

```csharp
    /// <remarks>
    /// The missing-file check runs only when <paramref name="forceFullReconciliation"/> is set. It
    /// costs one storage round trip per document with an external file — against a blob account,
    /// one network call each — so unlike every other check here it is not something the home page
    /// pays on every load. The Review button is where it belongs.
    /// </remarks>
```

```csharp
        var missingFiles = forceFullReconciliation
            ? await FindMissingFilesAsync(cancellationToken)
            : [];
```

and the helper:

```csharp
    private async Task<IReadOnlyList<MissingFile>> FindMissingFilesAsync(
        CancellationToken cancellationToken)
    {
        if (_fileStorage is null)
            return [];

        var candidates = await _repository.GetActiveExternalFilesAsync(cancellationToken);
        var missing = new List<MissingFile>();

        foreach (var candidate in candidates)
        {
            if (!await _fileStorage.FileExistsAsync(
                    candidate.DocumentId, candidate.StorageLocation, cancellationToken))
            {
                missing.Add(candidate);
            }
        }

        return missing
            .OrderBy(f => f.FileName, StringComparer.CurrentCulture)
            .ToList();
    }
```

Pass `missingFiles` into the `IndexHealth` you return. Ordering matches `Broken` and `InterruptedUpdates` for the same reason: the panel renders it, and a list that reshuffles between refreshes reads as churn.

Update the DI factory in `src/Mjm.LocalDocs.Core/DependencyInjection/ServiceCollectionExtensions.cs`: `DashboardMetricsService` is currently registered with `AddScoped<DashboardMetricsService>()`, which cannot supply an optional dependency that *is* registered. Replace it with a factory mirroring the one `DocumentService` uses, resolving `IDocumentFileStorage` with `GetService` and `TimeProvider` with `GetService` too so its own default still applies when absent.

- [ ] **Step 8: Show it in the panel**

In `IndexHealthPanel.razor`, add a third section after the interrupted-updates block, inside the same `else`. It has **no repair action** — that is the point, and the copy has to say so, because a document in this list is one the Reindex button will happily turn green without fixing anything:

```razor
        @if (Health.MissingFiles.Count > 0)
        {
            <MudText Style="font-size: 0.75rem; font-weight: 600; color: var(--ld-text-primary); margin: 12px 0 6px;">
                Original file missing
            </MudText>
            <MudText Style="font-size: 0.6875rem; color: var(--ld-text-muted); margin-bottom: 6px;">
                Still searchable, but the file cannot be downloaded. Reindexing will not bring it back.
            </MudText>

            @foreach (var file in Health.MissingFiles)
            {
                <div class="d-flex align-center" style="gap: 8px; padding: 6px 0; border-top: 1px solid var(--ld-border);">
                    <MudIcon Icon="@Icons.Material.Rounded.LinkOff"
                             Style="font-size: 0.875rem; color: var(--ld-stat-rose-icon);" />
                    <MudTooltip Text="@($"{file.FileName} — {file.StorageLocation}")">
                        <MudText Style="font-size: 0.75rem; color: var(--ld-text-secondary); overflow: hidden; text-overflow: ellipsis; white-space: nowrap; max-width: 170px;">
                            @file.FileName
                        </MudText>
                    </MudTooltip>
                </div>
            }
        }
```

The all-clear condition now has three lists to account for. Extend it, and note that because this list is only ever populated by a forced reconciliation, "Everything is indexed." on a cheap load says nothing about files — which is honest, since the cheap load did not look.

- [ ] **Step 9: Verify**

Run: `dotnet test`
Expected: PASS, 337 (327 baseline + 6 repository cases across two fixtures + 4 service cases). Every existing `IndexHealth` construction needs a fifth argument; pass an empty list where a test does not care, and do **not** weaken an existing assertion to accommodate it — if one has to change, stop and report.

Run: `dotnet build --no-incremental`
Expected: 0 errors. Grep for each changed filename and report what you find.

- [ ] **Step 10: Commit**

```bash
git add src/Mjm.LocalDocs.Core src/Mjm.LocalDocs.Infrastructure src/Mjm.LocalDocs.Server/Components/Dashboard/IndexHealthPanel.razor tests/Mjm.LocalDocs.Tests
git commit -m "Report documents whose original file is gone"
```

---

## Task 2: Escape the LIKE pattern the vector stores delete by

**Files:**
- Modify: `src/Mjm.LocalDocs.Infrastructure/Persistence/SqliteVectorStore.cs`
- Modify: `src/Mjm.LocalDocs.Infrastructure/Persistence/SqlServerVectorStore.cs`
- Test: `tests/Mjm.LocalDocs.Tests/VectorStore/SqliteVectorStoreTests.cs`

**Interfaces:** none. `DeleteByDocumentIdAsync` keeps its signature.

**Why this task exists.** Both SQL vector stores delete a document's embeddings with `WHERE chunk_id LIKE '{documentId}_chunk_%'`. In `LIKE`, `_` matches any single character and `%` matches any sequence, so a document id containing either would over-match: deleting `doc_1` would also remove the embeddings of `docX1`, and an id containing `%` could clear a whole prefix. Ids are GUIDs today, so this is unreachable — but it is a data-loss shape sitting in a delete path, and the earlier work fanned it out across a whole project per click.

The right fix is `ESCAPE`, not string mangling. Both SQLite and SQL Server support `LIKE ... ESCAPE '\'`, and both treat the escape character literally when it precedes anything else, so escaping `\`, `%` and `_` in that order is sufficient and safe.

- [ ] **Step 1: Write the failing test**

Append to `tests/Mjm.LocalDocs.Tests/VectorStore/SqliteVectorStoreTests.cs`, inside the existing class:

```csharp
    [Fact]
    public async Task DeleteByDocumentIdAsync_WithAnUnderscoreInTheId_DoesNotTouchASimilarDocument()
    {
        // In LIKE, _ matches any single character. Unescaped, deleting "doc_1" also deletes "docX1".
        await _sut.UpsertAsync("doc_1_chunk_0", Dim128(0.1f));
        await _sut.UpsertAsync("docX1_chunk_0", Dim128(0.2f));

        await _sut.DeleteByDocumentIdAsync("doc_1");

        var survivors = await _sut.GetExistingChunkIdsAsync(["doc_1_chunk_0", "docX1_chunk_0"]);

        Assert.Equal(["docX1_chunk_0"], survivors);
    }

    [Fact]
    public async Task DeleteByDocumentIdAsync_WithAPercentInTheId_DeletesOnlyThatDocument()
    {
        // And % matches any sequence, so an id containing it could clear a whole prefix.
        await _sut.UpsertAsync("a%b_chunk_0", Dim128(0.1f));
        await _sut.UpsertAsync("azzb_chunk_0", Dim128(0.2f));

        await _sut.DeleteByDocumentIdAsync("a%b");

        var survivors = await _sut.GetExistingChunkIdsAsync(["a%b_chunk_0", "azzb_chunk_0"]);

        Assert.Equal(["azzb_chunk_0"], survivors);
    }
```

Only SQLite gets tests: its suite runs against a real database file, while the SQL Server suite skips without a reachable server. Apply the identical fix to both stores regardless — a fix present in one and absent in the other is worse than the bug.

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/Mjm.LocalDocs.Tests/Mjm.LocalDocs.Tests.csproj --filter "FullyQualifiedName~DeleteByDocumentIdAsync_With"`
Expected: FAIL. The underscore case deletes both rows, so `survivors` comes back empty instead of holding `docX1_chunk_0`.

- [ ] **Step 3: Escape the pattern in both stores**

Add this private helper to each of the two stores, with the same body and comment:

```csharp
    /// <summary>
    /// Escapes the LIKE wildcards in a document id so a prefix match cannot over-match.
    /// </summary>
    /// <remarks>
    /// Backslash first: escaping it after the wildcards would double-escape the escapes this
    /// method just inserted.
    /// </remarks>
    private static string EscapeLikePattern(string value) =>
        value.Replace("\\", "\\\\")
             .Replace("%", "\\%")
             .Replace("_", "\\_");
```

In `SqliteVectorStore.DeleteByDocumentIdAsync`, change the command text and parameter to:

```csharp
        cmd.CommandText = "DELETE FROM chunk_embeddings WHERE chunk_id LIKE @pattern ESCAPE '\\'";
        cmd.Parameters.AddWithValue("@pattern", $"{EscapeLikePattern(documentId)}_chunk_%");
```

Note that the trailing `_chunk_%` is *not* escaped: those wildcards are the ones this query intends.

Apply the same change in `SqlServerVectorStore.DeleteByDocumentIdAsync` — same `ESCAPE '\'` clause on its `DELETE FROM {FullTableName} WHERE chunk_id LIKE @pattern`, same escaped-prefix parameter.

- [ ] **Step 4: Verify**

Run: `dotnet test tests/Mjm.LocalDocs.Tests/Mjm.LocalDocs.Tests.csproj --filter "FullyQualifiedName~SqliteVectorStoreTests"`
Expected: PASS, including the two new cases.

Run: `dotnet test`
Expected: PASS, 339 (337 after Task 1 + 2 here).

- [ ] **Step 5: Commit**

```bash
git add src/Mjm.LocalDocs.Infrastructure/Persistence/SqliteVectorStore.cs src/Mjm.LocalDocs.Infrastructure/Persistence/SqlServerVectorStore.cs tests/Mjm.LocalDocs.Tests/VectorStore/SqliteVectorStoreTests.cs
git commit -m "Escape LIKE wildcards when deleting a document's embeddings"
```

---

## Self-Review

**Coverage.** The two defects this plan targets are the last two technical items the branch's reviews left open: the invisible file loss (Task 1) and the unescaped `LIKE` (Task 2).

**Deliberately still not here, and why.** Cleaning up embeddings and files already orphaned by past deletions is a *maintenance capability*, not a defect fix: it needs a new `IVectorStore` method to enumerate all stored embedding ids — which `IVectorStore` does not have and two of its four implementations would have to gain — plus a destructive action with its own confirmation flow. It should be specified and reviewed as a feature, not appended to a fix.

**One thing to expect in review.** Task 1 makes the Review button materially more expensive: it now costs a storage round trip per document with an external file, on top of the embedding probe. That is the deliberate trade — the cheap load stays cheap and the expensive answer is available on demand — but it means a large knowledge base on blob storage will feel that button. Worth a progress indicator eventually; not worth blocking this on one.

**A limit worth stating plainly.** This makes the loss *visible*; it does not make it *recoverable*. Nothing here can bring a deleted file back. The value is that the panel stops claiming a document is fine when its file is gone, and stops offering a Reindex that would turn it green.
