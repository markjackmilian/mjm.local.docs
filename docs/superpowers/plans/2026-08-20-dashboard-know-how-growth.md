# Dashboard Know-How Growth & Index Health — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the home dashboard with a stacked-bar growth chart (new documents vs new versions), five diagnostic KPI cards, and an index health panel — and fix the ingestion pipeline so documents can no longer end up uploaded-but-unsearchable without anyone noticing.

**Architecture:** Additive read methods on `IDocumentRepository` (aggregate projections, no entity materialisation) and `IVectorStore` (embedding count + existence probe) feed a new concrete `DashboardMetricsService` in Core, which folds contributions into calendar buckets in C# rather than SQL. `DocumentService` grows a shared private `IndexDocumentAsync` used by both insertion and a new `ReindexDocumentAsync`, with compensating cleanup so a failed embedding call degrades to a clean zero-chunk state instead of a silent half-index. Three new Blazor components compose the reworked `Home.razor`.

**Tech Stack:** .NET 10, C#, Blazor Server (InteractiveServer), MudBlazor 8.15.0 (`MudChart` StackedBar + Donut, `MudToggleGroup`), EF Core 10 (SQLite + SQL Server), xUnit v3 + NSubstitute.

**Design spec:** `docs/superpowers/specs/2026-08-20-dashboard-know-how-growth-design.md`

## Global Constraints

- **Solution root:** `C:\Projects\mjm.local.docs\mjm.local.docs` (nested). **All `dotnet` commands and every `src/...` / `tests/...` path below are relative to that folder.** This plan document lives in the outer repo at `docs/superpowers/plans/`.
- **Branch:** `feature/dashboard` (already checked out).
- **Target framework:** `net10.0`; `Nullable` and `ImplicitUsings` enabled (already set in all csproj).
- **No EF migration, no schema change.** Every new read is derivable from existing columns. Hard constraint from the spec.
- **No new NuGet packages.** Not for charting (MudBlazor 8.15.0 is already referenced), not for time faking (a local `TimeProvider` subclass is used instead of `Microsoft.Extensions.TimeProvider.Testing`).
- **Commits:** concise messages matching repo style (e.g. `Add dashboard metrics service`). **NEVER** add a `Co-Authored-By` trailer (user global instruction — overrides any default).
- **Domain conventions:** `sealed` types, `required` members, string GUID ids, XML doc comments on every public member, `#region` grouping in the repositories as the surrounding code already does.
- **UI language is English.** The existing pages read "Total Size", "QUICK ACTIONS", "RECENT PROJECTS". All new labels follow that, regardless of the language used in planning.
- **Styling:** reuse the `--ld-*` tokens in `wwwroot/app.css` including their `[data-theme="dark"]` overrides. The only new literals allowed are the two chart palette colours in Task 9 and Task 10.
- **No bUnit in this repo.** UI tasks (8–11) have no automated tests; they are verified by `dotnet build` plus the manual check written into each task. Do not invent a UI test framework.
- **`DateTimeOffset` does not translate on the SQLite provider.** `Microsoft.EntityFrameworkCore.Sqlite` 10.0.2 refuses relational comparisons (`>=`), aggregates (`Max`/`Min`), and `ORDER BY` on a `DateTimeOffset` column — it throws rather than risk a wrong answer, because it stores the value as TEXT and cannot guarantee ordering across mixed offsets. Only equality and plain projection translate. SQLite is the default store (`appsettings.json` → `LocalDocs:Storage:Provider: "Sqlite"`), so any date filtering or aggregation must happen **in C# over a narrow column projection**, never in SQL. This is already the house pattern: `EfCoreApiTokenRepository.GetAllAsync` materialises with `ToListAsync` and then sorts by `CreatedAt` in LINQ-to-Objects for exactly this reason. Projecting scalar columns is what keeps this cheap — the bug being removed was materialising `DocumentEntity`, which carries `FileContent`.
- **Deviation from the spec, deliberate:** the spec named a repository method `GetChunkIdsByDocumentsAsync(ids)` returning flat chunk ids. This plan uses `GetChunkOwnershipAsync(ids)` returning `(DocumentId, ChunkId)` pairs instead, because mapping a chunk id back to its document would otherwise require parsing the `{documentId}_chunk_{index}` convention with `LastIndexOf`. Carrying the owner explicitly removes that fragility. Nothing else in the spec changes.

---

## File Structure

**Created:**

| File | Responsibility |
|---|---|
| `src/Mjm.LocalDocs.Core/Models/Dashboard/DashboardReadModels.cs` | All six dashboard records + `GrowthGranularity` enum. One file: they are small, cohesive, and always change together |
| `src/Mjm.LocalDocs.Core/Services/DashboardMetricsService.cs` | Bucketing, rolling-window KPIs, index-health fast path and probe |
| `src/Mjm.LocalDocs.Core/Services/DocumentIndexingException.cs` | Typed failure so callers can say "saved but not indexed" |
| `src/Mjm.LocalDocs.Server/Components/Dashboard/StatCard.razor` | One KPI card |
| `src/Mjm.LocalDocs.Server/Components/Dashboard/KnowHowGrowthChart.razor` | Chart card + granularity toggle |
| `src/Mjm.LocalDocs.Server/Components/Dashboard/IndexHealthPanel.razor` | Donut, broken-document list, reindex actions |
| `tests/Mjm.LocalDocs.Tests/Services/DashboardMetricsServiceTests.cs` | Bucketing and health logic |
| `tests/Mjm.LocalDocs.Tests/Services/DocumentServiceIndexingTests.cs` | Pipeline failure, repair, interrupted-update closure |
| `tests/Mjm.LocalDocs.Tests/Repositories/DocumentRepositoryAggregateTests.cs` | Abstract base holding every aggregate test, written once |
| `tests/Mjm.LocalDocs.Tests/Repositories/EfCoreDocumentRepositoryAggregateTests.cs` | Runs the base suite against real SQLite |
| `tests/Mjm.LocalDocs.Tests/Repositories/InMemoryDocumentRepositoryAggregateTests.cs` | Runs the base suite against the in-memory repository |

**Modified:**

| File | Change |
|---|---|
| `src/Mjm.LocalDocs.Core/Abstractions/IVectorStore.cs` | +2 methods |
| `src/Mjm.LocalDocs.Core/Abstractions/IDocumentRepository.cs` | +7 methods |
| `src/Mjm.LocalDocs.Infrastructure/VectorStore/InMemoryVectorStore.cs` | Implement +2 |
| `src/Mjm.LocalDocs.Infrastructure/VectorStore/Hnsw/HnswVectorStore.cs` | Implement +2 |
| `src/Mjm.LocalDocs.Infrastructure/Persistence/SqliteVectorStore.cs` | Implement +2 |
| `src/Mjm.LocalDocs.Infrastructure/Persistence/SqlServerVectorStore.cs` | Implement +2 |
| `src/Mjm.LocalDocs.Infrastructure/Persistence/Repositories/EfCoreDocumentRepository.cs` | Implement +7 |
| `src/Mjm.LocalDocs.Infrastructure/VectorStore/InMemoryDocumentRepository.cs` | Implement +7 |
| `src/Mjm.LocalDocs.Core/Services/DocumentService.cs` | Extract `IndexDocumentAsync`, add compensation + `ReindexDocumentAsync` |
| `src/Mjm.LocalDocs.Core/DependencyInjection/ServiceCollectionExtensions.cs` | Register `DashboardMetricsService` |
| `src/Mjm.LocalDocs.Server/wwwroot/app.css` | +2 stat accents (violet, rose), light and dark |
| `src/Mjm.LocalDocs.Server/Components/Pages/Home.razor` | Rewritten as composition; removes the per-project N+1 |

---

## Task 1: Vector store embedding count and existence probe

**Files:**
- Modify: `src/Mjm.LocalDocs.Core/Abstractions/IVectorStore.cs`
- Modify: `src/Mjm.LocalDocs.Infrastructure/VectorStore/InMemoryVectorStore.cs`
- Modify: `src/Mjm.LocalDocs.Infrastructure/VectorStore/Hnsw/HnswVectorStore.cs`
- Modify: `src/Mjm.LocalDocs.Infrastructure/Persistence/SqliteVectorStore.cs`
- Modify: `src/Mjm.LocalDocs.Infrastructure/Persistence/SqlServerVectorStore.cs`
- Test: `tests/Mjm.LocalDocs.Tests/VectorStore/InMemoryVectorStoreTests.cs`
- Test: `tests/Mjm.LocalDocs.Tests/VectorStore/SqliteVectorStoreTests.cs`
- Test: `tests/Mjm.LocalDocs.Tests/VectorStore/HnswVectorStoreTests.cs`
- Test: `tests/Mjm.LocalDocs.Tests/VectorStore/SqlServerVectorStoreTests.cs`

**Interfaces:**
- Consumes: nothing (first task).
- Produces: `IVectorStore.CountAsync(CancellationToken) → Task<long>` and `IVectorStore.GetExistingChunkIdsAsync(IEnumerable<string>, CancellationToken) → Task<IReadOnlyList<string>>`. Task 4 consumes both.

- [ ] **Step 1: Write the failing tests**

Append to `tests/Mjm.LocalDocs.Tests/VectorStore/InMemoryVectorStoreTests.cs` (inside the existing class):

```csharp
    [Fact]
    public async Task CountAsync_WithNoEmbeddings_ReturnsZero()
    {
        var count = await _sut.CountAsync();

        Assert.Equal(0L, count);
    }

    [Fact]
    public async Task CountAsync_AfterUpserts_ReturnsNumberOfEmbeddings()
    {
        await _sut.UpsertAsync("doc-1_chunk_0", new float[] { 0.1f, 0.2f });
        await _sut.UpsertAsync("doc-1_chunk_1", new float[] { 0.3f, 0.4f });

        var count = await _sut.CountAsync();

        Assert.Equal(2L, count);
    }

    [Fact]
    public async Task GetExistingChunkIdsAsync_ReturnsOnlyStoredIds()
    {
        await _sut.UpsertAsync("doc-1_chunk_0", new float[] { 0.1f, 0.2f });

        var existing = await _sut.GetExistingChunkIdsAsync(
            ["doc-1_chunk_0", "doc-1_chunk_1", "doc-2_chunk_0"]);

        Assert.Equal(["doc-1_chunk_0"], existing);
    }

    [Fact]
    public async Task GetExistingChunkIdsAsync_WithEmptyInput_ReturnsEmpty()
    {
        await _sut.UpsertAsync("doc-1_chunk_0", new float[] { 0.1f, 0.2f });

        var existing = await _sut.GetExistingChunkIdsAsync([]);

        Assert.Empty(existing);
    }
```

Append the same four tests, adapted, to `SqliteVectorStoreTests.cs` and `HnswVectorStoreTests.cs`. Those two suites construct their `_sut` differently (temp file paths) but the assertions are identical — copy the bodies verbatim. For `SqliteVectorStoreTests`, the embedding arrays must have 128 floats to match `embeddingDimension: 128` in its constructor; use this helper already-style local:

```csharp
    private static ReadOnlyMemory<float> Dim128(float seed)
    {
        var values = new float[128];
        for (var i = 0; i < values.Length; i++)
        {
            values[i] = seed + (i * 0.001f);
        }
        return values;
    }
```

Then in the Sqlite variants call `Dim128(0.1f)` / `Dim128(0.3f)` instead of the two-element arrays.

Add one batching test to `SqliteVectorStoreTests.cs`, which is the only place the parameter-count limit can actually bite:

```csharp
    [Fact]
    public async Task GetExistingChunkIdsAsync_WithSixHundredIds_ReturnsAllStoredOnes()
    {
        var stored = new List<string>();
        for (var i = 0; i < 600; i++)
        {
            var chunkId = $"doc-1_chunk_{i}";
            await _sut.UpsertAsync(chunkId, Dim128(i * 0.0001f));
            stored.Add(chunkId);
        }

        var probe = stored.Concat(Enumerable.Range(0, 600).Select(i => $"missing_chunk_{i}")).ToList();

        var existing = await _sut.GetExistingChunkIdsAsync(probe);

        Assert.Equal(600, existing.Count);
    }
```

For `SqlServerVectorStoreTests.cs`, follow that file's existing skip convention (the suite is skipped when no SQL Server is reachable — mirror whatever attribute the neighbouring tests already use) and add the `CountAsync` and `GetExistingChunkIdsAsync` cases with the dimension its `_sut` is constructed with.

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/Mjm.LocalDocs.Tests/Mjm.LocalDocs.Tests.csproj --filter "FullyQualifiedName~VectorStore"`
Expected: BUILD FAILURE — `'IVectorStore' does not contain a definition for 'CountAsync'`. A compile error is the correct failure here; do not proceed until you see it.

- [ ] **Step 3: Add the interface members**

In `src/Mjm.LocalDocs.Core/Abstractions/IVectorStore.cs`, add before `SearchAsync`:

```csharp
    /// <summary>
    /// Gets the total number of embeddings currently stored.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The number of stored embeddings.</returns>
    Task<long> CountAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the subset of the supplied chunk identifiers that have an embedding stored.
    /// </summary>
    /// <remarks>
    /// Callers are responsible for batching: implementations may build one database parameter
    /// per identifier, and SQL Server caps a command at 2100 parameters.
    /// Pass at most a few hundred identifiers per call.
    /// </remarks>
    /// <param name="chunkIds">The chunk identifiers to probe.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The identifiers that have an embedding, in no guaranteed order.</returns>
    Task<IReadOnlyList<string>> GetExistingChunkIdsAsync(
        IEnumerable<string> chunkIds,
        CancellationToken cancellationToken = default);
```

- [ ] **Step 4: Implement in `InMemoryVectorStore`**

Add after `DeleteByDocumentIdAsync`:

```csharp
    /// <inheritdoc />
    public Task<long> CountAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult((long)_embeddings.Count);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<string>> GetExistingChunkIdsAsync(
        IEnumerable<string> chunkIds,
        CancellationToken cancellationToken = default)
    {
        var existing = chunkIds.Where(_embeddings.ContainsKey).ToList();
        return Task.FromResult<IReadOnlyList<string>>(existing);
    }
```

- [ ] **Step 5: Implement in `HnswVectorStore`**

Add after `DeleteByDocumentIdAsync`:

```csharp
    /// <inheritdoc />
    public Task<long> CountAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        return Task.FromResult((long)_graph.Count);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<string>> GetExistingChunkIdsAsync(
        IEnumerable<string> chunkIds,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        // HnswGraph.Contains is an O(1) _idToIndex lookup under a read lock. Do not
        // snapshot GetAllIds() here: that scans and allocates the whole graph on every
        // probe, on precisely the backend chosen for large corpora.
        var existing = chunkIds.Where(_graph.Contains).ToList();
        return Task.FromResult<IReadOnlyList<string>>(existing);
    }
```

- [ ] **Step 6: Implement in `SqliteVectorStore`**

Add after `DeleteByDocumentIdAsync`:

```csharp
    /// <inheritdoc />
    public async Task<long> CountAsync(CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);

        await using var cmd = _connection.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM chunk_embeddings";

        var result = await cmd.ExecuteScalarAsync(cancellationToken);
        return result is long count ? count : 0L;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<string>> GetExistingChunkIdsAsync(
        IEnumerable<string> chunkIds,
        CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);

        var ids = chunkIds.ToList();
        if (ids.Count == 0)
            return [];

        var found = new List<string>();

        // Batch so a large probe cannot exceed the provider's parameter limit.
        foreach (var batch in ids.Chunk(500))
        {
            await using var cmd = _connection.CreateCommand();

            var parameterNames = new string[batch.Length];
            for (var i = 0; i < batch.Length; i++)
            {
                parameterNames[i] = $"@id{i}";
                cmd.Parameters.AddWithValue($"@id{i}", batch[i]);
            }

            cmd.CommandText =
                $"SELECT chunk_id FROM chunk_embeddings WHERE chunk_id IN ({string.Join(", ", parameterNames)})";

            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                found.Add(reader.GetString(0));
            }
        }

        return found;
    }
```

Note: the implementation batches internally as well as documenting that callers should. Belt and braces — the interface contract stays the same, and the 600-id test proves the store does not fall over on its own.

- [ ] **Step 7: Implement in `SqlServerVectorStore`**

Add after `DeleteByDocumentIdAsync`:

```csharp
    /// <inheritdoc />
    public async Task<long> CountAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        // COUNT_BIG returns bigint, so the scalar maps straight onto long.
        var sql = $"SELECT COUNT_BIG(*) FROM {FullTableName}";

        await using var command = new SqlCommand(sql, connection);
        var result = await command.ExecuteScalarAsync(cancellationToken);
        return result is long count ? count : 0L;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<string>> GetExistingChunkIdsAsync(
        IEnumerable<string> chunkIds,
        CancellationToken cancellationToken = default)
    {
        var ids = chunkIds.ToList();
        if (ids.Count == 0)
            return [];

        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        var found = new List<string>();

        // 500 per batch keeps well clear of the 2100 parameter ceiling.
        foreach (var batch in ids.Chunk(500))
        {
            var parameterNames = new string[batch.Length];

            await using var command = new SqlCommand { Connection = connection };
            for (var i = 0; i < batch.Length; i++)
            {
                parameterNames[i] = $"@id{i}";
                command.Parameters.AddWithValue($"@id{i}", batch[i]);
            }

            command.CommandText =
                $"SELECT chunk_id FROM {FullTableName} WHERE chunk_id IN ({string.Join(", ", parameterNames)})";

            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                found.Add(reader.GetString(0));
            }
        }

        return found;
    }
```

- [ ] **Step 8: Run tests to verify they pass**

Run: `dotnet test tests/Mjm.LocalDocs.Tests/Mjm.LocalDocs.Tests.csproj --filter "FullyQualifiedName~VectorStore"`
Expected: PASS. SQL Server cases skip when no server is reachable — that is the pre-existing behaviour of that suite, not a failure.

- [ ] **Step 9: Commit**

```bash
git add src/Mjm.LocalDocs.Core/Abstractions/IVectorStore.cs src/Mjm.LocalDocs.Infrastructure/VectorStore src/Mjm.LocalDocs.Infrastructure/Persistence/SqliteVectorStore.cs src/Mjm.LocalDocs.Infrastructure/Persistence/SqlServerVectorStore.cs tests/Mjm.LocalDocs.Tests/VectorStore
git commit -m "Add embedding count and existence probe to IVectorStore"
```

---

## Task 2: Contribution and count reads on IDocumentRepository

**Files:**
- Create: `src/Mjm.LocalDocs.Core/Models/Dashboard/DashboardReadModels.cs`
- Modify: `src/Mjm.LocalDocs.Core/Abstractions/IDocumentRepository.cs`
- Modify: `src/Mjm.LocalDocs.Infrastructure/Persistence/Repositories/EfCoreDocumentRepository.cs`
- Modify: `src/Mjm.LocalDocs.Infrastructure/VectorStore/InMemoryDocumentRepository.cs`
- Test: `tests/Mjm.LocalDocs.Tests/Repositories/DocumentRepositoryAggregateTests.cs`
- Test: `tests/Mjm.LocalDocs.Tests/Repositories/EfCoreDocumentRepositoryAggregateTests.cs`
- Test: `tests/Mjm.LocalDocs.Tests/Repositories/InMemoryDocumentRepositoryAggregateTests.cs`

**Interfaces:**
- Consumes: nothing from Task 1.
- Produces: `DocumentContribution(DateTimeOffset CreatedAt, bool IsNewDocument)`; `GetContributionsSinceAsync(DateTimeOffset, CancellationToken) → Task<IReadOnlyList<DocumentContribution>>`; `CountActiveDocumentsAsync(CancellationToken) → Task<int>`; `GetLastContributionAtAsync(CancellationToken) → Task<DateTimeOffset?>`; `GetActiveDocumentCountsByProjectAsync(CancellationToken) → Task<IReadOnlyDictionary<string, int>>`. Tasks 4 and 11 consume these.

- [ ] **Step 1: Create the read models file**

Create `src/Mjm.LocalDocs.Core/Models/Dashboard/DashboardReadModels.cs`. Write the whole file now — later tasks add nothing to it:

```csharp
namespace Mjm.LocalDocs.Core.Models.Dashboard;

/// <summary>
/// Granularity of the know-how growth chart.
/// </summary>
public enum GrowthGranularity
{
    /// <summary>Calendar weeks, starting Monday.</summary>
    Weekly,

    /// <summary>Calendar months.</summary>
    Monthly
}

/// <summary>
/// A single contribution event: one document version being created.
/// </summary>
/// <param name="CreatedAt">When the document version was created.</param>
/// <param name="IsNewDocument">
/// True when this is a brand-new document (no parent), false when it is a new
/// version of an existing one.
/// </param>
public sealed record DocumentContribution(DateTimeOffset CreatedAt, bool IsNewDocument);

/// <summary>
/// How many chunks an active document currently has.
/// </summary>
/// <param name="DocumentId">The document identifier.</param>
/// <param name="ProjectId">The owning project identifier.</param>
/// <param name="FileName">The document file name, for display.</param>
/// <param name="ChunkCount">Number of chunks persisted for the document.</param>
public sealed record DocumentChunkTally(
    string DocumentId,
    string ProjectId,
    string FileName,
    int ChunkCount);

/// <summary>
/// Which document a chunk belongs to.
/// </summary>
/// <param name="DocumentId">The owning document identifier.</param>
/// <param name="ChunkId">The chunk identifier.</param>
public sealed record ChunkOwnership(string DocumentId, string ChunkId);

/// <summary>
/// One bar of the growth chart.
/// </summary>
/// <param name="Start">Inclusive start of the bucket.</param>
/// <param name="Label">Display label for the axis.</param>
/// <param name="NewDocuments">Brand-new documents created in the bucket.</param>
/// <param name="NewVersions">New versions of existing documents created in the bucket.</param>
/// <param name="IsPartial">
/// True for the bucket still in progress, so a naturally low final bar is not read as a collapse.
/// </param>
public sealed record GrowthBucket(
    DateTimeOffset Start,
    string Label,
    int NewDocuments,
    int NewVersions,
    bool IsPartial);

/// <summary>
/// Searchability of the active document set.
/// </summary>
/// <param name="ActiveDocuments">Number of documents not superseded.</param>
/// <param name="FullyIndexed">Documents with at least one chunk and no missing embeddings.</param>
/// <param name="Broken">
/// Documents that are not fully searchable: zero chunks, or at least one chunk without an embedding.
/// </param>
public sealed record IndexHealth(
    int ActiveDocuments,
    int FullyIndexed,
    IReadOnlyList<DocumentChunkTally> Broken);

/// <summary>
/// Everything the dashboard renders in one payload.
/// </summary>
/// <param name="ActiveDocumentCount">Documents not superseded.</param>
/// <param name="NewInPeriod">Brand-new documents in the current rolling window.</param>
/// <param name="NewInPreviousPeriod">Brand-new documents in the preceding window of equal length.</param>
/// <param name="LastContributionAt">Most recent creation across all documents, or null when empty.</param>
/// <param name="Health">Index health of the active document set.</param>
/// <param name="Growth">Twelve buckets, oldest first.</param>
public sealed record DashboardMetrics(
    int ActiveDocumentCount,
    int NewInPeriod,
    int NewInPreviousPeriod,
    DateTimeOffset? LastContributionAt,
    IndexHealth Health,
    IReadOnlyList<GrowthBucket> Growth);
```

- [ ] **Step 2: Write the failing tests**

Every aggregate test is written **once**, in an abstract base class, and run against both
implementations. xUnit discovers inherited `[Fact]` methods on each concrete subclass, so this
produces one test case per implementation: a divergence fails only the subclass it affects.
That is exactly the parity signal, without copying test bodies between two files.

Create `tests/Mjm.LocalDocs.Tests/Repositories/DocumentRepositoryAggregateTests.cs`:

```csharp
using Mjm.LocalDocs.Core.Abstractions;
using Mjm.LocalDocs.Core.Models;

namespace Mjm.LocalDocs.Tests.Repositories;

/// <summary>
/// Dashboard aggregate reads, specified once and run against every
/// <see cref="IDocumentRepository"/> implementation via a concrete subclass.
/// </summary>
public abstract class DocumentRepositoryAggregateTests
{
    /// <summary>The repository under test, supplied by the concrete fixture.</summary>
    protected abstract IDocumentRepository Sut { get; }

    /// <summary>
    /// Ensures a project row exists, for fixtures backed by a store that enforces the
    /// Documents-to-Projects foreign key. The in-memory repository has no such constraint,
    /// so the default does nothing.
    /// </summary>
    protected virtual Task EnsureProjectAsync(string projectId) => Task.CompletedTask;

    // All timestamps are UTC on purpose: EF Core stores DateTimeOffset as TEXT on SQLite,
    // so MAX() is lexicographic and only agrees with chronological order at a fixed offset.
    protected static readonly DateTimeOffset Jan = new(2026, 1, 15, 12, 0, 0, TimeSpan.Zero);
    protected static readonly DateTimeOffset Feb = new(2026, 2, 10, 12, 0, 0, TimeSpan.Zero);
    protected static readonly DateTimeOffset Mar = new(2026, 3, 5, 12, 0, 0, TimeSpan.Zero);

    protected async Task SeedDocumentAsync(
        string id,
        string projectId = "proj-1",
        string? parentDocumentId = null,
        bool isSuperseded = false,
        DateTimeOffset? createdAt = null,
        int chunkCount = 0)
    {
        var document = new Document
        {
            Id = id,
            ProjectId = projectId,
            FileName = $"{id}.txt",
            FileExtension = ".txt",
            FileSizeBytes = 10,
            ExtractedText = "text",
            ParentDocumentId = parentDocumentId,
            IsSuperseded = isSuperseded,
            CreatedAt = createdAt ?? Jan
        };

        await EnsureProjectAsync(projectId);
        await Sut.AddDocumentAsync(document);

        if (chunkCount > 0)
        {
            var chunks = Enumerable.Range(0, chunkCount).Select(i => new DocumentChunk
            {
                Id = $"{id}_chunk_{i}",
                DocumentId = id,
                Content = $"chunk {i}",
                ChunkIndex = i,
                FileName = $"{id}.txt"
            });

            await Sut.AddChunksAsync(chunks);
        }
    }

    [Fact]
    public async Task GetContributionsSinceAsync_ReturnsOnlyDocumentsAtOrAfterTheBoundary()
    {
        await SeedDocumentAsync("doc-old", createdAt: Jan);
        await SeedDocumentAsync("doc-new", createdAt: Mar);

        var contributions = await Sut.GetContributionsSinceAsync(Feb);

        Assert.Single(contributions);
        Assert.Equal(Mar, contributions[0].CreatedAt);
    }

    [Fact]
    public async Task GetContributionsSinceAsync_MarksParentedDocumentsAsVersions()
    {
        await SeedDocumentAsync("doc-1", createdAt: Feb);
        await SeedDocumentAsync("doc-2", parentDocumentId: "doc-1", createdAt: Mar);

        var contributions = await Sut.GetContributionsSinceAsync(Jan);

        Assert.Equal(1, contributions.Count(c => c.IsNewDocument));
        Assert.Equal(1, contributions.Count(c => !c.IsNewDocument));
    }

    [Fact]
    public async Task GetContributionsSinceAsync_IncludesSupersededDocuments()
    {
        await SeedDocumentAsync("doc-1", isSuperseded: true, createdAt: Feb);

        var contributions = await Sut.GetContributionsSinceAsync(Jan);

        Assert.Single(contributions);
    }

    [Fact]
    public async Task CountActiveDocumentsAsync_ExcludesSuperseded()
    {
        await SeedDocumentAsync("doc-1");
        await SeedDocumentAsync("doc-2", isSuperseded: true);

        var count = await Sut.CountActiveDocumentsAsync();

        Assert.Equal(1, count);
    }

    [Fact]
    public async Task GetLastContributionAtAsync_WithNoDocuments_ReturnsNull()
    {
        var last = await Sut.GetLastContributionAtAsync();

        Assert.Null(last);
    }

    [Fact]
    public async Task GetLastContributionAtAsync_ReturnsMostRecentIncludingSuperseded()
    {
        await SeedDocumentAsync("doc-1", createdAt: Jan);
        await SeedDocumentAsync("doc-2", isSuperseded: true, createdAt: Mar);

        var last = await Sut.GetLastContributionAtAsync();

        Assert.Equal(Mar, last);
    }

    [Fact]
    public async Task GetActiveDocumentCountsByProjectAsync_GroupsAndExcludesSuperseded()
    {
        await SeedDocumentAsync("doc-1", projectId: "proj-a");
        await SeedDocumentAsync("doc-2", projectId: "proj-a");
        await SeedDocumentAsync("doc-3", projectId: "proj-b");
        await SeedDocumentAsync("doc-4", projectId: "proj-b", isSuperseded: true);

        var counts = await Sut.GetActiveDocumentCountsByProjectAsync();

        Assert.Equal(2, counts["proj-a"]);
        Assert.Equal(1, counts["proj-b"]);
    }
}
```

Create `tests/Mjm.LocalDocs.Tests/Repositories/EfCoreDocumentRepositoryAggregateTests.cs`:

```csharp
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Mjm.LocalDocs.Core.Abstractions;
using Mjm.LocalDocs.Infrastructure.Persistence;
using Mjm.LocalDocs.Infrastructure.Persistence.Entities;
using Mjm.LocalDocs.Infrastructure.Persistence.Repositories;

namespace Mjm.LocalDocs.Tests.Repositories;

/// <summary>
/// Runs the shared aggregate suite against <see cref="EfCoreDocumentRepository"/> on a
/// temporary SQLite database file that is cleaned up after each test.
/// </summary>
public sealed class EfCoreDocumentRepositoryAggregateTests
    : DocumentRepositoryAggregateTests, IDisposable
{
    private readonly string _testDbPath;
    private readonly LocalDocsDbContext _context;
    private readonly EfCoreDocumentRepository _repository;

    protected override IDocumentRepository Sut => _repository;

    public EfCoreDocumentRepositoryAggregateTests()
    {
        _testDbPath = Path.Combine(Path.GetTempPath(), $"efcore_docrepo_test_{Guid.NewGuid()}.db");

        var options = new DbContextOptionsBuilder<LocalDocsDbContext>()
            .UseSqlite($"Data Source={_testDbPath}")
            .Options;

        _context = new LocalDocsDbContext(options);
        _context.Database.EnsureCreated();
        _repository = new EfCoreDocumentRepository(_context);
    }

    /// <summary>
    /// Documents.ProjectId is a real foreign key here, and the shared seed helper invents
    /// project ids. Create the parent row rather than switching foreign keys off, so the
    /// fixture exercises the same referential integrity production does.
    /// </summary>
    protected override async Task EnsureProjectAsync(string projectId)
    {
        if (await _context.Projects.AnyAsync(p => p.Id == projectId))
            return;

        _context.Projects.Add(new ProjectEntity
        {
            Id = projectId,
            Name = projectId,
            CreatedAt = DateTimeOffset.UtcNow
        });

        await _context.SaveChangesAsync();
    }

    public void Dispose()
    {
        _context.Dispose();

        // Clear connection pool to release the file lock on Windows.
        SqliteConnection.ClearAllPools();
        Thread.Sleep(50);

        try
        {
            if (File.Exists(_testDbPath))
            {
                File.Delete(_testDbPath);
            }
        }
        catch (IOException)
        {
            // Best effort: a lingering temp file is harmless.
        }
    }
}
```

Create `tests/Mjm.LocalDocs.Tests/Repositories/InMemoryDocumentRepositoryAggregateTests.cs`:

```csharp
using Mjm.LocalDocs.Core.Abstractions;
using Mjm.LocalDocs.Infrastructure.VectorStore;

namespace Mjm.LocalDocs.Tests.Repositories;

/// <summary>
/// Runs the shared aggregate suite against <see cref="InMemoryDocumentRepository"/>,
/// proving behavioural parity with the EF Core implementation.
/// </summary>
public sealed class InMemoryDocumentRepositoryAggregateTests : DocumentRepositoryAggregateTests
{
    private readonly InMemoryDocumentRepository _repository = new();

    protected override IDocumentRepository Sut => _repository;
}
```
- [ ] **Step 3: Run tests to verify they fail**

Run: `dotnet test tests/Mjm.LocalDocs.Tests/Mjm.LocalDocs.Tests.csproj --filter "FullyQualifiedName~DocumentRepositoryAggregateTests"`
Expected: BUILD FAILURE — `'IDocumentRepository' does not contain a definition for 'GetContributionsSinceAsync'`.

- [ ] **Step 4: Add the interface members**

In `src/Mjm.LocalDocs.Core/Abstractions/IDocumentRepository.cs`, add `using Mjm.LocalDocs.Core.Models.Dashboard;` at the top, then add a new region before the closing brace:

```csharp
    #region Dashboard Aggregates

    /// <summary>
    /// Gets one record per document version created at or after the given instant.
    /// Superseded documents are included: the series counts creation events, not present state.
    /// </summary>
    /// <remarks>
    /// Implementations must apply the window in memory over a scalar column projection: the
    /// SQLite provider cannot translate a relational comparison on a <see cref="DateTimeOffset"/>.
    /// </remarks>
    /// <param name="since">Inclusive lower bound on <see cref="Document.CreatedAt"/>.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Contribution records, in no guaranteed order.</returns>
    Task<IReadOnlyList<DocumentContribution>> GetContributionsSinceAsync(
        DateTimeOffset since,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Counts documents that are not superseded.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The number of active documents.</returns>
    Task<int> CountActiveDocumentsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the most recent <see cref="Document.CreatedAt"/> across all documents,
    /// superseded ones included.
    /// </summary>
    /// <remarks>
    /// Implementations must aggregate in memory over a scalar column projection: the SQLite
    /// provider cannot apply <c>Max</c> to a <see cref="DateTimeOffset"/> column.
    /// </remarks>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The latest creation timestamp, or null when there are no documents.</returns>
    Task<DateTimeOffset?> GetLastContributionAtAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Counts active documents per project in a single query.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Project identifier to active document count. Projects with none are absent.</returns>
    Task<IReadOnlyDictionary<string, int>> GetActiveDocumentCountsByProjectAsync(
        CancellationToken cancellationToken = default);

    #endregion
```

- [ ] **Step 5: Implement in `EfCoreDocumentRepository`**

Add `using Mjm.LocalDocs.Core.Models.Dashboard;` at the top, then add a new region before `#region Private Helpers`:

```csharp
    #region Dashboard Aggregates

    /// <inheritdoc />
    public async Task<IReadOnlyList<DocumentContribution>> GetContributionsSinceAsync(
        DateTimeOffset since,
        CancellationToken cancellationToken = default)
    {
        // The projection translates; a `Where(d => d.CreatedAt >= since)` does not — the SQLite
        // provider refuses relational comparisons on DateTimeOffset. So read the two scalar
        // columns and apply the window in memory. This stays cheap because the projection never
        // touches FileContent; materialising DocumentEntity is the bug this method exists to avoid.
        var contributions = await _context.Documents
            .AsNoTracking()
            .Select(d => new DocumentContribution(d.CreatedAt, d.ParentDocumentId == null))
            .ToListAsync(cancellationToken);

        return contributions.Where(c => c.CreatedAt >= since).ToList();
    }

    /// <inheritdoc />
    public Task<int> CountActiveDocumentsAsync(CancellationToken cancellationToken = default)
    {
        return _context.Documents
            .AsNoTracking()
            .CountAsync(d => !d.IsSuperseded, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<DateTimeOffset?> GetLastContributionAtAsync(
        CancellationToken cancellationToken = default)
    {
        // MaxAsync on a DateTimeOffset column throws on the SQLite provider, so project the
        // single column and aggregate in memory.
        var timestamps = await _context.Documents
            .AsNoTracking()
            .Select(d => d.CreatedAt)
            .ToListAsync(cancellationToken);

        return timestamps.Count == 0 ? null : timestamps.Max();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyDictionary<string, int>> GetActiveDocumentCountsByProjectAsync(
        CancellationToken cancellationToken = default)
    {
        var rows = await _context.Documents
            .AsNoTracking()
            .Where(d => !d.IsSuperseded)
            .GroupBy(d => d.ProjectId)
            .Select(g => new { ProjectId = g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken);

        return rows.ToDictionary(r => r.ProjectId, r => r.Count);
    }

    #endregion
```

- [ ] **Step 6: Implement in `InMemoryDocumentRepository`**

Add `using Mjm.LocalDocs.Core.Models.Dashboard;` at the top, then add before the closing brace:

```csharp
    #region Dashboard Aggregates

    /// <inheritdoc />
    public Task<IReadOnlyList<DocumentContribution>> GetContributionsSinceAsync(
        DateTimeOffset since,
        CancellationToken cancellationToken = default)
    {
        var contributions = _documents.Values
            .Where(d => d.CreatedAt >= since)
            .Select(d => new DocumentContribution(d.CreatedAt, d.ParentDocumentId == null))
            .ToList();

        return Task.FromResult<IReadOnlyList<DocumentContribution>>(contributions);
    }

    /// <inheritdoc />
    public Task<int> CountActiveDocumentsAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(_documents.Values.Count(d => !d.IsSuperseded));
    }

    /// <inheritdoc />
    public Task<DateTimeOffset?> GetLastContributionAtAsync(CancellationToken cancellationToken = default)
    {
        if (_documents.IsEmpty)
            return Task.FromResult<DateTimeOffset?>(null);

        return Task.FromResult<DateTimeOffset?>(_documents.Values.Max(d => d.CreatedAt));
    }

    /// <inheritdoc />
    public Task<IReadOnlyDictionary<string, int>> GetActiveDocumentCountsByProjectAsync(
        CancellationToken cancellationToken = default)
    {
        var counts = _documents.Values
            .Where(d => !d.IsSuperseded)
            .GroupBy(d => d.ProjectId)
            .ToDictionary(g => g.Key, g => g.Count());

        return Task.FromResult<IReadOnlyDictionary<string, int>>(counts);
    }

    #endregion
```

- [ ] **Step 7: Run tests to verify they pass**

Run: `dotnet test tests/Mjm.LocalDocs.Tests/Mjm.LocalDocs.Tests.csproj --filter "FullyQualifiedName~DocumentRepositoryAggregateTests"`
Expected: PASS, 14 tests (7 per implementation).

- [ ] **Step 8: Commit**

```bash
git add src/Mjm.LocalDocs.Core/Models/Dashboard src/Mjm.LocalDocs.Core/Abstractions/IDocumentRepository.cs src/Mjm.LocalDocs.Infrastructure/Persistence/Repositories/EfCoreDocumentRepository.cs src/Mjm.LocalDocs.Infrastructure/VectorStore/InMemoryDocumentRepository.cs tests/Mjm.LocalDocs.Tests/Repositories
git commit -m "Add contribution and count aggregates to IDocumentRepository"
```

---

## Task 3: Chunk tally reads on IDocumentRepository

**Files:**
- Modify: `src/Mjm.LocalDocs.Core/Abstractions/IDocumentRepository.cs`
- Modify: `src/Mjm.LocalDocs.Infrastructure/Persistence/Repositories/EfCoreDocumentRepository.cs`
- Modify: `src/Mjm.LocalDocs.Infrastructure/VectorStore/InMemoryDocumentRepository.cs`
- Test: `tests/Mjm.LocalDocs.Tests/Repositories/DocumentRepositoryAggregateTests.cs` (append only)

**Interfaces:**
- Consumes: `DocumentChunkTally` and `ChunkOwnership` from Task 2's read models file.
- Produces: `CountChunksAsync(CancellationToken) → Task<long>`; `GetActiveDocumentChunkTalliesAsync(CancellationToken) → Task<IReadOnlyList<DocumentChunkTally>>`; `GetChunkOwnershipAsync(IEnumerable<string>, CancellationToken) → Task<IReadOnlyList<ChunkOwnership>>`. Task 4 consumes all three.

- [ ] **Step 1: Write the failing tests**

Append these to the abstract base `tests/Mjm.LocalDocs.Tests/Repositories/DocumentRepositoryAggregateTests.cs`.
Both concrete subclasses pick them up automatically — nothing is added to either subclass file.
The `SeedDocumentAsync` helper already takes `chunkCount`.

```csharp
    [Fact]
    public async Task CountChunksAsync_ReturnsTotalAcrossDocuments()
    {
        await SeedDocumentAsync("doc-1", chunkCount: 3);
        await SeedDocumentAsync("doc-2", chunkCount: 2);

        var count = await Sut.CountChunksAsync();

        Assert.Equal(5L, count);
    }

    [Fact]
    public async Task GetActiveDocumentChunkTalliesAsync_ExcludesSupersededAndReportsCounts()
    {
        await SeedDocumentAsync("doc-1", chunkCount: 3);
        await SeedDocumentAsync("doc-2", chunkCount: 0);
        await SeedDocumentAsync("doc-3", isSuperseded: true, chunkCount: 4);

        var tallies = await Sut.GetActiveDocumentChunkTalliesAsync();

        Assert.Equal(2, tallies.Count);
        Assert.Equal(3, tallies.Single(t => t.DocumentId == "doc-1").ChunkCount);
        Assert.Equal(0, tallies.Single(t => t.DocumentId == "doc-2").ChunkCount);
    }

    [Fact]
    public async Task GetActiveDocumentChunkTalliesAsync_CarriesProjectAndFileName()
    {
        await SeedDocumentAsync("doc-1", projectId: "proj-x", chunkCount: 1);

        var tallies = await Sut.GetActiveDocumentChunkTalliesAsync();

        var tally = Assert.Single(tallies);
        Assert.Equal("proj-x", tally.ProjectId);
        Assert.Equal("doc-1.txt", tally.FileName);
    }

    [Fact]
    public async Task GetChunkOwnershipAsync_ReturnsChunksPairedWithTheirDocument()
    {
        await SeedDocumentAsync("doc-1", chunkCount: 2);
        await SeedDocumentAsync("doc-2", chunkCount: 1);

        var ownership = await Sut.GetChunkOwnershipAsync(["doc-1"]);

        Assert.Equal(2, ownership.Count);
        Assert.All(ownership, o => Assert.Equal("doc-1", o.DocumentId));
        Assert.Contains(ownership, o => o.ChunkId == "doc-1_chunk_0");
    }

    [Fact]
    public async Task GetChunkOwnershipAsync_WithEmptyInput_ReturnsEmpty()
    {
        await SeedDocumentAsync("doc-1", chunkCount: 2);

        var ownership = await Sut.GetChunkOwnershipAsync([]);

        Assert.Empty(ownership);
    }
```
- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/Mjm.LocalDocs.Tests/Mjm.LocalDocs.Tests.csproj --filter "FullyQualifiedName~DocumentRepositoryAggregateTests"`
Expected: BUILD FAILURE — `'IDocumentRepository' does not contain a definition for 'CountChunksAsync'`.

- [ ] **Step 3: Add the interface members**

In `IDocumentRepository.cs`, inside the `#region Dashboard Aggregates` added in Task 2, append:

```csharp
    /// <summary>
    /// Counts all persisted chunks. In a healthy system this equals
    /// <see cref="IVectorStore.CountAsync"/>, because chunks exist only for active documents.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The total number of chunks.</returns>
    Task<long> CountChunksAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the chunk count of every active document, including documents with zero chunks.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>One tally per active document.</returns>
    Task<IReadOnlyList<DocumentChunkTally>> GetActiveDocumentChunkTalliesAsync(
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the chunks belonging to the given documents, each paired with its owner.
    /// </summary>
    /// <remarks>
    /// The owner is carried explicitly rather than parsed back out of the
    /// <c>{documentId}_chunk_{index}</c> identifier convention.
    /// </remarks>
    /// <param name="documentIds">The documents whose chunks are wanted.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Chunk-to-document pairs, in no guaranteed order.</returns>
    Task<IReadOnlyList<ChunkOwnership>> GetChunkOwnershipAsync(
        IEnumerable<string> documentIds,
        CancellationToken cancellationToken = default);
```

- [ ] **Step 4: Implement in `EfCoreDocumentRepository`**

Append inside its `#region Dashboard Aggregates`:

```csharp
    /// <inheritdoc />
    public Task<long> CountChunksAsync(CancellationToken cancellationToken = default)
    {
        return _context.DocumentChunks
            .AsNoTracking()
            .LongCountAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<DocumentChunkTally>> GetActiveDocumentChunkTalliesAsync(
        CancellationToken cancellationToken = default)
    {
        // d.Chunks.Count becomes a correlated COUNT subquery: no chunk rows are materialised.
        return await _context.Documents
            .AsNoTracking()
            .Where(d => !d.IsSuperseded)
            .Select(d => new DocumentChunkTally(d.Id, d.ProjectId, d.FileName, d.Chunks.Count))
            .ToListAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ChunkOwnership>> GetChunkOwnershipAsync(
        IEnumerable<string> documentIds,
        CancellationToken cancellationToken = default)
    {
        var ids = documentIds.ToList();
        if (ids.Count == 0)
            return [];

        return await _context.DocumentChunks
            .AsNoTracking()
            .Where(c => ids.Contains(c.DocumentId))
            .Select(c => new ChunkOwnership(c.DocumentId, c.Id))
            .ToListAsync(cancellationToken);
    }
```

- [ ] **Step 5: Implement in `InMemoryDocumentRepository`**

Append inside its `#region Dashboard Aggregates`:

```csharp
    /// <inheritdoc />
    public Task<long> CountChunksAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult((long)_chunks.Count);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<DocumentChunkTally>> GetActiveDocumentChunkTalliesAsync(
        CancellationToken cancellationToken = default)
    {
        var chunkCounts = _chunks.Values
            .GroupBy(c => c.DocumentId)
            .ToDictionary(g => g.Key, g => g.Count());

        var tallies = _documents.Values
            .Where(d => !d.IsSuperseded)
            .Select(d => new DocumentChunkTally(
                d.Id,
                d.ProjectId,
                d.FileName,
                chunkCounts.TryGetValue(d.Id, out var count) ? count : 0))
            .ToList();

        return Task.FromResult<IReadOnlyList<DocumentChunkTally>>(tallies);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<ChunkOwnership>> GetChunkOwnershipAsync(
        IEnumerable<string> documentIds,
        CancellationToken cancellationToken = default)
    {
        var ids = documentIds.ToHashSet(StringComparer.Ordinal);
        if (ids.Count == 0)
            return Task.FromResult<IReadOnlyList<ChunkOwnership>>([]);

        var ownership = _chunks.Values
            .Where(c => ids.Contains(c.DocumentId))
            .Select(c => new ChunkOwnership(c.DocumentId, c.Id))
            .ToList();

        return Task.FromResult<IReadOnlyList<ChunkOwnership>>(ownership);
    }
```

- [ ] **Step 6: Run tests to verify they pass**

Run: `dotnet test tests/Mjm.LocalDocs.Tests/Mjm.LocalDocs.Tests.csproj --filter "FullyQualifiedName~DocumentRepositoryAggregateTests"`
Expected: PASS, 24 tests (12 per implementation).

- [ ] **Step 7: Commit**

```bash
git add src/Mjm.LocalDocs.Core/Abstractions/IDocumentRepository.cs src/Mjm.LocalDocs.Infrastructure/Persistence/Repositories/EfCoreDocumentRepository.cs src/Mjm.LocalDocs.Infrastructure/VectorStore/InMemoryDocumentRepository.cs tests/Mjm.LocalDocs.Tests/Repositories
git commit -m "Add chunk tally aggregates to IDocumentRepository"
```

---

## Task 4: DashboardMetricsService — index health fast path and probe

**Files:**
- Create: `src/Mjm.LocalDocs.Core/Services/DashboardMetricsService.cs`
- Test: `tests/Mjm.LocalDocs.Tests/Services/DashboardMetricsServiceTests.cs`

**Interfaces:**
- Consumes: `CountChunksAsync`, `GetActiveDocumentChunkTalliesAsync`, `GetChunkOwnershipAsync` (Task 3); `IVectorStore.CountAsync`, `GetExistingChunkIdsAsync` (Task 1); `IndexHealth`, `DocumentChunkTally` (Task 2).
- Produces: `DashboardMetricsService(IDocumentRepository repository, IVectorStore vectorStore)` and `GetIndexHealthAsync(bool forceFullReconciliation = false, CancellationToken) → Task<IndexHealth>`. Task 5 extends the constructor with an optional `TimeProvider`; Tasks 10 and 11 consume the service.

This task builds the health half of the service. Task 5 adds the growth half. The order matters:
health has no dependency on bucketing, so building it first means the service never contains a
stubbed method or an unused constructor parameter at any point.

- [ ] **Step 1: Write the failing tests**

Create `tests/Mjm.LocalDocs.Tests/Services/DashboardMetricsServiceTests.cs`:

```csharp
using Mjm.LocalDocs.Core.Abstractions;
using Mjm.LocalDocs.Core.Models.Dashboard;
using Mjm.LocalDocs.Core.Services;
using NSubstitute;

namespace Mjm.LocalDocs.Tests.Services;

/// <summary>
/// Unit tests for <see cref="DashboardMetricsService"/>.
/// </summary>
public sealed class DashboardMetricsServiceTests
{
    private readonly IDocumentRepository _repository = Substitute.For<IDocumentRepository>();
    private readonly IVectorStore _vectorStore = Substitute.For<IVectorStore>();

    private DashboardMetricsService CreateSut() => new(_repository, _vectorStore);

    private static DocumentChunkTally Tally(string id, int chunkCount) =>
        new(id, "proj-1", $"{id}.txt", chunkCount);

    [Fact]
    public async Task GetIndexHealthAsync_FlagsDocumentsWithZeroChunks()
    {
        _repository.GetActiveDocumentChunkTalliesAsync(Arg.Any<CancellationToken>())
            .Returns([Tally("doc-1", 3), Tally("doc-2", 0)]);
        _repository.CountChunksAsync(Arg.Any<CancellationToken>()).Returns(3L);
        _vectorStore.CountAsync(Arg.Any<CancellationToken>()).Returns(3L);

        var health = await CreateSut().GetIndexHealthAsync();

        Assert.Equal(2, health.ActiveDocuments);
        Assert.Equal(1, health.FullyIndexed);
        Assert.Equal("doc-2", Assert.Single(health.Broken).DocumentId);
    }

    [Fact]
    public async Task GetIndexHealthAsync_WhenCountsAgree_SkipsTheProbeEntirely()
    {
        _repository.GetActiveDocumentChunkTalliesAsync(Arg.Any<CancellationToken>())
            .Returns([Tally("doc-1", 3)]);
        _repository.CountChunksAsync(Arg.Any<CancellationToken>()).Returns(3L);
        _vectorStore.CountAsync(Arg.Any<CancellationToken>()).Returns(3L);

        var health = await CreateSut().GetIndexHealthAsync();

        Assert.Empty(health.Broken);
        await _vectorStore.DidNotReceive().GetExistingChunkIdsAsync(
            Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetIndexHealthAsync_WhenCountsDiverge_FindsDocumentsMissingEmbeddings()
    {
        _repository.GetActiveDocumentChunkTalliesAsync(Arg.Any<CancellationToken>())
            .Returns([Tally("doc-1", 2), Tally("doc-2", 2)]);
        _repository.CountChunksAsync(Arg.Any<CancellationToken>()).Returns(4L);
        _vectorStore.CountAsync(Arg.Any<CancellationToken>()).Returns(2L);

        _repository.GetChunkOwnershipAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>())
            .Returns([
                new ChunkOwnership("doc-1", "doc-1_chunk_0"),
                new ChunkOwnership("doc-1", "doc-1_chunk_1"),
                new ChunkOwnership("doc-2", "doc-2_chunk_0"),
                new ChunkOwnership("doc-2", "doc-2_chunk_1")
            ]);

        // Only doc-1's embeddings made it into the store.
        _vectorStore.GetExistingChunkIdsAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>())
            .Returns(["doc-1_chunk_0", "doc-1_chunk_1"]);

        var health = await CreateSut().GetIndexHealthAsync();

        Assert.Equal("doc-2", Assert.Single(health.Broken).DocumentId);
        Assert.Equal(1, health.FullyIndexed);
    }

    [Fact]
    public async Task GetIndexHealthAsync_TreatsPartiallyEmbeddedDocumentAsBroken()
    {
        _repository.GetActiveDocumentChunkTalliesAsync(Arg.Any<CancellationToken>())
            .Returns([Tally("doc-1", 2)]);
        _repository.CountChunksAsync(Arg.Any<CancellationToken>()).Returns(2L);
        _vectorStore.CountAsync(Arg.Any<CancellationToken>()).Returns(1L);

        _repository.GetChunkOwnershipAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>())
            .Returns([
                new ChunkOwnership("doc-1", "doc-1_chunk_0"),
                new ChunkOwnership("doc-1", "doc-1_chunk_1")
            ]);
        _vectorStore.GetExistingChunkIdsAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>())
            .Returns(["doc-1_chunk_0"]);

        var health = await CreateSut().GetIndexHealthAsync();

        Assert.Equal("doc-1", Assert.Single(health.Broken).DocumentId);
        Assert.Equal(0, health.FullyIndexed);
    }

    [Fact]
    public async Task GetIndexHealthAsync_WithForceFlag_ProbesEvenWhenCountsAgree()
    {
        _repository.GetActiveDocumentChunkTalliesAsync(Arg.Any<CancellationToken>())
            .Returns([Tally("doc-1", 1)]);
        _repository.CountChunksAsync(Arg.Any<CancellationToken>()).Returns(1L);
        _vectorStore.CountAsync(Arg.Any<CancellationToken>()).Returns(1L);

        _repository.GetChunkOwnershipAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>())
            .Returns([new ChunkOwnership("doc-1", "doc-1_chunk_0")]);
        // An orphan embedding elsewhere made the totals agree while this chunk has none.
        _vectorStore.GetExistingChunkIdsAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>())
            .Returns([]);

        var health = await CreateSut().GetIndexHealthAsync(forceFullReconciliation: true);

        Assert.Equal("doc-1", Assert.Single(health.Broken).DocumentId);
    }

    [Fact]
    public async Task GetIndexHealthAsync_ProbesInBatchesOfFiveHundred()
    {
        _repository.GetActiveDocumentChunkTalliesAsync(Arg.Any<CancellationToken>())
            .Returns([Tally("doc-1", 1200)]);
        _repository.CountChunksAsync(Arg.Any<CancellationToken>()).Returns(1200L);
        _vectorStore.CountAsync(Arg.Any<CancellationToken>()).Returns(1199L);

        var ownership = Enumerable.Range(0, 1200)
            .Select(i => new ChunkOwnership("doc-1", $"doc-1_chunk_{i}"))
            .ToList();
        _repository.GetChunkOwnershipAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>())
            .Returns(ownership);
        _vectorStore.GetExistingChunkIdsAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>())
            .Returns([]);

        await CreateSut().GetIndexHealthAsync();

        // 1200 ids => 500 + 500 + 200
        await _vectorStore.Received(3).GetExistingChunkIdsAsync(
            Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetIndexHealthAsync_WithNoActiveDocuments_ReportsAllClear()
    {
        _repository.GetActiveDocumentChunkTalliesAsync(Arg.Any<CancellationToken>()).Returns([]);
        _repository.CountChunksAsync(Arg.Any<CancellationToken>()).Returns(0L);
        _vectorStore.CountAsync(Arg.Any<CancellationToken>()).Returns(0L);

        var health = await CreateSut().GetIndexHealthAsync();

        Assert.Equal(0, health.ActiveDocuments);
        Assert.Equal(0, health.FullyIndexed);
        Assert.Empty(health.Broken);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/Mjm.LocalDocs.Tests/Mjm.LocalDocs.Tests.csproj --filter "FullyQualifiedName~DashboardMetricsServiceTests"`
Expected: BUILD FAILURE — `The type or namespace name 'DashboardMetricsService' could not be found`.

- [ ] **Step 3: Write the implementation**

Create `src/Mjm.LocalDocs.Core/Services/DashboardMetricsService.cs`:

```csharp
using Mjm.LocalDocs.Core.Abstractions;
using Mjm.LocalDocs.Core.Models.Dashboard;

namespace Mjm.LocalDocs.Core.Services;

/// <summary>
/// Computes the home dashboard's growth series and health indicators.
/// </summary>
public sealed class DashboardMetricsService
{
    private const int ProbeBatchSize = 500;

    private readonly IDocumentRepository _repository;
    private readonly IVectorStore _vectorStore;

    /// <summary>
    /// Creates a new <see cref="DashboardMetricsService"/>.
    /// </summary>
    /// <param name="repository">Document repository.</param>
    /// <param name="vectorStore">Vector store, for the embedding count and existence probe.</param>
    public DashboardMetricsService(
        IDocumentRepository repository,
        IVectorStore vectorStore)
    {
        _repository = repository;
        _vectorStore = vectorStore;
    }

    /// <summary>
    /// Reports which active documents are not fully searchable.
    /// </summary>
    /// <param name="forceFullReconciliation">
    /// When true, probes every chunk instead of trusting the count comparison.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The index health of the active document set.</returns>
    public async Task<IndexHealth> GetIndexHealthAsync(
        bool forceFullReconciliation = false,
        CancellationToken cancellationToken = default)
    {
        var tallies = await _repository.GetActiveDocumentChunkTalliesAsync(cancellationToken);
        var activeCount = tallies.Count;

        var broken = tallies.Where(t => t.ChunkCount == 0).ToList();
        var chunked = tallies.Where(t => t.ChunkCount > 0).ToList();

        // Fast path: two cheap counts. Reconciling every chunk on every page load would
        // cost O(total chunks), and in a healthy system it would find nothing.
        var chunkCount = await _repository.CountChunksAsync(cancellationToken);
        var embeddingCount = await _vectorStore.CountAsync(cancellationToken);

        if (forceFullReconciliation || chunkCount != embeddingCount)
        {
            broken.AddRange(await FindDocumentsMissingEmbeddingsAsync(chunked, cancellationToken));
        }

        var ordered = broken
            .OrderBy(t => t.FileName, StringComparer.CurrentCulture)
            .ToList();

        return new IndexHealth(activeCount, activeCount - ordered.Count, ordered);
    }

    private async Task<IReadOnlyList<DocumentChunkTally>> FindDocumentsMissingEmbeddingsAsync(
        IReadOnlyList<DocumentChunkTally> candidates,
        CancellationToken cancellationToken)
    {
        if (candidates.Count == 0)
            return [];

        var ownership = await _repository.GetChunkOwnershipAsync(
            candidates.Select(c => c.DocumentId),
            cancellationToken);

        if (ownership.Count == 0)
            return [];

        var existing = new HashSet<string>(StringComparer.Ordinal);
        foreach (var batch in ownership.Select(o => o.ChunkId).Chunk(ProbeBatchSize))
        {
            var found = await _vectorStore.GetExistingChunkIdsAsync(batch, cancellationToken);
            foreach (var chunkId in found)
            {
                existing.Add(chunkId);
            }
        }

        // A document with even one unembedded chunk is only partly searchable, which is a
        // trap rather than a partial success — so it counts as broken.
        var affected = ownership
            .Where(o => !existing.Contains(o.ChunkId))
            .Select(o => o.DocumentId)
            .ToHashSet(StringComparer.Ordinal);

        return candidates.Where(c => affected.Contains(c.DocumentId)).ToList();
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/Mjm.LocalDocs.Tests/Mjm.LocalDocs.Tests.csproj --filter "FullyQualifiedName~DashboardMetricsServiceTests"`
Expected: PASS, 7 tests.

- [ ] **Step 5: Commit**

```bash
git add src/Mjm.LocalDocs.Core/Services/DashboardMetricsService.cs tests/Mjm.LocalDocs.Tests/Services/DashboardMetricsServiceTests.cs
git commit -m "Add index health reconciliation to dashboard metrics service"
```

---

## Task 5: DashboardMetricsService — growth buckets and rolling KPIs

**Files:**
- Modify: `src/Mjm.LocalDocs.Core/Services/DashboardMetricsService.cs`
- Test: `tests/Mjm.LocalDocs.Tests/Services/DashboardMetricsServiceTests.cs`

**Interfaces:**
- Consumes: `GetContributionsSinceAsync`, `CountActiveDocumentsAsync`, `GetLastContributionAtAsync` (Task 2); `GetIndexHealthAsync` (Task 4).
- Produces: an optional third constructor parameter `TimeProvider? timeProvider = null`, and `GetMetricsAsync(GrowthGranularity, CancellationToken) → Task<DashboardMetrics>`. Tasks 8 and 11 consume these.

The constructor gains the clock in this task rather than in Task 4, so the field is used the
moment it exists. The parameter is optional, so Task 4's `CreateSut()` keeps compiling; Step 1
below routes it through a fixed clock so bucket boundaries are deterministic.

- [ ] **Step 1: Write the failing tests**

In `tests/Mjm.LocalDocs.Tests/Services/DashboardMetricsServiceTests.cs`, add the fixed clock and
the `Now` anchor, and change `CreateSut()` to use them. Replace the existing `CreateSut()` line
with:

```csharp
    private static readonly DateTimeOffset Now = new(2026, 8, 20, 10, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// Fixed clock so bucket boundaries are deterministic, without taking a
    /// dependency on Microsoft.Extensions.TimeProvider.Testing.
    /// </summary>
    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private DashboardMetricsService CreateSut() =>
        new(_repository, _vectorStore, new FixedTimeProvider(Now));

    private void GivenContributions(params DocumentContribution[] contributions)
    {
        _repository.GetContributionsSinceAsync(Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(contributions.ToList());
        _repository.GetActiveDocumentChunkTalliesAsync(Arg.Any<CancellationToken>())
            .Returns([]);
        _repository.CountChunksAsync(Arg.Any<CancellationToken>()).Returns(0L);
        _vectorStore.CountAsync(Arg.Any<CancellationToken>()).Returns(0L);
    }
```

Then append these tests to the same class:

```csharp
    [Fact]
    public async Task GetMetricsAsync_Monthly_ReturnsTwelveBucketsOldestFirst()
    {
        GivenContributions();

        var metrics = await CreateSut().GetMetricsAsync(GrowthGranularity.Monthly);

        Assert.Equal(12, metrics.Growth.Count);
        Assert.Equal(new DateTimeOffset(2025, 9, 1, 0, 0, 0, TimeSpan.Zero), metrics.Growth[0].Start);
        Assert.Equal(new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.Zero), metrics.Growth[11].Start);
    }

    [Fact]
    public async Task GetMetricsAsync_Monthly_MarksOnlyTheCurrentBucketPartial()
    {
        GivenContributions();

        var metrics = await CreateSut().GetMetricsAsync(GrowthGranularity.Monthly);

        Assert.True(metrics.Growth[11].IsPartial);
        Assert.All(metrics.Growth.Take(11), b => Assert.False(b.IsPartial));
    }

    [Fact]
    public async Task GetMetricsAsync_Monthly_SplitsNewDocumentsFromVersions()
    {
        GivenContributions(
            new DocumentContribution(new DateTimeOffset(2026, 8, 3, 9, 0, 0, TimeSpan.Zero), true),
            new DocumentContribution(new DateTimeOffset(2026, 8, 4, 9, 0, 0, TimeSpan.Zero), true),
            new DocumentContribution(new DateTimeOffset(2026, 8, 5, 9, 0, 0, TimeSpan.Zero), false));

        var metrics = await CreateSut().GetMetricsAsync(GrowthGranularity.Monthly);

        Assert.Equal(2, metrics.Growth[11].NewDocuments);
        Assert.Equal(1, metrics.Growth[11].NewVersions);
    }

    [Fact]
    public async Task GetMetricsAsync_Monthly_KeepsEmptyBucketsSoStallsStayVisible()
    {
        GivenContributions(
            new DocumentContribution(new DateTimeOffset(2026, 3, 10, 9, 0, 0, TimeSpan.Zero), true));

        var metrics = await CreateSut().GetMetricsAsync(GrowthGranularity.Monthly);

        var march = metrics.Growth.Single(b => b.Start.Month == 3 && b.Start.Year == 2026);
        Assert.Equal(1, march.NewDocuments);
        Assert.Equal(11, metrics.Growth.Count(b => b.NewDocuments == 0 && b.NewVersions == 0));
    }

    [Fact]
    public async Task GetMetricsAsync_Weekly_BucketsStartOnMonday()
    {
        GivenContributions();

        var metrics = await CreateSut().GetMetricsAsync(GrowthGranularity.Weekly);

        Assert.Equal(12, metrics.Growth.Count);
        Assert.All(metrics.Growth, b => Assert.Equal(DayOfWeek.Monday, b.Start.DayOfWeek));
        // 2026-08-20 is a Thursday, so the current week starts Monday 2026-08-17.
        Assert.Equal(new DateTimeOffset(2026, 8, 17, 0, 0, 0, TimeSpan.Zero), metrics.Growth[11].Start);
    }

    [Fact]
    public async Task GetMetricsAsync_Monthly_ComparesRollingThirtyDayWindows()
    {
        GivenContributions(
            // Inside the last 30 days (on or after 2026-07-21).
            new DocumentContribution(new DateTimeOffset(2026, 8, 10, 9, 0, 0, TimeSpan.Zero), true),
            new DocumentContribution(new DateTimeOffset(2026, 8, 1, 9, 0, 0, TimeSpan.Zero), true),
            // Inside the previous 30 days (2026-06-21 .. 2026-07-20).
            new DocumentContribution(new DateTimeOffset(2026, 7, 1, 9, 0, 0, TimeSpan.Zero), true),
            // A version, which must not count towards either window.
            new DocumentContribution(new DateTimeOffset(2026, 8, 12, 9, 0, 0, TimeSpan.Zero), false));

        var metrics = await CreateSut().GetMetricsAsync(GrowthGranularity.Monthly);

        Assert.Equal(2, metrics.NewInPeriod);
        Assert.Equal(1, metrics.NewInPreviousPeriod);
    }

    [Fact]
    public async Task GetMetricsAsync_Weekly_ComparesRollingSevenDayWindows()
    {
        GivenContributions(
            new DocumentContribution(new DateTimeOffset(2026, 8, 18, 9, 0, 0, TimeSpan.Zero), true),
            new DocumentContribution(new DateTimeOffset(2026, 8, 9, 9, 0, 0, TimeSpan.Zero), true));

        var metrics = await CreateSut().GetMetricsAsync(GrowthGranularity.Weekly);

        Assert.Equal(1, metrics.NewInPeriod);
        Assert.Equal(1, metrics.NewInPreviousPeriod);
    }

    [Fact]
    public async Task GetMetricsAsync_SurfacesLastContributionFromOutsideTheChartWindow()
    {
        GivenContributions();
        var ancient = new DateTimeOffset(2024, 2, 1, 9, 0, 0, TimeSpan.Zero);
        _repository.GetLastContributionAtAsync(Arg.Any<CancellationToken>()).Returns(ancient);

        var metrics = await CreateSut().GetMetricsAsync(GrowthGranularity.Monthly);

        Assert.Equal(ancient, metrics.LastContributionAt);
    }

    [Fact]
    public async Task GetMetricsAsync_WithEmptyKnowledgeBase_ReportsNullLastContribution()
    {
        GivenContributions();
        _repository.GetLastContributionAtAsync(Arg.Any<CancellationToken>())
            .Returns((DateTimeOffset?)null);

        var metrics = await CreateSut().GetMetricsAsync(GrowthGranularity.Monthly);

        Assert.Null(metrics.LastContributionAt);
        Assert.Equal(0, metrics.ActiveDocumentCount);
    }

    [Fact]
    public async Task GetMetricsAsync_ReadsActiveCountFromTheRepository()
    {
        GivenContributions();
        _repository.CountActiveDocumentsAsync(Arg.Any<CancellationToken>()).Returns(412);

        var metrics = await CreateSut().GetMetricsAsync(GrowthGranularity.Monthly);

        Assert.Equal(412, metrics.ActiveDocumentCount);
    }

    [Fact]
    public async Task GetMetricsAsync_CarriesIndexHealthIntoThePayload()
    {
        GivenContributions();
        _repository.GetActiveDocumentChunkTalliesAsync(Arg.Any<CancellationToken>())
            .Returns([Tally("doc-1", 0)]);
        _repository.CountChunksAsync(Arg.Any<CancellationToken>()).Returns(0L);
        _vectorStore.CountAsync(Arg.Any<CancellationToken>()).Returns(0L);

        var metrics = await CreateSut().GetMetricsAsync(GrowthGranularity.Monthly);

        Assert.Equal("doc-1", Assert.Single(metrics.Health.Broken).DocumentId);
    }

    [Fact]
    public async Task GetMetricsAsync_Monthly_SplitsABucketBoundaryInstantIntoTheLaterBucket()
    {
        var augustStart = new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.Zero);

        GivenContributions(
            // Exactly the August bucket's start instant.
            new DocumentContribution(augustStart, true),
            // One tick earlier, so still inside July.
            new DocumentContribution(augustStart.AddTicks(-1), true));

        var metrics = await CreateSut().GetMetricsAsync(GrowthGranularity.Monthly);

        // Buckets are half-open [start, end), so the boundary instant belongs to August alone:
        // one contribution each, never both in one bucket and never dropped from both.
        Assert.Equal(1, metrics.Growth[10].NewDocuments);
        Assert.Equal(1, metrics.Growth[11].NewDocuments);
    }

    [Fact]
    public async Task GetMetricsAsync_Monthly_CountsAWindowBoundaryInstantAsCurrent()
    {
        // The clock is 2026-08-20T10:00:00Z, so the rolling 30-day window starts exactly here.
        var windowStart = new DateTimeOffset(2026, 7, 21, 10, 0, 0, TimeSpan.Zero);

        GivenContributions(
            new DocumentContribution(windowStart, true),
            new DocumentContribution(windowStart.AddTicks(-1), true));

        var metrics = await CreateSut().GetMetricsAsync(GrowthGranularity.Monthly);

        // The window is half-open too: the boundary instant is current, one tick earlier is previous.
        Assert.Equal(1, metrics.NewInPeriod);
        Assert.Equal(1, metrics.NewInPreviousPeriod);
    }
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/Mjm.LocalDocs.Tests/Mjm.LocalDocs.Tests.csproj --filter "FullyQualifiedName~DashboardMetricsServiceTests"`
Expected: BUILD FAILURE — no constructor accepting three arguments, and `'DashboardMetricsService' does not contain a definition for 'GetMetricsAsync'`.

- [ ] **Step 3: Extend the service**

In `src/Mjm.LocalDocs.Core/Services/DashboardMetricsService.cs`:

Add a class-level remark above the type declaration explaining the in-memory fold:

```csharp
/// <remarks>
/// Bucketing happens in memory rather than in SQL: date bucketing is provider-specific
/// (<c>strftime</c> on SQLite, <c>DATEPART</c> on SQL Server) and this application supports
/// both, so a C# fold keeps the service provider-agnostic.
/// </remarks>
```

Add `using System.Globalization;` at the top, add the bucket-count constant beside `ProbeBatchSize`:

```csharp
    private const int BucketCount = 12;
```

Add the field beside the other two:

```csharp
    private readonly TimeProvider _timeProvider;
```

Replace the constructor with:

```csharp
    /// <summary>
    /// Creates a new <see cref="DashboardMetricsService"/>.
    /// </summary>
    /// <param name="repository">Document repository.</param>
    /// <param name="vectorStore">Vector store, for the embedding count and existence probe.</param>
    /// <param name="timeProvider">Clock. Defaults to <see cref="TimeProvider.System"/>.</param>
    public DashboardMetricsService(
        IDocumentRepository repository,
        IVectorStore vectorStore,
        TimeProvider? timeProvider = null)
    {
        _repository = repository;
        _vectorStore = vectorStore;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }
```

Add `GetMetricsAsync` immediately above `GetIndexHealthAsync`:

```csharp
    /// <summary>
    /// Gets everything the dashboard renders.
    /// </summary>
    /// <param name="granularity">Bucket size for the growth chart.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The dashboard payload.</returns>
    public async Task<DashboardMetrics> GetMetricsAsync(
        GrowthGranularity granularity,
        CancellationToken cancellationToken = default)
    {
        var now = _timeProvider.GetUtcNow();
        var firstBucketStart = GetFirstBucketStart(now, granularity);

        var contributions = await _repository.GetContributionsSinceAsync(firstBucketStart, cancellationToken);
        var growth = BuildBuckets(contributions, firstBucketStart, now, granularity);

        // Rolling windows, not calendar buckets: comparing an in-progress month against a
        // complete one would report a fictitious drop on the third of every month.
        var windowDays = granularity == GrowthGranularity.Weekly ? 7 : 30;
        var currentWindowStart = now.AddDays(-windowDays);
        var previousWindowStart = now.AddDays(-windowDays * 2);

        var newInPeriod = contributions.Count(c =>
            c.IsNewDocument && c.CreatedAt >= currentWindowStart);

        var newInPreviousPeriod = contributions.Count(c =>
            c.IsNewDocument && c.CreatedAt >= previousWindowStart && c.CreatedAt < currentWindowStart);

        var activeCount = await _repository.CountActiveDocumentsAsync(cancellationToken);
        var lastContribution = await _repository.GetLastContributionAtAsync(cancellationToken);
        var health = await GetIndexHealthAsync(forceFullReconciliation: false, cancellationToken);

        return new DashboardMetrics(
            activeCount,
            newInPeriod,
            newInPreviousPeriod,
            lastContribution,
            health,
            growth);
    }
```

Add these private helpers after `FindDocumentsMissingEmbeddingsAsync`:

```csharp
    private static DateTimeOffset GetFirstBucketStart(
        DateTimeOffset now,
        GrowthGranularity granularity)
    {
        if (granularity == GrowthGranularity.Monthly)
        {
            var startOfMonth = new DateTimeOffset(now.Year, now.Month, 1, 0, 0, 0, now.Offset);
            return startOfMonth.AddMonths(-(BucketCount - 1));
        }

        return StartOfWeek(now).AddDays(-7 * (BucketCount - 1));
    }

    private static DateTimeOffset StartOfWeek(DateTimeOffset value)
    {
        var daysSinceMonday = ((int)value.DayOfWeek - (int)DayOfWeek.Monday + 7) % 7;
        return new DateTimeOffset(value.Date.AddDays(-daysSinceMonday), value.Offset);
    }

    private static IReadOnlyList<GrowthBucket> BuildBuckets(
        IReadOnlyList<DocumentContribution> contributions,
        DateTimeOffset firstBucketStart,
        DateTimeOffset now,
        GrowthGranularity granularity)
    {
        var buckets = new List<GrowthBucket>(BucketCount);

        for (var i = 0; i < BucketCount; i++)
        {
            var start = granularity == GrowthGranularity.Monthly
                ? firstBucketStart.AddMonths(i)
                : firstBucketStart.AddDays(7 * i);

            var end = granularity == GrowthGranularity.Monthly
                ? start.AddMonths(1)
                : start.AddDays(7);

            var inBucket = contributions
                .Where(c => c.CreatedAt >= start && c.CreatedAt < end)
                .ToList();

            // Empty buckets are kept, never collapsed: a five-month stall must stay visible.
            buckets.Add(new GrowthBucket(
                start,
                FormatLabel(start, granularity),
                inBucket.Count(c => c.IsNewDocument),
                inBucket.Count(c => !c.IsNewDocument),
                now < end));
        }

        return buckets;
    }

    private static string FormatLabel(DateTimeOffset start, GrowthGranularity granularity)
    {
        return granularity == GrowthGranularity.Monthly
            ? start.ToString("MMM", CultureInfo.CurrentCulture)
            : start.ToString("dd/MM", CultureInfo.CurrentCulture);
    }
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/Mjm.LocalDocs.Tests/Mjm.LocalDocs.Tests.csproj --filter "FullyQualifiedName~DashboardMetricsServiceTests"`
Expected: PASS, 20 tests.

- [ ] **Step 5: Commit**

```bash
git add src/Mjm.LocalDocs.Core/Services/DashboardMetricsService.cs tests/Mjm.LocalDocs.Tests/Services/DashboardMetricsServiceTests.cs
git commit -m "Add growth buckets and rolling KPIs to dashboard metrics service"
```
## Task 6: Stop silent half-indexing on document add

**Files:**
- Create: `src/Mjm.LocalDocs.Core/Services/DocumentIndexingException.cs`
- Modify: `src/Mjm.LocalDocs.Core/Services/DocumentService.cs`
- Test: `tests/Mjm.LocalDocs.Tests/Services/DocumentServiceIndexingTests.cs`

**Interfaces:**
- Consumes: nothing from earlier tasks.
- Produces: `DocumentIndexingException(string documentId, Exception innerException)` with a `DocumentId` property; private `IndexDocumentAsync(Document, CancellationToken)` and `DiscardPartialIndexAsync(string)` inside `DocumentService`. Task 7 consumes both privates.

- [ ] **Step 1: Write the failing tests**

Create `tests/Mjm.LocalDocs.Tests/Services/DocumentServiceIndexingTests.cs`:

```csharp
using Mjm.LocalDocs.Core.Abstractions;
using Mjm.LocalDocs.Core.Models;
using Mjm.LocalDocs.Core.Services;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace Mjm.LocalDocs.Tests.Services;

/// <summary>
/// Tests for indexing failure handling and repair in <see cref="DocumentService"/>.
/// </summary>
public sealed class DocumentServiceIndexingTests
{
    private readonly IDocumentRepository _repository = Substitute.For<IDocumentRepository>();
    private readonly IVectorStore _vectorStore = Substitute.For<IVectorStore>();
    private readonly IDocumentProcessor _processor = Substitute.For<IDocumentProcessor>();
    private readonly IEmbeddingService _embeddingService = Substitute.For<IEmbeddingService>();
    private readonly DocumentService _sut;

    public DocumentServiceIndexingTests()
    {
        _sut = new DocumentService(_repository, _vectorStore, _processor, _embeddingService);
    }

    private static Document CreateDocument(
        string id = "doc-1",
        string? parentDocumentId = null,
        bool isSuperseded = false)
    {
        return new Document
        {
            Id = id,
            ProjectId = "proj-1",
            FileName = $"{id}.txt",
            FileExtension = ".txt",
            FileContent = "Test content"u8.ToArray(),
            FileSizeBytes = 12,
            ExtractedText = "Test content for chunking",
            ParentDocumentId = parentDocumentId,
            IsSuperseded = isSuperseded
        };
    }

    private void GivenChunks(string documentId, int count)
    {
        var chunks = Enumerable.Range(0, count).Select(i => new DocumentChunk
        {
            Id = $"{documentId}_chunk_{i}",
            DocumentId = documentId,
            Content = $"chunk {i}",
            ChunkIndex = i,
            FileName = $"{documentId}.txt"
        }).ToList();

        _processor.ChunkDocumentAsync(Arg.Any<Document>(), Arg.Any<CancellationToken>())
            .Returns(chunks);
    }

    [Fact]
    public async Task AddDocumentAsync_WhenEmbeddingFails_ThrowsDocumentIndexingException()
    {
        var document = CreateDocument();
        _repository.AddDocumentAsync(Arg.Any<Document>(), Arg.Any<CancellationToken>())
            .Returns(document);
        GivenChunks("doc-1", 2);
        _embeddingService.GenerateEmbeddingsAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new HttpRequestException("provider unreachable"));

        var ex = await Assert.ThrowsAsync<DocumentIndexingException>(
            () => _sut.AddDocumentAsync(document));

        Assert.Equal("doc-1", ex.DocumentId);
        Assert.IsType<HttpRequestException>(ex.InnerException);
    }

    [Fact]
    public async Task AddDocumentAsync_WhenEmbeddingFails_KeepsDocumentButDiscardsChunks()
    {
        var document = CreateDocument();
        _repository.AddDocumentAsync(Arg.Any<Document>(), Arg.Any<CancellationToken>())
            .Returns(document);
        GivenChunks("doc-1", 2);
        _embeddingService.GenerateEmbeddingsAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new HttpRequestException("provider unreachable"));

        await Assert.ThrowsAsync<DocumentIndexingException>(() => _sut.AddDocumentAsync(document));

        // The uploaded file is not thrown away...
        await _repository.Received(1).AddDocumentAsync(Arg.Any<Document>(), Arg.Any<CancellationToken>());
        await _repository.DidNotReceive().DeleteDocumentAsync("doc-1", Arg.Any<CancellationToken>());
        // ...but the half-written index is.
        await _repository.Received(1).DeleteChunksByDocumentAsync("doc-1", Arg.Any<CancellationToken>());
        await _vectorStore.Received(1).DeleteByDocumentIdAsync("doc-1", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AddDocumentAsync_WhenCallerCancels_PropagatesCancellationUnwrapped()
    {
        var document = CreateDocument();
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        _repository.AddDocumentAsync(Arg.Any<Document>(), Arg.Any<CancellationToken>())
            .Returns(document);
        GivenChunks("doc-1", 2);
        _embeddingService.GenerateEmbeddingsAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new OperationCanceledException());

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => _sut.AddDocumentAsync(document, cts.Token));

        // The compensation must not inherit the cancelled token, or it could not run at all.
        // Matching on Arg.Any here would leave that guarantee unpinned.
        await _vectorStore.Received(1).DeleteByDocumentIdAsync(
            "doc-1", Arg.Is<CancellationToken>(t => !t.IsCancellationRequested));
        await _repository.Received(1).DeleteChunksByDocumentAsync(
            "doc-1", Arg.Is<CancellationToken>(t => !t.IsCancellationRequested));
    }

    [Fact]
    public async Task AddDocumentAsync_WhenProviderTimesOut_WrapsTheTaskCanceledException()
    {
        var document = CreateDocument();
        _repository.AddDocumentAsync(Arg.Any<Document>(), Arg.Any<CancellationToken>())
            .Returns(document);
        GivenChunks("doc-1", 2);
        // An HttpClient timeout surfaces as TaskCanceledException with the caller's token intact,
        // so it must be wrapped like any other provider failure, not mistaken for cancellation.
        // Without the `when` filter on the cancellation clause this is the most likely real
        // failure and it would escape raw, telling the user "cancelled" for a saved document.
        _embeddingService.GenerateEmbeddingsAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new TaskCanceledException("The request timed out."));

        var ex = await Assert.ThrowsAsync<DocumentIndexingException>(
            () => _sut.AddDocumentAsync(document));

        Assert.Equal("doc-1", ex.DocumentId);
        Assert.IsType<TaskCanceledException>(ex.InnerException);
    }

    [Fact]
    public async Task AddDocumentAsync_WhenCleanupAlsoFails_StillReportsTheOriginalCause()
    {
        var document = CreateDocument();
        _repository.AddDocumentAsync(Arg.Any<Document>(), Arg.Any<CancellationToken>())
            .Returns(document);
        GivenChunks("doc-1", 2);
        _embeddingService.GenerateEmbeddingsAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new HttpRequestException("provider unreachable"));
        _vectorStore.DeleteByDocumentIdAsync("doc-1", Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("cleanup exploded"));

        var ex = await Assert.ThrowsAsync<DocumentIndexingException>(
            () => _sut.AddDocumentAsync(document));

        Assert.IsType<HttpRequestException>(ex.InnerException);

        // The chunk delete must still run even though the vector delete threw.
        await _repository.Received(1).DeleteChunksByDocumentAsync("doc-1", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AddDocumentAsync_OnSuccess_StoresEmbeddingsAndDoesNotClean()
    {
        var document = CreateDocument();
        _repository.AddDocumentAsync(Arg.Any<Document>(), Arg.Any<CancellationToken>())
            .Returns(document);
        GivenChunks("doc-1", 2);
        _embeddingService.GenerateEmbeddingsAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>())
            .Returns([new float[] { 0.1f }, new float[] { 0.2f }]);

        await _sut.AddDocumentAsync(document);

        await _vectorStore.Received(1).UpsertBatchAsync(
            Arg.Any<IEnumerable<KeyValuePair<string, ReadOnlyMemory<float>>>>(),
            Arg.Any<CancellationToken>());
        await _repository.DidNotReceive().DeleteChunksByDocumentAsync("doc-1", Arg.Any<CancellationToken>());
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/Mjm.LocalDocs.Tests/Mjm.LocalDocs.Tests.csproj --filter "FullyQualifiedName~DocumentServiceIndexingTests"`
Expected: BUILD FAILURE — `The type or namespace name 'DocumentIndexingException' could not be found`.

- [ ] **Step 3: Create the exception**

Create `src/Mjm.LocalDocs.Core/Services/DocumentIndexingException.cs`:

```csharp
namespace Mjm.LocalDocs.Core.Services;

/// <summary>
/// Thrown when a document was persisted but could not be indexed for search.
/// </summary>
/// <remarks>
/// This is deliberately distinct from a failed upload: the file is stored and the document
/// exists, it is simply not searchable until reindexed. Callers should say so rather than
/// reporting the upload as failed.
/// </remarks>
public sealed class DocumentIndexingException : Exception
{
    /// <summary>
    /// The document that was saved but not indexed.
    /// </summary>
    public string DocumentId { get; }

    /// <summary>
    /// Creates a new <see cref="DocumentIndexingException"/>.
    /// </summary>
    /// <param name="documentId">The document that was saved but not indexed.</param>
    /// <param name="innerException">The failure that prevented indexing.</param>
    public DocumentIndexingException(string documentId, Exception innerException)
        : base(
            $"Document '{documentId}' was saved but could not be indexed. " +
            "It will not appear in search results until it is reindexed.",
            innerException)
    {
        DocumentId = documentId;
    }
}
```

- [ ] **Step 4: Extract `IndexDocumentAsync` and add compensation**

In `src/Mjm.LocalDocs.Core/Services/DocumentService.cs`, replace steps 3 to 6 of `AddDocumentAsync` — that is, everything from the `// 3. Split document into chunks` comment down to and including `return savedDocument;` — with:

```csharp
        // 3. Index the document (chunks + embeddings). On failure the document survives but
        //    the partial index does not, so it degrades to a clean "not indexed" state.
        try
        {
            await IndexDocumentAsync(document, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await DiscardPartialIndexAsync(document.Id);
            throw;
        }
        catch (Exception ex)
        {
            await DiscardPartialIndexAsync(document.Id);
            throw new DocumentIndexingException(document.Id, ex);
        }

        return savedDocument;
```

Also add the exception each method now propagates to its XML docs — this is the one exception
callers are specifically meant to catch. On `AddDocumentAsync`, beside the existing
`<exception cref="ArgumentException">`:

```csharp
    /// <exception cref="DocumentIndexingException">Thrown when the document was saved but could not be indexed.</exception>
```

And on `UpdateDocumentAsync`, beside its existing `<exception cref="InvalidOperationException">`:

```csharp
    /// <exception cref="DocumentIndexingException">Thrown when the new version was saved but could not be indexed. The previous version is left active.</exception>
```

Then add these two privates at the end of the class, before the closing brace:

```csharp
    /// <summary>
    /// Chunks a document, persists the chunks, and stores their embeddings.
    /// Shared by insertion and reindexing so indexing logic lives in one place.
    /// </summary>
    private async Task IndexDocumentAsync(Document document, CancellationToken cancellationToken)
    {
        var chunks = await _processor.ChunkDocumentAsync(document, cancellationToken);

        if (chunks.Count == 0)
            return;

        await _repository.AddChunksAsync(chunks, cancellationToken);

        var texts = chunks.Select(c => c.Content).ToList();
        var embeddings = await _embeddingService.GenerateEmbeddingsAsync(texts, cancellationToken);

        var embeddingsToStore = chunks
            .Select((chunk, index) => new KeyValuePair<string, ReadOnlyMemory<float>>(
                chunk.Id,
                embeddings[index]))
            .ToList();

        await _vectorStore.UpsertBatchAsync(embeddingsToStore, cancellationToken);
    }

    /// <summary>
    /// Removes any chunks and embeddings left behind by a failed indexing attempt.
    /// </summary>
    /// <remarks>
    /// Best effort by design: it swallows its own errors so it can never mask the real
    /// indexing failure, and it ignores the caller's token so a cancellation cannot leave
    /// the partial index in place.
    /// </remarks>
    private async Task DiscardPartialIndexAsync(string documentId)
    {
        // Guarded independently: in the dominant failure mode nothing was upserted, so the
        // vector delete is a no-op that must not be able to prevent the chunk delete that
        // actually matters. Every store deletes by chunk-id prefix, so the order creates
        // no dependency between the two.
        try
        {
            await _vectorStore.DeleteByDocumentIdAsync(documentId, CancellationToken.None);
        }
        catch
        {
            // Intentionally ignored: never mask the original indexing failure.
        }

        try
        {
            await _repository.DeleteChunksByDocumentAsync(documentId, CancellationToken.None);
        }
        catch
        {
            // Intentionally ignored: never mask the original indexing failure.
        }
    }
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test tests/Mjm.LocalDocs.Tests/Mjm.LocalDocs.Tests.csproj --filter "FullyQualifiedName~DocumentService"`
Expected: PASS. This runs both the new suite and the pre-existing `DocumentServiceTests`. If an existing test asserted that a raw provider exception escapes `AddDocumentAsync`, it now sees `DocumentIndexingException` — update that assertion, since the wrapping is the intended new behaviour.

- [ ] **Step 6: Commit**

```bash
git add src/Mjm.LocalDocs.Core/Services/DocumentIndexingException.cs src/Mjm.LocalDocs.Core/Services/DocumentService.cs tests/Mjm.LocalDocs.Tests/Services
git commit -m "Discard partial index when document indexing fails"
```

---

## Task 7: Reindex, and close interrupted updates

**Files:**
- Modify: `src/Mjm.LocalDocs.Core/Services/DocumentService.cs`
- Test: `tests/Mjm.LocalDocs.Tests/Services/DocumentServiceIndexingTests.cs`

**Interfaces:**
- Consumes: `IndexDocumentAsync`, `DiscardPartialIndexAsync`, `DocumentIndexingException` (Task 6).
- Produces: `DocumentService.ReindexDocumentAsync(string documentId, CancellationToken) → Task`. Task 10 consumes it.

- [ ] **Step 1: Write the failing tests**

Append to `DocumentServiceIndexingTests.cs`:

```csharp
    [Fact]
    public async Task ReindexDocumentAsync_RebuildsChunksAndEmbeddings()
    {
        var document = CreateDocument();
        _repository.GetDocumentAsync("doc-1", Arg.Any<CancellationToken>()).Returns(document);
        GivenChunks("doc-1", 2);
        _embeddingService.GenerateEmbeddingsAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>())
            .Returns([new float[] { 0.1f }, new float[] { 0.2f }]);

        await _sut.ReindexDocumentAsync("doc-1");

        await _repository.Received(1).AddChunksAsync(
            Arg.Any<IEnumerable<DocumentChunk>>(), Arg.Any<CancellationToken>());
        await _vectorStore.Received(1).UpsertBatchAsync(
            Arg.Any<IEnumerable<KeyValuePair<string, ReadOnlyMemory<float>>>>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ReindexDocumentAsync_WipesBeforeRebuildingSoRetriesAreIdempotent()
    {
        var document = CreateDocument();
        _repository.GetDocumentAsync("doc-1", Arg.Any<CancellationToken>()).Returns(document);
        GivenChunks("doc-1", 1);
        _embeddingService.GenerateEmbeddingsAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>())
            .Returns([new float[] { 0.1f }]);

        await _sut.ReindexDocumentAsync("doc-1");
        await _sut.ReindexDocumentAsync("doc-1");

        await _repository.Received(2).DeleteChunksByDocumentAsync("doc-1", Arg.Any<CancellationToken>());
        await _repository.Received(2).AddChunksAsync(
            Arg.Any<IEnumerable<DocumentChunk>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ReindexDocumentAsync_WhenDocumentMissing_Throws()
    {
        _repository.GetDocumentAsync("nope", Arg.Any<CancellationToken>())
            .Returns((Document?)null);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _sut.ReindexDocumentAsync("nope"));
    }

    [Fact]
    public async Task ReindexDocumentAsync_RefusesSupersededDocuments()
    {
        _repository.GetDocumentAsync("doc-1", Arg.Any<CancellationToken>())
            .Returns(CreateDocument(isSuperseded: true));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _sut.ReindexDocumentAsync("doc-1"));

        await _repository.DidNotReceive().AddChunksAsync(
            Arg.Any<IEnumerable<DocumentChunk>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ReindexDocumentAsync_WhenEmbeddingFailsAgain_ThrowsAndLeavesNoChunks()
    {
        _repository.GetDocumentAsync("doc-1", Arg.Any<CancellationToken>()).Returns(CreateDocument());
        GivenChunks("doc-1", 2);
        _embeddingService.GenerateEmbeddingsAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new HttpRequestException("still down"));

        await Assert.ThrowsAsync<DocumentIndexingException>(() => _sut.ReindexDocumentAsync("doc-1"));

        // Once for the pre-wipe, once for the compensating cleanup.
        await _repository.Received(2).DeleteChunksByDocumentAsync("doc-1", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ReindexDocumentAsync_WhenProviderTimesOut_WrapsTheTaskCanceledException()
    {
        _repository.GetDocumentAsync("doc-1", Arg.Any<CancellationToken>()).Returns(CreateDocument());
        GivenChunks("doc-1", 2);
        // Same trap as the insert path: a timeout is a provider failure, not a cancellation.
        _embeddingService.GenerateEmbeddingsAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new TaskCanceledException("The request timed out."));

        var ex = await Assert.ThrowsAsync<DocumentIndexingException>(
            () => _sut.ReindexDocumentAsync("doc-1"));

        Assert.Equal("doc-1", ex.DocumentId);
        Assert.IsType<TaskCanceledException>(ex.InnerException);
    }

    [Fact]
    public async Task ReindexDocumentAsync_WhenCallerCancels_PropagatesCancellationUnwrapped()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        _repository.GetDocumentAsync("doc-1", Arg.Any<CancellationToken>()).Returns(CreateDocument());
        GivenChunks("doc-1", 2);
        _embeddingService.GenerateEmbeddingsAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new OperationCanceledException());

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => _sut.ReindexDocumentAsync("doc-1", cts.Token));
    }

    [Fact]
    public async Task ReindexDocumentAsync_ClosesAnInterruptedUpdateBySupersedingTheParent()
    {
        var version2 = CreateDocument("doc-2", parentDocumentId: "doc-1");
        var parentStillActive = CreateDocument("doc-1");

        _repository.GetDocumentAsync("doc-2", Arg.Any<CancellationToken>()).Returns(version2);
        _repository.GetDocumentAsync("doc-1", Arg.Any<CancellationToken>()).Returns(parentStillActive);
        GivenChunks("doc-2", 1);
        _embeddingService.GenerateEmbeddingsAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>())
            .Returns([new float[] { 0.1f }]);

        await _sut.ReindexDocumentAsync("doc-2");

        await _repository.Received(1).SupersedeDocumentAsync("doc-1", Arg.Any<CancellationToken>());
        await _repository.Received(1).DeleteChunksByDocumentAsync("doc-1", Arg.Any<CancellationToken>());
        await _vectorStore.Received(1).DeleteByDocumentIdAsync("doc-1", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ReindexDocumentAsync_WhenParentAlreadySuperseded_LeavesItAlone()
    {
        var version2 = CreateDocument("doc-2", parentDocumentId: "doc-1");
        var parentAlreadyDone = CreateDocument("doc-1", isSuperseded: true);

        _repository.GetDocumentAsync("doc-2", Arg.Any<CancellationToken>()).Returns(version2);
        _repository.GetDocumentAsync("doc-1", Arg.Any<CancellationToken>()).Returns(parentAlreadyDone);
        GivenChunks("doc-2", 1);
        _embeddingService.GenerateEmbeddingsAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>())
            .Returns([new float[] { 0.1f }]);

        await _sut.ReindexDocumentAsync("doc-2");

        await _repository.DidNotReceive().SupersedeDocumentAsync("doc-1", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ReindexDocumentAsync_WhenIndexingFails_DoesNotSupersedeTheParent()
    {
        var version2 = CreateDocument("doc-2", parentDocumentId: "doc-1");

        _repository.GetDocumentAsync("doc-2", Arg.Any<CancellationToken>()).Returns(version2);
        _repository.GetDocumentAsync("doc-1", Arg.Any<CancellationToken>()).Returns(CreateDocument("doc-1"));
        GivenChunks("doc-2", 1);
        _embeddingService.GenerateEmbeddingsAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new HttpRequestException("still down"));

        await Assert.ThrowsAsync<DocumentIndexingException>(() => _sut.ReindexDocumentAsync("doc-2"));

        await _repository.DidNotReceive().SupersedeDocumentAsync("doc-1", Arg.Any<CancellationToken>());
    }
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/Mjm.LocalDocs.Tests/Mjm.LocalDocs.Tests.csproj --filter "FullyQualifiedName~DocumentServiceIndexingTests"`
Expected: BUILD FAILURE — `'DocumentService' does not contain a definition for 'ReindexDocumentAsync'`.

- [ ] **Step 3: Implement `ReindexDocumentAsync`**

In `DocumentService.cs`, add this public method after `UpdateDocumentAsync`:

```csharp
    /// <summary>
    /// Rebuilds the chunks and embeddings of a document from its already-extracted text.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Wipes the existing index first, so retrying after a partial failure is idempotent.
    /// The original file is never re-read: <see cref="Document.ExtractedText"/> is persisted,
    /// including on superseded versions.
    /// </para>
    /// <para>
    /// When the document is a version whose parent is still active — an update interrupted by
    /// an indexing failure — a successful reindex also supersedes that parent, closing the
    /// half-finished update.
    /// </para>
    /// </remarks>
    /// <param name="documentId">The document to reindex.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the document is not found, or is superseded and therefore not meant to be indexed.
    /// </exception>
    /// <exception cref="DocumentIndexingException">Thrown when indexing fails again.</exception>
    public async Task ReindexDocumentAsync(
        string documentId,
        CancellationToken cancellationToken = default)
    {
        var document = await _repository.GetDocumentAsync(documentId, cancellationToken);

        if (document is null)
            throw new InvalidOperationException($"Document '{documentId}' not found.");

        if (document.IsSuperseded)
        {
            throw new InvalidOperationException(
                $"Document '{documentId}' is superseded; superseded versions are not indexed by design.");
        }

        // Clean slate so a retry after a partial failure cannot duplicate chunks.
        await _vectorStore.DeleteByDocumentIdAsync(documentId, cancellationToken);
        await _repository.DeleteChunksByDocumentAsync(documentId, cancellationToken);

        try
        {
            await IndexDocumentAsync(document, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await DiscardPartialIndexAsync(documentId);
            throw;
        }
        catch (Exception ex)
        {
            // The filter above matters here for the same reason it does on the insert path:
            // an HttpClient timeout throws TaskCanceledException with the caller's token
            // uncancelled, and a timeout is a provider failure, not a cancellation.
            await DiscardPartialIndexAsync(documentId);
            throw new DocumentIndexingException(documentId, ex);
        }

        await CloseInterruptedUpdateAsync(document, cancellationToken);
    }

    /// <summary>
    /// Completes an update that failed partway: if this document is a version whose parent is
    /// still active, supersede the parent and remove it from search.
    /// </summary>
    private async Task CloseInterruptedUpdateAsync(
        Document document,
        CancellationToken cancellationToken)
    {
        if (document.ParentDocumentId is null)
            return;

        var parent = await _repository.GetDocumentAsync(document.ParentDocumentId, cancellationToken);

        if (parent is null || parent.IsSuperseded)
            return;

        await _repository.SupersedeDocumentAsync(parent.Id, cancellationToken);
        await _vectorStore.DeleteByDocumentIdAsync(parent.Id, cancellationToken);
        await _repository.DeleteChunksByDocumentAsync(parent.Id, cancellationToken);
    }
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/Mjm.LocalDocs.Tests/Mjm.LocalDocs.Tests.csproj --filter "FullyQualifiedName~DocumentService"`
Expected: PASS, 16 tests in `DocumentServiceIndexingTests` plus the pre-existing `DocumentServiceTests`.

- [ ] **Step 5: Run the whole suite**

Run: `dotnet test`
Expected: PASS. SQL Server vector store tests skip when no server is reachable — pre-existing behaviour.

- [ ] **Step 6: Commit**

```bash
git add src/Mjm.LocalDocs.Core/Services/DocumentService.cs tests/Mjm.LocalDocs.Tests/Services/DocumentServiceIndexingTests.cs
git commit -m "Add document reindexing that closes interrupted updates"
```

---

## Task 8: Register the service, add stat accents, build StatCard

**Files:**
- Modify: `src/Mjm.LocalDocs.Core/DependencyInjection/ServiceCollectionExtensions.cs`
- Modify: `src/Mjm.LocalDocs.Server/wwwroot/app.css`
- Create: `src/Mjm.LocalDocs.Server/Components/Dashboard/StatCard.razor`

**Interfaces:**
- Consumes: `DashboardMetricsService` (Task 4).
- Produces: `StatCard` component with parameters `Icon` (string, required), `Value` (string, required), `Label` (string, required), `Accent` (string, default `"blue"`), `Delta` (int?, default null). Task 11 consumes it.

- [ ] **Step 1: Register the service**

In `src/Mjm.LocalDocs.Core/DependencyInjection/ServiceCollectionExtensions.cs`, add after the `ApiTokenService` registration and before `return services;`:

```csharp
        // Dashboard read-side metrics (growth series + index health)
        services.AddScoped<DashboardMetricsService>();
```

The default constructor injection resolves `IDocumentRepository` and `IVectorStore`; the optional `TimeProvider` parameter falls back to `TimeProvider.System` when not registered, so no extra registration is needed.

- [ ] **Step 2: Add the two missing stat accents**

`app.css` currently defines only `green`, `blue` and `amber`. The dashboard needs five. In `src/Mjm.LocalDocs.Server/wwwroot/app.css`, find the light-theme block containing `--ld-stat-amber-icon: #D97706;` and add immediately after it:

```css
    --ld-stat-violet-bg: linear-gradient(135deg, #F5F3FF 0%, #EDE9FE 100%);
    --ld-stat-violet-border: #DDD6FE;
    --ld-stat-violet-icon-bg: #EDE9FE;
    --ld-stat-violet-icon: #7C3AED;

    --ld-stat-rose-bg: linear-gradient(135deg, #FFF1F2 0%, #FFE4E6 100%);
    --ld-stat-rose-border: #FECDD3;
    --ld-stat-rose-icon-bg: #FFE4E6;
    --ld-stat-rose-icon: #E11D48;
```

Then find the dark-theme block containing the `--ld-stat-amber-*` overrides and add immediately after them:

```css
    --ld-stat-violet-bg: linear-gradient(135deg, #2E1065 0%, #4C1D95 100%);
    --ld-stat-violet-border: #6D28D9;
    --ld-stat-violet-icon-bg: #4C1D95;
    --ld-stat-violet-icon: #A78BFA;

    --ld-stat-rose-bg: linear-gradient(135deg, #4C0519 0%, #881337 100%);
    --ld-stat-rose-border: #BE123C;
    --ld-stat-rose-icon-bg: #881337;
    --ld-stat-rose-icon: #FB7185;
```

Finally, locate the existing `.ld-stat-amber` and `.ld-stat-icon-amber` rules and add the two parallel pairs next to them, matching whatever properties those rules already set — typically:

```css
.ld-stat-violet {
    background: var(--ld-stat-violet-bg);
    border: 1px solid var(--ld-stat-violet-border);
}

.ld-stat-icon-violet {
    background: var(--ld-stat-violet-icon-bg);
    color: var(--ld-stat-violet-icon);
}

.ld-stat-rose {
    background: var(--ld-stat-rose-bg);
    border: 1px solid var(--ld-stat-rose-border);
}

.ld-stat-icon-rose {
    background: var(--ld-stat-rose-icon-bg);
    color: var(--ld-stat-rose-icon);
}
```

If the existing amber rules set different properties, mirror those instead — consistency with the file wins over the snippet above.

- [ ] **Step 3: Create the StatCard component**

Create `src/Mjm.LocalDocs.Server/Components/Dashboard/StatCard.razor`:

```razor
@* One KPI tile. Extracted from Home.razor, where this markup was triplicated inline. *@

<MudPaper Class="@($"pa-5 ld-stat-{Accent}")" Elevation="0" Style="border-radius: var(--ld-radius-lg); height: 100%;">
    <div class="d-flex align-center" style="gap: 16px;">
        <div class="@($"ld-stat-icon ld-stat-icon-{Accent}")">
            <MudIcon Icon="@Icon" Style="font-size: 1.25rem;" />
        </div>
        <div>
            <div class="d-flex align-center" style="gap: 6px;">
                <MudText Style="font-size: 1.75rem; font-weight: 700; letter-spacing: -0.025em; color: var(--ld-text-primary); line-height: 1;">
                    @Value
                </MudText>
                @if (Delta.HasValue)
                {
                    <MudIcon Icon="@DeltaIcon" Style="@($"font-size: 1rem; color: {DeltaColor};")" />
                }
            </div>
            <MudText Style="font-size: 0.8125rem; color: var(--ld-text-secondary); margin-top: 2px;">@Label</MudText>
        </div>
    </div>
</MudPaper>

@code {
    /// <summary>MudBlazor icon markup for the tile.</summary>
    [Parameter, EditorRequired] public string Icon { get; set; } = default!;

    /// <summary>Pre-formatted value. Formatting belongs to the caller, not the tile.</summary>
    [Parameter, EditorRequired] public string Value { get; set; } = default!;

    /// <summary>Short caption under the value.</summary>
    [Parameter, EditorRequired] public string Label { get; set; } = default!;

    /// <summary>Accent name: green, blue, amber, violet or rose.</summary>
    [Parameter] public string Accent { get; set; } = "blue";

    /// <summary>Optional trend. Positive shows a rising arrow, negative a falling one, zero a flat one.</summary>
    [Parameter] public int? Delta { get; set; }

    private string DeltaIcon => Delta switch
    {
        > 0 => Icons.Material.Rounded.TrendingUp,
        < 0 => Icons.Material.Rounded.TrendingDown,
        _ => Icons.Material.Rounded.TrendingFlat
    };

    private string DeltaColor => Delta switch
    {
        > 0 => "var(--ld-stat-green-icon)",
        < 0 => "var(--ld-stat-rose-icon)",
        _ => "var(--ld-text-muted)"
    };
}
```

- [ ] **Step 4: Verify it builds**

Run: `dotnet build`
Expected: Build succeeded, 0 errors. `StatCard` is not referenced yet, so this only proves it compiles.

- [ ] **Step 5: Commit**

```bash
git add src/Mjm.LocalDocs.Core/DependencyInjection/ServiceCollectionExtensions.cs src/Mjm.LocalDocs.Server/wwwroot/app.css src/Mjm.LocalDocs.Server/Components/Dashboard/StatCard.razor
git commit -m "Register dashboard metrics service and add StatCard component"
```

---

## Task 9: KnowHowGrowthChart component

**Files:**
- Create: `src/Mjm.LocalDocs.Server/Components/Dashboard/KnowHowGrowthChart.razor`

**Interfaces:**
- Consumes: `GrowthBucket`, `GrowthGranularity` (Task 2).
- Produces: `KnowHowGrowthChart` with parameters `Buckets` (`IReadOnlyList<GrowthBucket>`, required), `Granularity` (`GrowthGranularity`), `GranularityChanged` (`EventCallback<GrowthGranularity>`). Task 11 consumes it.

- [ ] **Step 1: Create the component**

Create `src/Mjm.LocalDocs.Server/Components/Dashboard/KnowHowGrowthChart.razor`:

```razor
@using Mjm.LocalDocs.Core.Models.Dashboard

@* Stacked bars: solid = brand-new documents, faded = new versions of existing ones.
   The split is the point — steady bars with a shrinking solid portion mean the
   knowledge base is being rewritten, not expanded. *@

<MudPaper Class="pa-5" Elevation="0" Style="border-radius: var(--ld-radius-lg); height: 100%;">
    <div class="d-flex align-center justify-space-between mb-4" style="gap: 12px;">
        <div>
            <p class="ld-section-title" style="margin-bottom: 2px;">KNOW-HOW GROWTH</p>
            <MudText Style="font-size: 0.75rem; color: var(--ld-text-muted);">
                New documents vs new versions
            </MudText>
        </div>
        <MudToggleGroup T="GrowthGranularity"
                        Value="@Granularity"
                        ValueChanged="@OnGranularityChanged"
                        Size="Size.Small"
                        CheckMark="false"
                        Dense="true">
            <MudToggleItem Value="@GrowthGranularity.Monthly" Text="Months" />
            <MudToggleItem Value="@GrowthGranularity.Weekly" Text="Weeks" />
        </MudToggleGroup>
    </div>

    @if (HasAnyContribution)
    {
        <MudChart ChartType="ChartType.StackedBar"
                  ChartSeries="@Series"
                  XAxisLabels="@Labels"
                  ChartOptions="@_chartOptions"
                  Width="100%"
                  Height="300px" />

        @if (Buckets.Count > 0 && Buckets[^1].IsPartial)
        {
            <MudText Style="font-size: 0.6875rem; color: var(--ld-text-muted); margin-top: 4px;">
                The last bar covers a period still in progress.
            </MudText>
        }
    }
    else
    {
        <div class="ld-empty-state">
            <MudIcon Icon="@Icons.Material.Rounded.ShowChart"
                     Style="font-size: 2.5rem; color: var(--ld-text-disabled); margin-bottom: 8px;" />
            <MudText Style="font-size: 0.875rem; font-weight: 600; color: var(--ld-text-primary);">
                No contributions in this window
            </MudText>
            <MudText Style="font-size: 0.8125rem; color: var(--ld-text-secondary);">
                Add documents to a project and the growth will show up here.
            </MudText>
        </div>
    }
</MudPaper>

@code {
    /// <summary>Twelve buckets, oldest first.</summary>
    [Parameter, EditorRequired] public IReadOnlyList<GrowthBucket> Buckets { get; set; } = [];

    /// <summary>Currently selected bucket size.</summary>
    [Parameter] public GrowthGranularity Granularity { get; set; } = GrowthGranularity.Monthly;

    /// <summary>Raised when the user switches bucket size.</summary>
    [Parameter] public EventCallback<GrowthGranularity> GranularityChanged { get; set; }

    // MudChart takes its colours from C#, not from the --ld-* CSS variables, so these two
    // are fixed values chosen to read correctly against both the light and dark surfaces.
    private readonly ChartOptions _chartOptions = new()
    {
        ChartPalette = ["#2563EB", "#93C5FD"],
        InterpolationOption = InterpolationOption.Straight
    };

    private bool HasAnyContribution =>
        Buckets.Any(b => b.NewDocuments > 0 || b.NewVersions > 0);

    private List<ChartSeries> Series =>
    [
        new() { Name = "New documents", Data = Buckets.Select(b => (double)b.NewDocuments).ToArray() },
        new() { Name = "New versions", Data = Buckets.Select(b => (double)b.NewVersions).ToArray() }
    ];

    private string[] Labels => Buckets.Select(b => b.Label).ToArray();

    private Task OnGranularityChanged(GrowthGranularity granularity)
    {
        Granularity = granularity;
        return GranularityChanged.InvokeAsync(granularity);
    }
}
```

- [ ] **Step 2: Verify it builds**

Run: `dotnet build`
Expected: Build succeeded, 0 errors.

If the build reports an unknown member on `ChartOptions`, `ChartSeries`, `MudToggleGroup` or `MudToggleItem`, the MudBlazor 8.15.0 API differs from what is written above. Do **not** add a package or change versions. Open the MudBlazor package's IntelliSense metadata for the offending type and adjust the property names in place, keeping the two-colour palette and the Months/Weeks toggle behaviour identical.

- [ ] **Step 3: Commit**

```bash
git add src/Mjm.LocalDocs.Server/Components/Dashboard/KnowHowGrowthChart.razor
git commit -m "Add know-how growth chart component"
```

---

## Task 10: IndexHealthPanel component

**Files:**
- Create: `src/Mjm.LocalDocs.Server/Components/Dashboard/IndexHealthPanel.razor`

**Interfaces:**
- Consumes: `IndexHealth`, `DocumentChunkTally` (Task 2); `DashboardMetricsService.GetIndexHealthAsync` (Task 4); `DocumentService.ReindexDocumentAsync` (Task 7).
- Produces: `IndexHealthPanel` with parameters `Health` (`IndexHealth`, required) and `HealthChanged` (`EventCallback<IndexHealth>`). Task 11 consumes it.

- [ ] **Step 1: Create the component**

Create `src/Mjm.LocalDocs.Server/Components/Dashboard/IndexHealthPanel.razor`:

```razor
@using Mjm.LocalDocs.Core.Models.Dashboard
@using Mjm.LocalDocs.Core.Services
@inject DashboardMetricsService Metrics
@inject DocumentService Documents
@inject ISnackbar Snackbar

@* A document can be uploaded and still be unsearchable, if embedding generation failed
   after its chunks were written. This panel is how that becomes visible and repairable. *@

<MudPaper Class="pa-5" Elevation="0" Style="border-radius: var(--ld-radius-lg); height: 100%;">
    <div class="d-flex align-center justify-space-between mb-3" style="gap: 8px;">
        <p class="ld-section-title" style="margin-bottom: 0;">INDEX HEALTH</p>
        <MudTooltip Text="Re-check every chunk against the vector store">
            <MudIconButton Icon="@Icons.Material.Rounded.Refresh"
                           Size="Size.Small"
                           Disabled="@_busy"
                           OnClick="@ReviewAsync" />
        </MudTooltip>
    </div>

    @if (Health.ActiveDocuments == 0)
    {
        <MudText Style="font-size: 0.8125rem; color: var(--ld-text-secondary);">
            No active documents yet.
        </MudText>
    }
    else
    {
        <div class="d-flex flex-column align-center mb-3">
            <MudChart ChartType="ChartType.Donut"
                      InputData="@DonutData"
                      InputLabels="@(["Indexed", "Not indexed"])"
                      ChartOptions="@_chartOptions"
                      Width="140px"
                      Height="140px" />
            <MudText Style="font-size: 1.25rem; font-weight: 700; color: var(--ld-text-primary); line-height: 1;">
                @IndexedPercent%
            </MudText>
            <MudText Style="font-size: 0.75rem; color: var(--ld-text-secondary);">
                @Health.FullyIndexed of @Health.ActiveDocuments searchable
            </MudText>
        </div>

        @if (Health.Broken.Count == 0)
        {
            <div class="d-flex align-center" style="gap: 8px;">
                <MudIcon Icon="@Icons.Material.Rounded.CheckCircle"
                         Style="font-size: 1rem; color: var(--ld-stat-green-icon);" />
                <MudText Style="font-size: 0.8125rem; color: var(--ld-text-secondary);">
                    Everything is indexed.
                </MudText>
            </div>
        }
        else
        {
            <MudText Style="font-size: 0.75rem; font-weight: 600; color: var(--ld-text-primary); margin-bottom: 6px;">
                Uploaded but not searchable
            </MudText>

            @foreach (var document in Health.Broken)
            {
                <div class="d-flex align-center justify-space-between" style="gap: 8px; padding: 6px 0; border-top: 1px solid var(--ld-border);">
                    <MudTooltip Text="@document.FileName">
                        <MudText Style="font-size: 0.75rem; color: var(--ld-text-secondary); overflow: hidden; text-overflow: ellipsis; white-space: nowrap; max-width: 150px;">
                            @document.FileName
                        </MudText>
                    </MudTooltip>
                    <MudButton Variant="Variant.Text"
                               Size="Size.Small"
                               Disabled="@_busy"
                               OnClick="@(() => ReindexAsync(document))">
                        Reindex
                    </MudButton>
                </div>
            }
        }
    }
</MudPaper>

@code {
    /// <summary>Current index health of the active document set.</summary>
    [Parameter, EditorRequired] public IndexHealth Health { get; set; } = default!;

    /// <summary>Raised with fresh health after a review or a successful reindex.</summary>
    [Parameter] public EventCallback<IndexHealth> HealthChanged { get; set; }

    private bool _busy;

    private readonly ChartOptions _chartOptions = new()
    {
        ChartPalette = ["#059669", "#E11D48"]
    };

    private double[] DonutData =>
    [
        Health.FullyIndexed,
        Health.ActiveDocuments - Health.FullyIndexed
    ];

    private int IndexedPercent => Health.ActiveDocuments == 0
        ? 100
        : (int)Math.Round(100.0 * Health.FullyIndexed / Health.ActiveDocuments);

    private async Task ReviewAsync()
    {
        _busy = true;
        try
        {
            // Forced reconciliation: the cheap count comparison can miss drift when the
            // totals happen to agree over different sets.
            var health = await Metrics.GetIndexHealthAsync(forceFullReconciliation: true);
            await HealthChanged.InvokeAsync(health);
            Snackbar.Add(
                health.Broken.Count == 0
                    ? "All documents are indexed."
                    : $"{health.Broken.Count} document(s) need reindexing.",
                health.Broken.Count == 0 ? Severity.Success : Severity.Warning);
        }
        catch (Exception ex)
        {
            Snackbar.Add($"Could not check the index: {ex.Message}", Severity.Error);
        }
        finally
        {
            _busy = false;
        }
    }

    private async Task ReindexAsync(DocumentChunkTally document)
    {
        _busy = true;
        try
        {
            await Documents.ReindexDocumentAsync(document.DocumentId);
            Snackbar.Add($"{document.FileName} is searchable again.", Severity.Success);

            var health = await Metrics.GetIndexHealthAsync(forceFullReconciliation: true);
            await HealthChanged.InvokeAsync(health);
        }
        catch (DocumentIndexingException ex)
        {
            Snackbar.Add(
                $"Reindexing {document.FileName} failed: {ex.InnerException?.Message ?? ex.Message}",
                Severity.Error);
        }
        catch (Exception ex)
        {
            Snackbar.Add($"Reindexing {document.FileName} failed: {ex.Message}", Severity.Error);
        }
        finally
        {
            _busy = false;
        }
    }
}
```

- [ ] **Step 2: Verify it builds**

Run: `dotnet build`
Expected: Build succeeded, 0 errors. If `--ld-border` is not a token defined in `app.css`, substitute the token that file actually uses for hairline separators.

- [ ] **Step 3: Commit**

```bash
git add src/Mjm.LocalDocs.Server/Components/Dashboard/IndexHealthPanel.razor
git commit -m "Add index health panel component"
```

---

## Task 11: Rework Home.razor and remove the per-project N+1

**Files:**
- Modify: `src/Mjm.LocalDocs.Server/Components/Pages/Home.razor`

**Interfaces:**
- Consumes: `StatCard` (Task 8), `KnowHowGrowthChart` (Task 9), `IndexHealthPanel` (Task 10), `DashboardMetricsService.GetMetricsAsync` (Task 5), `IDocumentRepository.GetActiveDocumentCountsByProjectAsync` (Task 2).
- Produces: the finished dashboard. Nothing consumes it.

- [ ] **Step 1: Replace the whole file**

Overwrite `src/Mjm.LocalDocs.Server/Components/Pages/Home.razor`:

```razor
@page "/"
@attribute [Authorize]
@rendermode InteractiveServer
@using Mjm.LocalDocs.Core.Abstractions
@using Mjm.LocalDocs.Core.Models
@using Mjm.LocalDocs.Core.Models.Dashboard
@using Mjm.LocalDocs.Core.Services
@using Mjm.LocalDocs.Server.Components.Dashboard
@inject IProjectRepository ProjectRepository
@inject IDocumentRepository DocumentRepository
@inject DashboardMetricsService Metrics
@inject NavigationManager Navigation

<PageTitle>Dashboard - Local Docs</PageTitle>

@if (_loading)
{
    <MudProgressCircular Color="Color.Primary" Indeterminate="true" Class="mx-auto d-block my-8" />
}
else if (_metrics is null)
{
    <MudAlert Severity="Severity.Error" Class="my-4">
        Could not load the dashboard. @_loadError
    </MudAlert>
}
else
{
    <div class="d-flex align-center justify-space-between flex-wrap mb-6" style="gap: 12px;">
        <div>
            <MudText Typo="Typo.h4" Class="ld-heading">Dashboard</MudText>
            <MudText Typo="Typo.body2" Style="color: var(--ld-text-secondary); margin-top: 4px;">
                Is the knowledge base growing?
            </MudText>
        </div>
        <div class="d-flex flex-wrap" style="gap: 12px;">
            <MudButton Variant="Variant.Filled"
                       Color="Color.Primary"
                       StartIcon="@Icons.Material.Rounded.Add"
                       Style="border-radius: var(--ld-radius-pill); padding: 8px 24px;"
                       OnClick="@(() => Navigation.NavigateTo("/projects/new"))">
                New Project
            </MudButton>
            <MudButton Variant="Variant.Outlined"
                       Color="Color.Primary"
                       StartIcon="@Icons.Material.Rounded.ViewList"
                       Style="border-radius: var(--ld-radius-pill); padding: 8px 24px;"
                       OnClick="@(() => Navigation.NavigateTo("/projects"))">
                All Projects
            </MudButton>
        </div>
    </div>

    @* Five KPI tiles. "Last contribution" is the bluntest number here: a large
       knowledge base whose last write was months ago is an archive, not a tool. *@
    <MudGrid Spacing="3" Class="mb-6">
        <MudItem xs="12" sm="6" md="4" lg="2">
            <StatCard Icon="@Icons.Material.Rounded.Description"
                      Value="@_metrics.ActiveDocumentCount.ToString()"
                      Label="Active documents"
                      Accent="blue" />
        </MudItem>
        <MudItem xs="12" sm="6" md="4" lg="2">
            <StatCard Icon="@Icons.Material.Rounded.NoteAdd"
                      Value="@_metrics.NewInPeriod.ToString()"
                      Label="@($"New ({PeriodLabel})")"
                      Accent="green" />
        </MudItem>
        <MudItem xs="12" sm="6" md="4" lg="3">
            <StatCard Icon="@Icons.Material.Rounded.CompareArrows"
                      Value="@FormatDelta(Delta)"
                      Label="@($"vs previous {PeriodLabel}")"
                      Accent="violet"
                      Delta="Delta" />
        </MudItem>
        <MudItem xs="12" sm="6" md="6" lg="3">
            <StatCard Icon="@Icons.Material.Rounded.Schedule"
                      Value="@LastContributionText"
                      Label="Last contribution"
                      Accent="amber" />
        </MudItem>
        <MudItem xs="12" sm="6" md="6" lg="2">
            <StatCard Icon="@Icons.Material.Rounded.ErrorOutline"
                      Value="@_health.Broken.Count.ToString()"
                      Label="Not indexed"
                      Accent="@(_health.Broken.Count == 0 ? "green" : "rose")" />
        </MudItem>
    </MudGrid>

    <MudGrid Spacing="3" Class="mb-6">
        <MudItem xs="12" md="8">
            <KnowHowGrowthChart Buckets="@_metrics.Growth"
                                Granularity="@_granularity"
                                GranularityChanged="@OnGranularityChangedAsync" />
        </MudItem>
        <MudItem xs="12" md="4">
            <IndexHealthPanel Health="@_health" HealthChanged="@OnHealthChanged" />
        </MudItem>
    </MudGrid>

    @if (_recentProjects.Count > 0)
    {
        <div class="mb-2">
            <p class="ld-section-title">RECENT PROJECTS</p>
        </div>

        <MudGrid Spacing="3">
            @foreach (var project in _recentProjects)
            {
                <MudItem xs="12" sm="6" md="4">
                    <MudPaper Class="pa-5 ld-card-hover cursor-pointer"
                              Elevation="0"
                              Style="border-radius: var(--ld-radius-lg);"
                              @onclick="@(() => Navigation.NavigateTo($"/projects/{project.Id}"))">
                        <div class="d-flex align-center mb-3" style="gap: 10px;">
                            <div style="width: 36px; height: 36px; border-radius: 10px; background: var(--ld-folder-bg); display: flex; align-items: center; justify-content: center;">
                                <MudIcon Icon="@Icons.Material.Rounded.Folder" Style="color: var(--ld-folder-icon); font-size: 1.125rem;" />
                            </div>
                            <MudText Style="font-size: 1rem; font-weight: 600; letter-spacing: -0.01em; color: var(--ld-text-primary);">
                                @project.Name
                            </MudText>
                        </div>
                        @if (!string.IsNullOrEmpty(project.Description))
                        {
                            <MudText Style="font-size: 0.8125rem; color: var(--ld-text-secondary); overflow: hidden; text-overflow: ellipsis; white-space: nowrap; margin-bottom: 12px;">
                                @project.Description
                            </MudText>
                        }
                        <div class="d-flex align-center" style="gap: 6px;">
                            <MudIcon Icon="@Icons.Material.Rounded.Description" Style="font-size: 0.875rem; color: var(--ld-text-muted);" />
                            <MudText Style="font-size: 0.75rem; color: var(--ld-text-muted);">
                                @GetDocumentCountForProject(project.Id) documents
                            </MudText>
                        </div>
                    </MudPaper>
                </MudItem>
            }
        </MudGrid>
    }
    else
    {
        <div class="ld-empty-state">
            <MudIcon Icon="@Icons.Material.Rounded.CreateNewFolder"
                     Style="font-size: 3rem; color: var(--ld-text-disabled); margin-bottom: 12px;" />
            <MudText Style="font-size: 0.9375rem; font-weight: 600; color: var(--ld-text-primary); margin-bottom: 4px;">
                No projects yet
            </MudText>
            <MudText Style="font-size: 0.8125rem; color: var(--ld-text-secondary); margin-bottom: 20px;">
                Create your first project to start organizing documents.
            </MudText>
            <MudButton Variant="Variant.Filled" Color="Color.Primary"
                       StartIcon="@Icons.Material.Rounded.Add"
                       Style="border-radius: var(--ld-radius-pill);"
                       OnClick="@(() => Navigation.NavigateTo("/projects/new"))">
                New Project
            </MudButton>
        </div>
    }
}

@code {
    private bool _loading = true;
    private string? _loadError;
    private GrowthGranularity _granularity = GrowthGranularity.Monthly;
    private DashboardMetrics? _metrics;
    private IndexHealth _health = new(0, 0, []);
    private List<Project> _recentProjects = [];
    private IReadOnlyDictionary<string, int> _projectDocumentCounts = new Dictionary<string, int>();

    protected override async Task OnInitializedAsync()
    {
        await LoadDataAsync();
    }

    private async Task LoadDataAsync()
    {
        _loading = true;
        _loadError = null;

        try
        {
            _metrics = await Metrics.GetMetricsAsync(_granularity);
            _health = _metrics.Health;

            var projects = await ProjectRepository.GetAllAsync();
            _recentProjects = projects
                .OrderByDescending(p => p.UpdatedAt ?? p.CreatedAt)
                .Take(6)
                .ToList();

            // One GROUP BY. The previous implementation looped GetDocumentsByProjectAsync
            // per project, which materialises whole entities including FileContent — so with
            // database file storage it pulled every stored blob into memory to count rows.
            _projectDocumentCounts = await DocumentRepository.GetActiveDocumentCountsByProjectAsync();
        }
        catch (Exception ex)
        {
            _metrics = null;
            _loadError = ex.Message;
        }
        finally
        {
            _loading = false;
        }
    }

    private async Task OnGranularityChangedAsync(GrowthGranularity granularity)
    {
        _granularity = granularity;
        await LoadDataAsync();
    }

    private void OnHealthChanged(IndexHealth health)
    {
        _health = health;
    }

    private int Delta => _metrics is null
        ? 0
        : _metrics.NewInPeriod - _metrics.NewInPreviousPeriod;

    private string PeriodLabel => _granularity == GrowthGranularity.Weekly ? "7d" : "30d";

    private string LastContributionText
    {
        get
        {
            if (_metrics?.LastContributionAt is null)
                return "—";

            var days = (int)(DateTimeOffset.UtcNow - _metrics.LastContributionAt.Value).TotalDays;
            return days switch
            {
                <= 0 => "today",
                1 => "1 day",
                _ => $"{days} days"
            };
        }
    }

    private static string FormatDelta(int delta) => delta > 0 ? $"+{delta}" : delta.ToString();

    private int GetDocumentCountForProject(string projectId) =>
        _projectDocumentCounts.TryGetValue(projectId, out var count) ? count : 0;
}
```

- [ ] **Step 2: Verify it builds**

Run: `dotnet build`
Expected: Build succeeded, 0 errors.

- [ ] **Step 3: Run the whole test suite**

Run: `dotnet test`
Expected: PASS. No test targets the UI; this confirms nothing in Core or Infrastructure regressed.

- [ ] **Step 4: Manual verification**

Run: `dotnet run --project src/Mjm.LocalDocs.Server/Mjm.LocalDocs.Server.csproj`

Open `http://localhost:5024`, log in with `admin` / `admin`, and confirm:

1. Five KPI tiles render, each with its own accent colour, and the delta tile shows an arrow.
2. The chart shows twelve bars with month labels; switching to **Weeks** relabels them and reloads without an error.
3. On an empty or near-empty knowledge base the chart shows the "No contributions in this window" empty state rather than a row of zeros.
4. The health panel shows a donut and "Everything is indexed." when nothing is broken.
5. Toggle the theme with the existing theme switch and confirm both new accents (violet, rose) and both chart palettes stay legible in dark mode.

To exercise the broken-document path deliberately: set `LocalDocs:Embeddings:Provider` to `Ollama` in `appsettings.Development.json` with nothing listening on the configured endpoint, upload a document, and confirm the upload surfaces an indexing failure rather than a generic one, that the document appears in the "Uploaded but not searchable" list, and that **Reindex** clears it once the provider is reachable again.

- [ ] **Step 5: Commit**

```bash
git add src/Mjm.LocalDocs.Server/Components/Pages/Home.razor
git commit -m "Rework dashboard around know-how growth and index health"
```

---

## Self-Review

**Spec coverage.** Every spec section maps to a task: metric semantics → Tasks 4 and 5; read models and abstraction additions → Tasks 1, 2, 3; the fast path → Task 4; the pipeline fix, prevention and repair → Tasks 6 and 7; interrupted updates → Task 7; the three components and the `Home.razor` composition → Tasks 8 through 11; the N+1 fix → Task 2 (`GetActiveDocumentCountsByProjectAsync`) consumed in Task 11. The `MudChart` palette and empty-state limitations the spec declared are honoured in Tasks 9 and 10.

**Two spec statements this plan deliberately narrows:**

1. The spec's `GetChunkIdsByDocumentsAsync` became `GetChunkOwnershipAsync` returning `(DocumentId, ChunkId)` pairs. Recorded in Global Constraints with the reason.
2. The spec said the typed exception lets "the upload UI and the MCP `add_document` tool" report *saved but not indexed*. This plan adds the exception and throws it, but **does not** update the two call sites' messages — they will surface the exception's own message, which already says the document was saved and needs reindexing. Rewording those two call sites is a genuinely separate, one-line-each change; it is called out here so it is not mistaken for completed work.

**Placeholder scan.** No TBD or TODO. Every code step carries the actual code. The two "if the API differs, adjust in place" notes in Tasks 9 and 10 are verification instructions with a defined fallback, not deferred decisions.

**Type consistency.** `DocumentChunkTally` carries `DocumentId, ProjectId, FileName, ChunkCount` in the model (Task 2), the repository implementations (Task 3), the service (Task 4) and the panel (Task 10). `IndexHealth(ActiveDocuments, FullyIndexed, Broken)` is constructed only in Task 4 and read in Tasks 5, 10 and 11. `GetIndexHealthAsync(bool, CancellationToken)` is declared once, in Task 4, and never restated. `GrowthGranularity` is `Weekly` / `Monthly` throughout.

**Two corrections made before execution**, both from the pre-flight scan for plan text that mandates what a reviewer would flag as a defect:

1. **Task ordering.** An earlier draft put growth bucketing before index health, which forced `GetMetricsAsync` to call a `GetIndexHealthAsync` that did not exist yet — so that draft had Task 4 declare a stub with an ignored `forceFullReconciliation` parameter and a fabricated all-clear return, replaced in Task 5. The two halves have no mutual dependency, so they are now ordered health-first: the service never contains a stub or an unused member at any point, and each task keeps its own review gate. The `TimeProvider` parameter likewise arrives in Task 5, where it is first used, rather than sitting unread in Task 4.

2. **Test duplication.** An earlier draft had Tasks 2 and 3 copy every aggregate test body into two files, on the stated grounds that "a shared base would hide a divergence behind one failing run." That reasoning was wrong: xUnit discovers inherited `[Fact]` methods on each concrete subclass, so an abstract base with two subclasses runs every test once per implementation and a divergence fails only the affected subclass. The tests are now written once in `DocumentRepositoryAggregateTests` with two thin fixtures. Task 1 still writes its assertions per vector store, because the four existing vector store test files in this repository already do, and matching the house pattern wins there.
