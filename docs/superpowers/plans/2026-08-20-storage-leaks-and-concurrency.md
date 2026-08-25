# Storage Leaks and Concurrency — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Fix three pre-existing defects that the dashboard work revealed rather than introduced: deleting a project orphans both its embeddings and its externally-stored files, nothing serialises the wipe-and-rebuild paths server-side, and `SearchAsync` carries the same blob-loading and N+1 problems the dashboard just had removed.

**Architecture:** A new `ProjectService` in Core owns project deletion so the ordering lives in one place instead of being remembered at three call sites; a singleton per-document lock registry makes the existing UI guards defence-in-depth rather than the only defence; and two narrow repository reads let `SearchAsync` replace one blob-heavy query plus an N+1 loop with a single scalar join.

**Tech Stack:** .NET 10, C#, Blazor Server (InteractiveServer), EF Core 10 (SQLite + SQL Server), xUnit v3 + NSubstitute.

**Predecessors:** `docs/superpowers/plans/2026-08-20-dashboard-know-how-growth.md` (11 tasks, complete) and `docs/superpowers/plans/2026-08-20-dashboard-followup-integrity.md` (6 tasks, complete). None of the three defects here was introduced by either — they were found by a whole-branch review and by running the dashboard against a real database.

## Global Constraints

- **Solution root:** `C:\Projects\mjm.local.docs\mjm.local.docs` (nested). **All `dotnet` commands and every `src/...` / `tests/...` path below is relative to that folder.** This plan lives in the outer repo under `docs/superpowers/plans/`.
- **Branch:** decide before Task 1 — see *Branch* below.
- **Target framework:** `net10.0`; `Nullable` and `ImplicitUsings` enabled.
- **No EF migration, no schema change.** All three fixes are code-only. In particular, do **not** try to solve the embedding leak by mapping `chunk_embeddings` as an EF entity so the cascade reaches it: the four vector stores own that storage and two of them are not relational at all.
- **No new NuGet packages.**
- **Commits:** concise messages matching repo style. **NEVER** add a `Co-Authored-By` trailer (user global instruction — overrides any default).
- **`DateTimeOffset` does not translate on the SQLite provider** — relational comparisons, `Max`/`Min` and `ORDER BY` all throw. Nothing here compares dates in SQL; if a task tempts you to, stop and report.
- **Never load `FileContent` or `ExtractedText` to answer a question about identity or membership.** That is the bug shape this plan exists to finish removing: every new read projects scalar columns.
- **Baseline before Task 1:** full suite 283 passed / 17 skipped (SQL Server, no server reachable) / 0 failed.
- **Report evidence rule:** any claim about build warnings must come from `dotnet build --no-incremental`. An incremental build emits nothing for projects it does not recompile, so grepping an incremental log proves nothing — that mistake has been made four times across the predecessor plans. If you did not run a clean build, say you did not check.

## Branch

**Continue on `feature/dashboard`.** An earlier draft of this plan said to cut a fresh branch from `develop` on the grounds that these are pre-existing defects. That was wrong: four of the five tasks would not compile there. `develop` has no `ReindexDocumentAsync`, no `DocumentIndexingException`, no `#region Dashboard Aggregates` on `IDocumentRepository`, and no `DocumentRepositoryAggregateTests` base class — all of it arrived with the two predecessor plans. The defects predate that work, but the code these fixes attach to does not.

A separate branch cut from `feature/dashboard` would also work and would keep the two bodies of work independently revertable, at the cost of a merge chain: it could not reach `develop` until the dashboard branch did. Since the dashboard branch's fate is still undecided, one branch is less to manage. Do not create a second branch without being told to.

---

## File Structure

**Created:**

| File | Responsibility |
|---|---|
| `src/Mjm.LocalDocs.Core/Services/ProjectService.cs` | Project lifecycle. Owns the "strip content, then delete the project" ordering |
| `src/Mjm.LocalDocs.Core/Abstractions/IDocumentLockRegistry.cs` | Per-document mutual exclusion for the wipe-and-rebuild paths |
| `src/Mjm.LocalDocs.Infrastructure/Concurrency/DocumentLockRegistry.cs` | The singleton implementation |
| `tests/Mjm.LocalDocs.Tests/Services/ProjectServiceTests.cs` | Deletion order and completeness |
| `tests/Mjm.LocalDocs.Tests/Concurrency/DocumentLockRegistryTests.cs` | Mutual exclusion, per-document independence, release on dispose |

**Modified:**

| File | Change |
|---|---|
| `src/Mjm.LocalDocs.Core/Abstractions/IDocumentRepository.cs` | +2 narrow reads |
| `src/Mjm.LocalDocs.Infrastructure/Persistence/Repositories/EfCoreDocumentRepository.cs` | Implement +2 |
| `src/Mjm.LocalDocs.Infrastructure/VectorStore/InMemoryDocumentRepository.cs` | Implement +2 |
| `src/Mjm.LocalDocs.Core/Models/Dashboard/DashboardReadModels.cs` | +2 read records |
| `src/Mjm.LocalDocs.Core/Services/DocumentService.cs` | Take the lock in `UpdateDocumentAsync` and `ReindexDocumentAsync`; rewrite `SearchAsync`'s filtering |
| `src/Mjm.LocalDocs.Core/DependencyInjection/ServiceCollectionExtensions.cs` | Register `ProjectService`; inject the lock registry into `DocumentService` |
| `src/Mjm.LocalDocs.Infrastructure/DependencyInjection/ServiceCollectionExtensions.cs` | Register `IDocumentLockRegistry` as a singleton |
| `src/Mjm.LocalDocs.Server/Components/Pages/Projects/ProjectList.razor` | Delete through `ProjectService` |
| `src/Mjm.LocalDocs.Server/Components/Pages/Projects/ProjectDetail.razor` | Delete through `ProjectService` |
| `src/Mjm.LocalDocs.Server/McpTools/ProjectTools.cs` | Delete through `ProjectService` |
| `tests/Mjm.LocalDocs.Tests/Repositories/DocumentRepositoryAggregateTests.cs` | New cases (abstract base — both fixtures inherit them) |
| `tests/Mjm.LocalDocs.Tests/Services/DocumentServiceTests.cs` | New `SearchAsync` cases |
| `tests/Mjm.LocalDocs.Tests/Services/DocumentServiceIndexingTests.cs` | New locking cases |

---

## Task 1: A narrow read of a project's file locations

**Files:**
- Modify: `src/Mjm.LocalDocs.Core/Models/Dashboard/DashboardReadModels.cs`
- Modify: `src/Mjm.LocalDocs.Core/Abstractions/IDocumentRepository.cs`
- Modify: `src/Mjm.LocalDocs.Infrastructure/Persistence/Repositories/EfCoreDocumentRepository.cs`
- Modify: `src/Mjm.LocalDocs.Infrastructure/VectorStore/InMemoryDocumentRepository.cs`
- Test: `tests/Mjm.LocalDocs.Tests/Repositories/DocumentRepositoryAggregateTests.cs`

**Interfaces:**
- Consumes: nothing.
- Produces: `record DocumentFileLocation(string DocumentId, string? StorageLocation)`; `IDocumentRepository.GetFileLocationsByProjectAsync(string projectId, CancellationToken) → Task<IReadOnlyList<DocumentFileLocation>>`. Task 2 consumes both.

**Why this task exists.** Task 2 has to visit every document in a project to strip two things that no cascade reaches. It needs only each document's id and its external storage path — two scalar columns. The obvious existing call, `GetDocumentsByProjectAsync`, materialises whole entities including `FileContent`, so with database file storage it would pull every blob in the project into memory just to enumerate paths. That is the exact bug the predecessor plans removed from the dashboard, and reintroducing it inside a fix would be embarrassing.

- [ ] **Step 1: Add the record**

In `DashboardReadModels.cs`, append:

```csharp
/// <summary>
/// Where a document's original file lives, if it lives outside the database.
/// </summary>
/// <param name="DocumentId">The document identifier.</param>
/// <param name="StorageLocation">
/// The external storage path, or null when the file content is held in the database row.
/// </param>
public sealed record DocumentFileLocation(string DocumentId, string? StorageLocation);
```

The file's namespace is `Mjm.LocalDocs.Core.Models.Dashboard`, which is now slightly wrong for a record used by deletion rather than the dashboard. Leave it: moving it means touching every existing `using`, and one misnamed namespace is a smaller problem than a churn commit across the solution. Note it and move on.

- [ ] **Step 2: Write the failing tests**

Append to the abstract base `tests/Mjm.LocalDocs.Tests/Repositories/DocumentRepositoryAggregateTests.cs`. Both concrete fixtures inherit them, so each case runs twice — once against real SQLite, once in memory. Add nothing to either subclass.

```csharp
    [Fact]
    public async Task GetFileLocationsByProjectAsync_ReturnsEveryDocumentInTheProject()
    {
        await SeedDocumentAsync("doc-1", projectId: "proj-a");
        await SeedDocumentAsync("doc-2", projectId: "proj-a");
        await SeedDocumentAsync("doc-3", projectId: "proj-b");

        var locations = await Sut.GetFileLocationsByProjectAsync("proj-a");

        Assert.Equal(2, locations.Count);
        Assert.Contains(locations, l => l.DocumentId == "doc-1");
        Assert.Contains(locations, l => l.DocumentId == "doc-2");
    }

    [Fact]
    public async Task GetFileLocationsByProjectAsync_IncludesSupersededDocuments()
    {
        // A superseded version still owns an external file and may still own embeddings, so
        // deletion has to visit it too.
        await SeedDocumentAsync("doc-1", projectId: "proj-a", isSuperseded: true);

        var locations = await Sut.GetFileLocationsByProjectAsync("proj-a");

        Assert.Single(locations);
    }

    [Fact]
    public async Task GetFileLocationsByProjectAsync_WithNoDocuments_ReturnsEmpty()
    {
        var locations = await Sut.GetFileLocationsByProjectAsync("proj-none");

        Assert.Empty(locations);
    }
```

`SeedDocumentAsync` does not set `FileStorageLocation`, so every seeded document reports null — which is the database-storage case. That is the shape these three cases need; the non-null path is exercised in Task 2 against a substitute.

- [ ] **Step 3: Run the tests to verify they fail**

Run: `dotnet test tests/Mjm.LocalDocs.Tests/Mjm.LocalDocs.Tests.csproj --filter "FullyQualifiedName~GetFileLocationsByProjectAsync"`
Expected: BUILD FAILURE — `'IDocumentRepository' does not contain a definition for 'GetFileLocationsByProjectAsync'`.

- [ ] **Step 4: Add the interface member**

Append inside `IDocumentRepository.cs`'s existing `#region Dashboard Aggregates`:

```csharp
    /// <summary>
    /// Gets the identifier and external storage path of every document in a project,
    /// superseded versions included.
    /// </summary>
    /// <remarks>
    /// Two scalar columns, deliberately. Deleting a project has to visit each document to remove
    /// its embeddings and its externally-stored file, neither of which any database cascade
    /// reaches — and enumerating them through <see cref="GetDocumentsByProjectAsync"/> would
    /// materialise every <c>FileContent</c> blob in the project to do it.
    /// </remarks>
    /// <param name="projectId">The project identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>One record per document, in no guaranteed order.</returns>
    Task<IReadOnlyList<DocumentFileLocation>> GetFileLocationsByProjectAsync(
        string projectId,
        CancellationToken cancellationToken = default);
```

- [ ] **Step 5: Implement in both repositories**

`EfCoreDocumentRepository`, inside its `#region Dashboard Aggregates`:

```csharp
    /// <inheritdoc />
    public async Task<IReadOnlyList<DocumentFileLocation>> GetFileLocationsByProjectAsync(
        string projectId,
        CancellationToken cancellationToken = default)
    {
        return await _context.Documents
            .AsNoTracking()
            .Where(d => d.ProjectId == projectId)
            .Select(d => new DocumentFileLocation(d.Id, d.FileStorageLocation))
            .ToListAsync(cancellationToken);
    }
```

`InMemoryDocumentRepository`, inside its `#region Dashboard Aggregates`:

```csharp
    /// <inheritdoc />
    public Task<IReadOnlyList<DocumentFileLocation>> GetFileLocationsByProjectAsync(
        string projectId,
        CancellationToken cancellationToken = default)
    {
        var locations = _documents.Values
            .Where(d => string.Equals(d.ProjectId, projectId, StringComparison.Ordinal))
            .Select(d => new DocumentFileLocation(d.Id, d.FileStorageLocation))
            .ToList();

        return Task.FromResult<IReadOnlyList<DocumentFileLocation>>(locations);
    }
```

- [ ] **Step 6: Run the tests to verify they pass**

Run: `dotnet test tests/Mjm.LocalDocs.Tests/Mjm.LocalDocs.Tests.csproj --filter "FullyQualifiedName~GetFileLocationsByProjectAsync"`
Expected: PASS, 6 tests (3 per implementation).

- [ ] **Step 7: Commit**

```bash
git add src/Mjm.LocalDocs.Core src/Mjm.LocalDocs.Infrastructure tests/Mjm.LocalDocs.Tests/Repositories/DocumentRepositoryAggregateTests.cs
git commit -m "Add a narrow read of a project's document file locations"
```

---

## Task 2: Deleting a project stops leaking embeddings and files

**Files:**
- Create: `src/Mjm.LocalDocs.Core/Services/ProjectService.cs`
- Create: `tests/Mjm.LocalDocs.Tests/Services/ProjectServiceTests.cs`
- Modify: `src/Mjm.LocalDocs.Core/DependencyInjection/ServiceCollectionExtensions.cs`
- Modify: `src/Mjm.LocalDocs.Server/Components/Pages/Projects/ProjectList.razor`
- Modify: `src/Mjm.LocalDocs.Server/Components/Pages/Projects/ProjectDetail.razor`
- Modify: `src/Mjm.LocalDocs.Server/McpTools/ProjectTools.cs`

**Interfaces:**
- Consumes: `GetFileLocationsByProjectAsync` and `DocumentFileLocation` (Task 1).
- Produces: `ProjectService.DeleteProjectAsync(string projectId, CancellationToken) → Task<bool>`. Nothing later consumes it.

**Why this task exists.** All three project-delete call sites call `IProjectRepository.DeleteAsync`, which removes the Projects row and lets EF cascade to Documents and DocumentChunks. Two things are outside that cascade:

- **Embeddings.** `chunk_embeddings` is owned by the vector stores, not by EF. So every project deletion orphans every embedding it held. This is the source of the 36-embeddings-for-33-chunks in the development database, and it is what permanently defeats the dashboard's chunk-versus-embedding fast path.
- **Externally-stored files.** With `FileStorage: FileSystem` or `AzureBlob`, the document row holds only a path. `DocumentService.DeleteDocumentAsync` deletes the file for a single document; the project cascade deletes nothing. So every project deletion leaves its files on disk or in blob storage forever. This one is not merely a performance wart — it is unbounded storage growth and, for a blob account, unbounded cost.

The fix is a service that does the whole job, so no call site has to remember a second step. That the bug existed at three sites at once is the argument for the shape of the fix.

- [ ] **Step 1: Write the failing tests**

Create `tests/Mjm.LocalDocs.Tests/Services/ProjectServiceTests.cs`:

```csharp
using Mjm.LocalDocs.Core.Abstractions;
using Mjm.LocalDocs.Core.Models.Dashboard;
using Mjm.LocalDocs.Core.Services;
using NSubstitute;

namespace Mjm.LocalDocs.Tests.Services;

/// <summary>
/// Unit tests for <see cref="ProjectService"/>.
/// </summary>
public sealed class ProjectServiceTests
{
    private readonly IProjectRepository _projects = Substitute.For<IProjectRepository>();
    private readonly IDocumentRepository _documents = Substitute.For<IDocumentRepository>();
    private readonly IVectorStore _vectorStore = Substitute.For<IVectorStore>();
    private readonly IDocumentFileStorage _fileStorage = Substitute.For<IDocumentFileStorage>();

    private ProjectService CreateSut(bool withFileStorage = true) =>
        new(_projects, _documents, _vectorStore, withFileStorage ? _fileStorage : null);

    [Fact]
    public async Task DeleteProjectAsync_RemovesEveryDocumentsEmbeddings()
    {
        _documents.GetFileLocationsByProjectAsync("proj-1", Arg.Any<CancellationToken>())
            .Returns([
                new DocumentFileLocation("doc-1", null),
                new DocumentFileLocation("doc-2", null)
            ]);
        _projects.DeleteAsync("proj-1", Arg.Any<CancellationToken>()).Returns(true);

        await CreateSut().DeleteProjectAsync("proj-1");

        // chunk_embeddings is not an EF table, so no cascade reaches it.
        await _vectorStore.Received(1).DeleteByDocumentIdAsync("doc-1", Arg.Any<CancellationToken>());
        await _vectorStore.Received(1).DeleteByDocumentIdAsync("doc-2", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DeleteProjectAsync_RemovesExternallyStoredFiles()
    {
        _documents.GetFileLocationsByProjectAsync("proj-1", Arg.Any<CancellationToken>())
            .Returns([new DocumentFileLocation("doc-1", "proj-1/doc-1.pdf")]);
        _projects.DeleteAsync("proj-1", Arg.Any<CancellationToken>()).Returns(true);

        await CreateSut().DeleteProjectAsync("proj-1");

        // With FileSystem or AzureBlob storage the row holds only a path, so the cascade leaves
        // the file behind — on disk, or costing money in a blob account.
        await _fileStorage.Received(1).DeleteFileAsync(
            "doc-1", "proj-1/doc-1.pdf", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DeleteProjectAsync_WithDatabaseStorage_DoesNotCallFileStorage()
    {
        _documents.GetFileLocationsByProjectAsync("proj-1", Arg.Any<CancellationToken>())
            .Returns([new DocumentFileLocation("doc-1", null)]);
        _projects.DeleteAsync("proj-1", Arg.Any<CancellationToken>()).Returns(true);

        await CreateSut().DeleteProjectAsync("proj-1");

        // A null location means the content lives in the row and goes with the cascade.
        await _fileStorage.DidNotReceive().DeleteFileAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DeleteProjectAsync_StripsContentBeforeDeletingTheProject()
    {
        _documents.GetFileLocationsByProjectAsync("proj-1", Arg.Any<CancellationToken>())
            .Returns([new DocumentFileLocation("doc-1", null)]);
        _projects.DeleteAsync("proj-1", Arg.Any<CancellationToken>()).Returns(true);

        await CreateSut().DeleteProjectAsync("proj-1");

        // Deleting the project first would cascade the document rows away, losing the very ids
        // needed to find the embeddings and files that outlive them.
        Received.InOrder(() =>
        {
            _vectorStore.DeleteByDocumentIdAsync("doc-1", Arg.Any<CancellationToken>());
            _projects.DeleteAsync("proj-1", Arg.Any<CancellationToken>());
        });
    }

    [Fact]
    public async Task DeleteProjectAsync_WhenTheProjectDoesNotExist_ReturnsFalse()
    {
        _documents.GetFileLocationsByProjectAsync("proj-none", Arg.Any<CancellationToken>())
            .Returns([]);
        _projects.DeleteAsync("proj-none", Arg.Any<CancellationToken>()).Returns(false);

        var deleted = await CreateSut().DeleteProjectAsync("proj-none");

        Assert.False(deleted);
    }

    [Fact]
    public async Task DeleteProjectAsync_WithNoFileStorageConfigured_StillRemovesEmbeddings()
    {
        _documents.GetFileLocationsByProjectAsync("proj-1", Arg.Any<CancellationToken>())
            .Returns([new DocumentFileLocation("doc-1", "proj-1/doc-1.pdf")]);
        _projects.DeleteAsync("proj-1", Arg.Any<CancellationToken>()).Returns(true);

        await CreateSut(withFileStorage: false).DeleteProjectAsync("proj-1");

        await _vectorStore.Received(1).DeleteByDocumentIdAsync("doc-1", Arg.Any<CancellationToken>());
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/Mjm.LocalDocs.Tests/Mjm.LocalDocs.Tests.csproj --filter "FullyQualifiedName~ProjectServiceTests"`
Expected: BUILD FAILURE — `The type or namespace name 'ProjectService' could not be found`.

- [ ] **Step 3: Write the service**

Create `src/Mjm.LocalDocs.Core/Services/ProjectService.cs`:

```csharp
using Mjm.LocalDocs.Core.Abstractions;

namespace Mjm.LocalDocs.Core.Services;

/// <summary>
/// Project lifecycle operations that span more than one store.
/// </summary>
/// <remarks>
/// Exists because deleting a project is not a single-store operation and was being treated as
/// one. Removing the project row cascades through EF to its documents and chunks, but embeddings
/// live in a store EF knows nothing about, and externally-stored files live outside the database
/// entirely. Both were left behind at all three call sites. Owning the whole sequence here means
/// no caller has to remember the parts a cascade cannot reach.
/// </remarks>
public sealed class ProjectService
{
    private readonly IProjectRepository _projects;
    private readonly IDocumentRepository _documents;
    private readonly IVectorStore _vectorStore;
    private readonly IDocumentFileStorage? _fileStorage;

    /// <summary>
    /// Creates a new <see cref="ProjectService"/>.
    /// </summary>
    /// <param name="projects">Project repository.</param>
    /// <param name="documents">Document repository.</param>
    /// <param name="vectorStore">Vector store holding the embeddings.</param>
    /// <param name="fileStorage">
    /// External file storage, or null when file content is held in the database.
    /// </param>
    public ProjectService(
        IProjectRepository projects,
        IDocumentRepository documents,
        IVectorStore vectorStore,
        IDocumentFileStorage? fileStorage = null)
    {
        _projects = projects;
        _documents = documents;
        _vectorStore = vectorStore;
        _fileStorage = fileStorage;
    }

    /// <summary>
    /// Deletes a project along with everything belonging to it, including the parts no database
    /// cascade reaches.
    /// </summary>
    /// <param name="projectId">The project identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if the project existed and was deleted, false if it was not found.</returns>
    public async Task<bool> DeleteProjectAsync(
        string projectId,
        CancellationToken cancellationToken = default)
    {
        // Read the ids first: deleting the project cascades the document rows away, and with them
        // the only record of which embeddings and files were supposed to go too.
        var locations = await _documents.GetFileLocationsByProjectAsync(projectId, cancellationToken);

        foreach (var location in locations)
        {
            if (_fileStorage is not null && !string.IsNullOrEmpty(location.StorageLocation))
            {
                await _fileStorage.DeleteFileAsync(
                    location.DocumentId,
                    location.StorageLocation,
                    cancellationToken);
            }

            await _vectorStore.DeleteByDocumentIdAsync(location.DocumentId, cancellationToken);
        }

        return await _projects.DeleteAsync(projectId, cancellationToken);
    }
}
```

- [ ] **Step 4: Register it**

In `src/Mjm.LocalDocs.Core/DependencyInjection/ServiceCollectionExtensions.cs`, add beside the existing `DocumentService` registration. It needs the same optional-file-storage treatment `DocumentService` gets, so use a factory rather than plain `AddScoped<ProjectService>()`:

```csharp
        services.AddScoped<ProjectService>(sp => new ProjectService(
            sp.GetRequiredService<IProjectRepository>(),
            sp.GetRequiredService<IDocumentRepository>(),
            sp.GetRequiredService<IVectorStore>(),
            sp.GetService<IDocumentFileStorage>()));
```

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test tests/Mjm.LocalDocs.Tests/Mjm.LocalDocs.Tests.csproj --filter "FullyQualifiedName~ProjectServiceTests"`
Expected: PASS, 6 tests.

- [ ] **Step 6: Switch the three call sites**

Each currently deletes through `IProjectRepository`. Change each to use `ProjectService`, leaving the surrounding confirmation dialogs, messages and navigation exactly as they are.

`Components/Pages/Projects/ProjectList.razor` — add `@inject ProjectService ProjectService` beside the existing injections, add `@using Mjm.LocalDocs.Core.Services` if the page does not already have it, and change the delete call to `await ProjectService.DeleteProjectAsync(project.Id);`.

`Components/Pages/Projects/ProjectDetail.razor` — same three changes; the call becomes `await ProjectService.DeleteProjectAsync(_project.Id);`.

`McpTools/ProjectTools.cs` — add a `ProjectService` constructor parameter and field alongside the existing `IProjectRepository` one (which the file still needs for its other tools), and change the delete call to `await _projectService.DeleteProjectAsync(projectId, cancellationToken);`.

Do **not** remove `IProjectRepository` from any of the three — all three use it for reads and creates.

- [ ] **Step 7: Verify**

Run: `dotnet build --no-incremental`
Expected: 0 errors. Grep the output for each changed filename and report what you find.

Run: `dotnet test`
Expected: PASS, 295 (283 baseline + 6 from Task 1 + 6 here).

- [ ] **Step 8: Commit**

```bash
git add src/Mjm.LocalDocs.Core src/Mjm.LocalDocs.Server tests/Mjm.LocalDocs.Tests/Services/ProjectServiceTests.cs
git commit -m "Delete a project's embeddings and files, not just its rows"
```

---

## Task 3: A per-document lock registry

**Files:**
- Create: `src/Mjm.LocalDocs.Core/Abstractions/IDocumentLockRegistry.cs`
- Create: `src/Mjm.LocalDocs.Infrastructure/Concurrency/DocumentLockRegistry.cs`
- Create: `tests/Mjm.LocalDocs.Tests/Concurrency/DocumentLockRegistryTests.cs`
- Modify: `src/Mjm.LocalDocs.Infrastructure/DependencyInjection/ServiceCollectionExtensions.cs`

**Interfaces:**
- Consumes: nothing.
- Produces: `IDocumentLockRegistry.AcquireAsync(string documentId, CancellationToken) → Task<IAsyncDisposable>`. Task 4 consumes it.

**Why this task exists.** `DocumentService.ReindexDocumentAsync` wipes a document's index and rebuilds it. That is safe to retry in sequence and unsafe to run twice at once — two interleaved runs can leave a document with no chunks while both report success. The UI already guards against a double-click, in three separate handlers, each with its own `_busy` flag. Those guards are per-component: two browser circuits, or a reindex racing an MCP `update_document` on the same document, still interleave. `UpdateDocumentAsync` has the same shape one level up — it reads `existing.IsSuperseded` and then acts on it, so two concurrent updates of one document both pass the guard and produce two active siblings.

The invariant belongs to the service, not to a component. The registry must be a **singleton**: `DocumentService` is registered scoped, so a `SemaphoreSlim` field on it would be per-request and protect nothing.

**Granularity, and the bound this accepts.** Locking per document rather than globally is the correct granularity — chunk ids are prefixed by document id and the vector store is keyed by chunk id, so operations on different documents cannot conflict. The implementation keeps one `SemaphoreSlim` per document id in a `ConcurrentDictionary` and never evicts them. That is a deliberate, bounded leak: one small object per document touched since process start, which for a local documentation tool is negligible, and evicting safely would need reference counting that buys nothing here. Do not add eviction; do note the bound in the XML docs.

- [ ] **Step 1: Write the failing tests**

Create `tests/Mjm.LocalDocs.Tests/Concurrency/DocumentLockRegistryTests.cs`:

```csharp
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
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/Mjm.LocalDocs.Tests/Mjm.LocalDocs.Tests.csproj --filter "FullyQualifiedName~DocumentLockRegistryTests"`
Expected: BUILD FAILURE — `The type or namespace name 'IDocumentLockRegistry' could not be found`.

- [ ] **Step 3: Write the interface**

Create `src/Mjm.LocalDocs.Core/Abstractions/IDocumentLockRegistry.cs`:

```csharp
namespace Mjm.LocalDocs.Core.Abstractions;

/// <summary>
/// Serialises operations that mutate one document's index, so two of them cannot interleave.
/// </summary>
/// <remarks>
/// Implementations must be registered as singletons. The services that use this are scoped, so a
/// lock held per service instance would be per-request and would protect nothing across browser
/// circuits or between the web UI and the MCP tools.
/// </remarks>
public interface IDocumentLockRegistry
{
    /// <summary>
    /// Waits for exclusive access to one document and returns a handle that releases it.
    /// </summary>
    /// <param name="documentId">The document to lock. Locks on different documents are independent.</param>
    /// <param name="cancellationToken">Cancellation token, observed while waiting.</param>
    /// <returns>A handle whose disposal releases the lock.</returns>
    Task<IAsyncDisposable> AcquireAsync(
        string documentId,
        CancellationToken cancellationToken = default);
}
```

- [ ] **Step 4: Write the implementation**

Create `src/Mjm.LocalDocs.Infrastructure/Concurrency/DocumentLockRegistry.cs`:

```csharp
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
            // Guarded: releasing twice would raise the permit count above one and silently
            // dissolve the mutual exclusion for every later caller.
            if (!_released)
            {
                _released = true;
                _gate.Release();
            }

            return ValueTask.CompletedTask;
        }
    }
}
```

- [ ] **Step 5: Register it as a singleton**

In `src/Mjm.LocalDocs.Infrastructure/DependencyInjection/ServiceCollectionExtensions.cs`, add alongside the other singleton registrations:

```csharp
        services.AddSingleton<IDocumentLockRegistry, DocumentLockRegistry>();
```

If the file has more than one registration method, put it in the one that always runs regardless of the configured storage provider — the lock is not storage-specific. Say in your report which method you chose and why.

- [ ] **Step 6: Run the tests to verify they pass**

Run: `dotnet test tests/Mjm.LocalDocs.Tests/Mjm.LocalDocs.Tests.csproj --filter "FullyQualifiedName~DocumentLockRegistryTests"`
Expected: PASS, 5 tests.

- [ ] **Step 7: Commit**

```bash
git add src/Mjm.LocalDocs.Core/Abstractions/IDocumentLockRegistry.cs src/Mjm.LocalDocs.Infrastructure tests/Mjm.LocalDocs.Tests/Concurrency
git commit -m "Add a per-document lock registry"
```

---

## Task 4: Serialise the paths that wipe and rebuild

**Files:**
- Modify: `src/Mjm.LocalDocs.Core/Services/DocumentService.cs`
- Modify: `src/Mjm.LocalDocs.Core/DependencyInjection/ServiceCollectionExtensions.cs`
- Test: `tests/Mjm.LocalDocs.Tests/Services/DocumentServiceIndexingTests.cs`

**Interfaces:**
- Consumes: `IDocumentLockRegistry` (Task 3).
- Produces: `DocumentService`'s constructor gains a required `IDocumentLockRegistry` parameter. Nothing later consumes it.

**Why this task exists.** Task 3 built the primitive; this applies it to the two methods that need it. `ReindexDocumentAsync` strips then rebuilds. `UpdateDocumentAsync` reads `existing.IsSuperseded` and then acts on it, which is a check-then-act over the same document. Both are correct in sequence and wrong concurrently.

`AddDocumentAsync` is deliberately **not** locked: it creates a document nobody else can be holding a reference to yet, so there is nothing to contend with. Record that in the code rather than only here — add a remark to its XML docs saying it must not acquire the registry, and why. The lock is non-reentrant, so a future acquisition keyed on `ParentDocumentId` would deadlock instantly and permanently: `UpdateDocumentAsync` calls it while already holding the lock on exactly that id.

Also update the summary comment on `AddLocalDocsCoreServices` in the Core `ServiceCollectionExtensions`, which lists the services this registration requires — it now hard-requires `IDocumentLockRegistry` through `GetRequiredService` and does not say so.

**On the constructor parameter.** It is required, not optional with a null-object default. A silently-absent lock would turn this whole task into decoration, and the compiler finding every construction site — including the tests — is the point.

- [ ] **Step 1: Write the failing tests**

Append to `tests/Mjm.LocalDocs.Tests/Services/DocumentServiceIndexingTests.cs`:

```csharp
    [Fact]
    public async Task ReindexDocumentAsync_TakesTheDocumentLock()
    {
        _repository.GetDocumentAsync("doc-1", Arg.Any<CancellationToken>()).Returns(CreateDocument());
        GivenChunks("doc-1", 1);
        _embeddingService.GenerateEmbeddingsAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>())
            .Returns([new float[] { 0.1f }]);

        await _sut.ReindexDocumentAsync("doc-1");

        // The UI's per-component busy flags cannot stop two circuits, or a reindex racing an MCP
        // update, from interleaving a wipe with a rebuild.
        await _locks.Received(1).AcquireAsync("doc-1", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ReindexDocumentAsync_ReleasesTheLockWhenIndexingFails()
    {
        _repository.GetDocumentAsync("doc-1", Arg.Any<CancellationToken>()).Returns(CreateDocument());
        GivenChunks("doc-1", 1);
        _embeddingService.GenerateEmbeddingsAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new HttpRequestException("provider unreachable"));

        await Assert.ThrowsAsync<DocumentIndexingException>(() => _sut.ReindexDocumentAsync("doc-1"));

        // A lock leaked on the failure path would wedge that document forever.
        await _lockHandle.Received(1).DisposeAsync();
    }

    [Fact]
    public async Task ReindexDocumentAsync_WhenRefused_TakesTheLockAndReleasesIt()
    {
        _repository.GetDocumentAsync("doc-1", Arg.Any<CancellationToken>())
            .Returns(CreateDocument(isSuperseded: true));

        await Assert.ThrowsAsync<InvalidOperationException>(() => _sut.ReindexDocumentAsync("doc-1"));

        // The guard reads happen under the lock, because their answers can be invalidated by a
        // concurrent update. So a refusal does acquire — and must still release.
        await _locks.Received(1).AcquireAsync("doc-1", Arg.Any<CancellationToken>());
        await _lockHandle.Received(1).DisposeAsync();
    }

    [Fact]
    public async Task ReindexDocumentAsync_ReadsTheDocumentOnlyAfterTakingTheLock()
    {
        _repository.GetDocumentAsync("doc-1", Arg.Any<CancellationToken>()).Returns(CreateDocument());
        GivenChunks("doc-1", 1);
        _embeddingService.GenerateEmbeddingsAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>())
            .Returns([new float[] { 0.1f }]);

        await _sut.ReindexDocumentAsync("doc-1");

        // Reading first would let a concurrent update supersede the document between the guard
        // and the rebuild. Received(1) alone is order-insensitive and would not catch that.
        Received.InOrder(() =>
        {
            _locks.AcquireAsync("doc-1", Arg.Any<CancellationToken>());
            _repository.GetDocumentAsync("doc-1", Arg.Any<CancellationToken>());
        });
    }

    [Fact]
    public async Task UpdateDocumentAsync_TakesTheLockBeforeReadingTheDocument()
    {
        var existing = CreateDocument("doc-1");
        var newVersion = CreateDocument("doc-2", parentDocumentId: "doc-1");

        _repository.GetDocumentAsync("doc-1", Arg.Any<CancellationToken>()).Returns(existing);
        _repository.AddDocumentAsync(Arg.Any<Document>(), Arg.Any<CancellationToken>()).Returns(newVersion);
        GivenChunks("doc-2", 1);
        _embeddingService.GenerateEmbeddingsAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>())
            .Returns([new float[] { 0.1f }]);

        await _sut.UpdateDocumentAsync("doc-1", newVersion);

        // The IsSuperseded read is half of the check-then-act being protected, so acquiring after
        // it would protect nothing. Received(1) is order-insensitive and would miss the mistake.
        Received.InOrder(() =>
        {
            _locks.AcquireAsync("doc-1", Arg.Any<CancellationToken>());
            _repository.GetDocumentAsync("doc-1", Arg.Any<CancellationToken>());
        });
    }

    [Fact]
    public async Task UpdateDocumentAsync_WhenTheTargetIsAlreadySuperseded_ReleasesTheLock()
    {
        _repository.GetDocumentAsync("doc-1", Arg.Any<CancellationToken>())
            .Returns(CreateDocument("doc-1", isSuperseded: true));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _sut.UpdateDocumentAsync("doc-1", CreateDocument("doc-2", parentDocumentId: "doc-1")));

        // This refusal throws while holding the lock — the highest-value release path in the change.
        await _lockHandle.Received(1).DisposeAsync();
    }

    [Fact]
    public async Task UpdateDocumentAsync_TakesTheLockOnTheDocumentBeingReplaced()
    {
        var existing = CreateDocument("doc-1");
        var newVersion = CreateDocument("doc-2", parentDocumentId: "doc-1");

        _repository.GetDocumentAsync("doc-1", Arg.Any<CancellationToken>()).Returns(existing);
        _repository.AddDocumentAsync(Arg.Any<Document>(), Arg.Any<CancellationToken>()).Returns(newVersion);
        GivenChunks("doc-2", 1);
        _embeddingService.GenerateEmbeddingsAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>())
            .Returns([new float[] { 0.1f }]);

        await _sut.UpdateDocumentAsync("doc-1", newVersion);

        // The IsSuperseded read followed by the supersede is a check-then-act: two concurrent
        // updates of one document would both pass the guard and leave two active siblings.
        await _locks.Received(1).AcquireAsync("doc-1", Arg.Any<CancellationToken>());
    }
```

The fixture needs the two new substitutes. Add these fields beside the existing ones and wire them into the constructor call:

```csharp
    private readonly IDocumentLockRegistry _locks = Substitute.For<IDocumentLockRegistry>();
    private readonly IAsyncDisposable _lockHandle = Substitute.For<IAsyncDisposable>();
```

and in the test class's constructor, before `_sut` is created:

```csharp
        _locks.AcquireAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(_lockHandle);
```

Every `new DocumentService(...)` in the test project now needs the registry argument. `DocumentServiceTests` has its own fixture — give it the same two fields and the same stub, so its existing tests keep passing untouched.

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/Mjm.LocalDocs.Tests/Mjm.LocalDocs.Tests.csproj --filter "FullyQualifiedName~DocumentService"`
Expected: BUILD FAILURE — no `DocumentService` constructor takes an `IDocumentLockRegistry`.

- [ ] **Step 3: Take the parameter**

In `DocumentService.cs`, add the field and the constructor parameter. Place it after `embeddingService` and before the optional `fileStorage`, so the existing optional arguments keep their defaults:

```csharp
    private readonly IDocumentLockRegistry _locks;
```

```csharp
    /// <param name="locks">Per-document lock registry, serialising wipe-and-rebuild operations.</param>
    public DocumentService(
        IDocumentRepository repository,
        IVectorStore vectorStore,
        IDocumentProcessor processor,
        IEmbeddingService embeddingService,
        IDocumentLockRegistry locks,
        IDocumentFileStorage? fileStorage = null,
        FileStorageProvider fileStorageProvider = FileStorageProvider.Database)
```

with `_locks = locks;` in the body.

Update the DI factory in `src/Mjm.LocalDocs.Core/DependencyInjection/ServiceCollectionExtensions.cs` to resolve and pass it:

```csharp
            var locks = sp.GetRequiredService<IDocumentLockRegistry>();
```

placed with the other `GetRequiredService` calls, and threaded into the `new DocumentService(...)` argument list in the same position as the constructor.

- [ ] **Step 4: Hold the lock**

In `ReindexDocumentAsync`, the lock goes **first**, before the document is fetched.

An earlier draft of this plan put it after the two refusal guards, reasoning that they are pure reads which reject before any mutation, so serialising refusals would buy nothing. That reasoning is wrong, and the interleaving it permits produces the worst state in the system:

1. `ReindexDocumentAsync("P")` reads `P`, sees it is not superseded and has no active child, then blocks waiting for the lock.
2. `UpdateDocumentAsync("P")` holds the lock: it adds child `C`, strips `P`'s index, supersedes `P`, and releases.
3. The reindex acquires the lock and rebuilds from its **stale** `document` object — leaving `P` superseded **with chunks**. `CloseInterruptedUpdateAsync` retires `P`'s ancestors, never `P` itself.

That end state is exactly the one the strip-before-supersede comment a few lines above exists to prevent: absent from the active-only tallies, invisible to the chunk-versus-embedding comparison, and refused by reindex. A guard whose answer can go stale before it is acted on is not a pure read. The cost of moving the lock up is one serialised refusal per contended document, which is a real cost only in the case where correctness demands it.

So place this as the **first statement of the method body**, above the `GetDocumentAsync` call:

```csharp
        // Acquire before reading: the guards below decide on state a concurrent update can
        // invalidate. Evaluated outside the lock, a reindex could read "not superseded", block,
        // and then rebuild from a stale document after an update had superseded it — leaving a
        // superseded document that kept its chunks, which is the one state the tallies, the count
        // comparison and this very guard all fail to catch.
        await using var _ = await _locks.AcquireAsync(documentId, cancellationToken);
```

In `UpdateDocumentAsync`, the lock must be taken **before** the `IsSuperseded` read, because that read is half of the check-then-act being protected. Place it as the first statement of the method body:

```csharp
        await using var _ = await _locks.AcquireAsync(existingDocumentId, cancellationToken);
```

Both use `await using`, so the lock is released on every exit including an exception. Do not add a `try`/`finally` — that is what `await using` is.

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test`
Expected: PASS, with eight new cases across the two `DocumentService` suites. Every pre-existing `DocumentService` test must still pass with no change beyond the fixture wiring; if one needed its assertions altered, stop and report rather than adjusting it.

- [ ] **Step 6: Commit**

```bash
git add src/Mjm.LocalDocs.Core tests/Mjm.LocalDocs.Tests/Services
git commit -m "Serialise reindex and update per document"
```

---

## Task 5: Search stops loading blobs to answer questions about identity

**Files:**
- Modify: `src/Mjm.LocalDocs.Core/Models/Dashboard/DashboardReadModels.cs`
- Modify: `src/Mjm.LocalDocs.Core/Abstractions/IDocumentRepository.cs`
- Modify: `src/Mjm.LocalDocs.Infrastructure/Persistence/Repositories/EfCoreDocumentRepository.cs`
- Modify: `src/Mjm.LocalDocs.Infrastructure/VectorStore/InMemoryDocumentRepository.cs`
- Modify: `src/Mjm.LocalDocs.Core/Services/DocumentService.cs`
- Test: `tests/Mjm.LocalDocs.Tests/Repositories/DocumentRepositoryAggregateTests.cs`
- Test: `tests/Mjm.LocalDocs.Tests/Services/DocumentServiceTests.cs`

**Interfaces:**
- Consumes: nothing from Tasks 1-4.
- Produces: `record ChunkDocumentContext(string ChunkId, string DocumentId, string ProjectId, bool IsSuperseded)`; `IDocumentRepository.GetChunkDocumentContextAsync(IEnumerable<string> chunkIds, CancellationToken) → Task<IReadOnlyList<ChunkDocumentContext>>`.

**Why this task exists.** `SearchAsync` runs on every query, from the UI and from every MCP `search_docs` call. It has both of the problems the dashboard just had removed, in a hotter path:

- Its project filter calls `GetDocumentsByProjectAsync`, materialising every document in the project — `FileContent` and `ExtractedText` included — purely to build a `HashSet` of ids.
- Its superseded filter calls `GetDocumentAsync` once per distinct document among the results, each load pulling that document's full row.

Both questions are about the documents owning the matched chunks, and both can be answered by one join projecting four scalar columns. That also collapses two passes into one.

- [ ] **Step 1: Add the record**

In `DashboardReadModels.cs`, append:

```csharp
/// <summary>
/// The owning document's identity and state for one chunk, without its content.
/// </summary>
/// <param name="ChunkId">The chunk identifier.</param>
/// <param name="DocumentId">The owning document identifier.</param>
/// <param name="ProjectId">The owning project identifier, for filtering a search by project.</param>
/// <param name="IsSuperseded">Whether the owning document has been replaced by a newer version.</param>
public sealed record ChunkDocumentContext(
    string ChunkId,
    string DocumentId,
    string ProjectId,
    bool IsSuperseded);
```

- [ ] **Step 2: Write the failing repository tests**

Append to the abstract base `DocumentRepositoryAggregateTests.cs`:

```csharp
    [Fact]
    public async Task GetChunkDocumentContextAsync_ReturnsTheOwningDocumentsProjectAndState()
    {
        await SeedDocumentAsync("doc-1", projectId: "proj-a", chunkCount: 2);

        var context = await Sut.GetChunkDocumentContextAsync(["doc-1_chunk_0", "doc-1_chunk_1"]);

        Assert.Equal(2, context.Count);
        Assert.All(context, c => Assert.Equal("doc-1", c.DocumentId));
        Assert.All(context, c => Assert.Equal("proj-a", c.ProjectId));
        Assert.All(context, c => Assert.False(c.IsSuperseded));
    }

    [Fact]
    public async Task GetChunkDocumentContextAsync_ReportsASupersededOwner()
    {
        await SeedDocumentAsync("doc-1", isSuperseded: true, chunkCount: 1);

        var context = await Sut.GetChunkDocumentContextAsync(["doc-1_chunk_0"]);

        Assert.True(Assert.Single(context).IsSuperseded);
    }

    [Fact]
    public async Task GetChunkDocumentContextAsync_IgnoresUnknownChunkIds()
    {
        await SeedDocumentAsync("doc-1", chunkCount: 1);

        var context = await Sut.GetChunkDocumentContextAsync(["doc-1_chunk_0", "nonexistent_chunk_0"]);

        Assert.Equal("doc-1_chunk_0", Assert.Single(context).ChunkId);
    }

    [Fact]
    public async Task GetChunkDocumentContextAsync_WithEmptyInput_ReturnsEmpty()
    {
        await SeedDocumentAsync("doc-1", chunkCount: 1);

        var context = await Sut.GetChunkDocumentContextAsync([]);

        Assert.Empty(context);
    }
```

- [ ] **Step 3: Run the tests to verify they fail**

Run: `dotnet test tests/Mjm.LocalDocs.Tests/Mjm.LocalDocs.Tests.csproj --filter "FullyQualifiedName~GetChunkDocumentContextAsync"`
Expected: BUILD FAILURE — `'IDocumentRepository' does not contain a definition for 'GetChunkDocumentContextAsync'`.

- [ ] **Step 4: Add the interface member**

Append inside `IDocumentRepository.cs`'s `#region Dashboard Aggregates`:

```csharp
    /// <summary>
    /// Gets the owning document's project and superseded state for each of the given chunks.
    /// </summary>
    /// <remarks>
    /// Answers both of a search's post-filter questions — is this chunk's document in the
    /// requested project, and has it been superseded — in one join over four scalar columns.
    /// Unknown chunk ids are omitted rather than reported.
    /// </remarks>
    /// <param name="chunkIds">The chunk identifiers to resolve.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>One record per known chunk, in no guaranteed order.</returns>
    Task<IReadOnlyList<ChunkDocumentContext>> GetChunkDocumentContextAsync(
        IEnumerable<string> chunkIds,
        CancellationToken cancellationToken = default);
```

- [ ] **Step 5: Implement in both repositories**

`EfCoreDocumentRepository`:

```csharp
    /// <inheritdoc />
    public async Task<IReadOnlyList<ChunkDocumentContext>> GetChunkDocumentContextAsync(
        IEnumerable<string> chunkIds,
        CancellationToken cancellationToken = default)
    {
        var ids = chunkIds.ToList();
        if (ids.Count == 0)
            return [];

        return await _context.DocumentChunks
            .AsNoTracking()
            .Where(c => ids.Contains(c.Id))
            .Join(
                _context.Documents.AsNoTracking(),
                chunk => chunk.DocumentId,
                document => document.Id,
                (chunk, document) => new ChunkDocumentContext(
                    chunk.Id,
                    document.Id,
                    document.ProjectId,
                    document.IsSuperseded))
            .ToListAsync(cancellationToken);
    }
```

`InMemoryDocumentRepository`:

```csharp
    /// <inheritdoc />
    public Task<IReadOnlyList<ChunkDocumentContext>> GetChunkDocumentContextAsync(
        IEnumerable<string> chunkIds,
        CancellationToken cancellationToken = default)
    {
        var ids = chunkIds.ToHashSet(StringComparer.Ordinal);
        if (ids.Count == 0)
            return Task.FromResult<IReadOnlyList<ChunkDocumentContext>>([]);

        var context = _chunks.Values
            .Where(c => ids.Contains(c.Id))
            .Select(c => new
            {
                Chunk = c,
                Document = _documents.TryGetValue(c.DocumentId, out var d) ? d : null
            })
            .Where(x => x.Document is not null)
            .Select(x => new ChunkDocumentContext(
                x.Chunk.Id,
                x.Document!.Id,
                x.Document.ProjectId,
                x.Document.IsSuperseded))
            .ToList();

        return Task.FromResult<IReadOnlyList<ChunkDocumentContext>>(context);
    }
```

- [ ] **Step 6: Write the failing service tests**

Append to `tests/Mjm.LocalDocs.Tests/Services/DocumentServiceTests.cs`. Read that file's existing `SearchAsync` tests first and follow their setup style; these assert the new behaviour rather than the old plumbing:

```csharp
    [Fact]
    public async Task SearchAsync_WithProjectFilter_DoesNotEnumerateTheProjectsDocuments()
    {
        _embeddingService.GenerateEmbeddingAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(CreateTestEmbedding());
        _vectorStore.SearchAsync(Arg.Any<ReadOnlyMemory<float>>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns([new VectorSearchResult { ChunkId = "doc-1_chunk_0", Score = 0.9 }]);
        _repository.GetChunksByIdsAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>())
            .Returns([CreateTestChunk("doc-1_chunk_0", "doc-1")]);
        _repository.GetChunkDocumentContextAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>())
            .Returns([new ChunkDocumentContext("doc-1_chunk_0", "doc-1", "proj-a", false)]);

        var results = await _sut.SearchAsync("query", projectId: "proj-a");

        Assert.Single(results);
        // The old filter materialised every document in the project, FileContent included, to
        // build a set of ids.
        await _repository.DidNotReceive().GetDocumentsByProjectAsync(
            Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SearchAsync_ExcludesChunksFromAnotherProject()
    {
        _embeddingService.GenerateEmbeddingAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(CreateTestEmbedding());
        _vectorStore.SearchAsync(Arg.Any<ReadOnlyMemory<float>>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns([
                new VectorSearchResult { ChunkId = "doc-1_chunk_0", Score = 0.9 },
                new VectorSearchResult { ChunkId = "doc-2_chunk_0", Score = 0.8 }
            ]);
        _repository.GetChunksByIdsAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>())
            .Returns([
                CreateTestChunk("doc-1_chunk_0", "doc-1"),
                CreateTestChunk("doc-2_chunk_0", "doc-2")
            ]);
        _repository.GetChunkDocumentContextAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>())
            .Returns([
                new ChunkDocumentContext("doc-1_chunk_0", "doc-1", "proj-a", false),
                new ChunkDocumentContext("doc-2_chunk_0", "doc-2", "proj-b", false)
            ]);

        var results = await _sut.SearchAsync("query", projectId: "proj-a");

        Assert.Equal("doc-1_chunk_0", Assert.Single(results).Chunk.Id);
    }

    [Fact]
    public async Task SearchAsync_ExcludesSupersededOwnersWithoutQueryingPerDocument()
    {
        _embeddingService.GenerateEmbeddingAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(CreateTestEmbedding());
        _vectorStore.SearchAsync(Arg.Any<ReadOnlyMemory<float>>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns([
                new VectorSearchResult { ChunkId = "doc-1_chunk_0", Score = 0.9 },
                new VectorSearchResult { ChunkId = "doc-2_chunk_0", Score = 0.8 }
            ]);
        _repository.GetChunksByIdsAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>())
            .Returns([
                CreateTestChunk("doc-1_chunk_0", "doc-1"),
                CreateTestChunk("doc-2_chunk_0", "doc-2")
            ]);
        _repository.GetChunkDocumentContextAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>())
            .Returns([
                new ChunkDocumentContext("doc-1_chunk_0", "doc-1", "proj-a", false),
                new ChunkDocumentContext("doc-2_chunk_0", "doc-2", "proj-a", true)
            ]);

        var results = await _sut.SearchAsync("query");

        Assert.Equal("doc-1_chunk_0", Assert.Single(results).Chunk.Id);
        // The old safety net loaded each distinct result document one at a time.
        await _repository.DidNotReceive().GetDocumentAsync(
            Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SearchAsync_DropsAChunkWhoseOwnerIsUnknown()
    {
        _embeddingService.GenerateEmbeddingAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(CreateTestEmbedding());
        _vectorStore.SearchAsync(Arg.Any<ReadOnlyMemory<float>>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns([new VectorSearchResult { ChunkId = "orphan_chunk_0", Score = 0.9 }]);
        _repository.GetChunksByIdsAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>())
            .Returns([CreateTestChunk("orphan_chunk_0", "doc-gone")]);
        // An orphaned embedding whose chunk row survived but whose document did not.
        _repository.GetChunkDocumentContextAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>())
            .Returns([]);

        var results = await _sut.SearchAsync("query");

        Assert.Empty(results);
    }
```

- [ ] **Step 7: Rewrite the filtering**

In `SearchAsync`, replace steps 4 through 6 — from the `// 4. Filter by project if specified` comment through the end of the superseded-filter block, stopping before `return results.Take(limit).ToList();` — with:

```csharp
        // 4. Resolve each matched chunk's owning document in one join over scalar columns. This
        //    answers both post-filters at once. The old code enumerated every document in the
        //    project to build a set of ids, and then loaded each result document one at a time —
        //    both pulling FileContent and ExtractedText to answer questions about identity.
        var context = await _repository.GetChunkDocumentContextAsync(chunkIds, cancellationToken);

        var admissible = context
            .Where(c => !c.IsSuperseded)
            .Where(c => string.IsNullOrEmpty(projectId)
                        || string.Equals(c.ProjectId, projectId, StringComparison.Ordinal))
            .Select(c => c.ChunkId)
            .ToHashSet(StringComparer.Ordinal);

        // 5. Build results, keeping the vector store's ordering. A chunk with no context row is
        //    dropped: its document is gone, so the embedding is an orphan.
        var chunkDict = chunks.ToDictionary(c => c.Id);
        var results = vectorResults
            .Where(vr => admissible.Contains(vr.ChunkId) && chunkDict.ContainsKey(vr.ChunkId))
            .Select(vr => new SearchResult
            {
                Chunk = chunkDict[vr.ChunkId],
                Score = vr.Score
            })
            .ToList();
```

Note that the superseded filter is no longer a separate pass over the built results, and that it now runs before `Take(limit)` just as it did before — so a superseded document can no longer consume a slot in the returned page.

- [ ] **Step 8: Run the tests to verify they pass**

Run: `dotnet test`
Expected: PASS, 311 (299 after Task 4 + 8 repository cases across two fixtures + 4 service cases). Pre-existing `SearchAsync` tests that stubbed `GetDocumentsByProjectAsync` or `GetDocumentAsync` to drive filtering now need to stub `GetChunkDocumentContextAsync` instead — that is a legitimate change, since the behaviour under test is the same and only the collaborator changed. Do not weaken an assertion; if one cannot be expressed against the new collaborator, stop and report.

- [ ] **Step 9: Commit**

```bash
git add src/Mjm.LocalDocs.Core src/Mjm.LocalDocs.Infrastructure tests/Mjm.LocalDocs.Tests
git commit -m "Resolve search filters in one join instead of per document"
```

---

## Self-Review

**Finding coverage.** The three defects the user scoped for this plan each map to tasks: project deletion orphaning embeddings **and** externally-stored files → Tasks 1 and 2; no server-side serialisation of the wipe-and-rebuild paths → Tasks 3 and 4; `SearchAsync`'s blob-loading project filter and per-document superseded loop → Task 5.

**One finding is larger than the review that prompted it.** The whole-branch review identified the embedding leak. Reading `DocumentService.DeleteDocumentAsync` against `EfCoreProjectRepository.DeleteAsync` shows the same cascade also leaves **externally-stored files** behind, which matters more: embeddings are a correctness and performance wart, while orphaned blobs are unbounded storage growth and, on a blob account, unbounded cost. The development machine's own configuration selects `AzureBlob`, so this is live rather than theoretical. Task 2 covers both.

**Deliberately not in this plan.** A sweep to clean up embeddings and files already orphaned by past project deletions. Task 2 stops new ones; existing orphans stay until someone removes them. A sweep needs to enumerate all stored embeddings to diff them against the chunk table, which the two SQL vector stores cannot do without a new interface method, and it is a destructive maintenance operation that should be an explicit action rather than a side effect of a fix. The same argument applies to orphaned files. If wanted, it is its own plan.

**Also deliberately not in this plan.** Distributed locking. Task 3's registry is in-process and says so in its own remarks. Nothing about this application is deployed multi-instance today, and a distributed lock is a different abstraction with different failure modes.

**Placeholder scan.** No TBD, no TODO. Every code step carries the code to write.

**Type consistency.** `DocumentFileLocation(DocumentId, StorageLocation)` is defined in Task 1 and consumed in Task 2. `IDocumentLockRegistry.AcquireAsync(string, CancellationToken) → Task<IAsyncDisposable>` is defined in Task 3 and consumed in Task 4. `ChunkDocumentContext(ChunkId, DocumentId, ProjectId, IsSuperseded)` is defined and consumed within Task 5.

**Ordering dependencies.** Task 2 needs Task 1. Task 4 needs Task 3. Task 5 is independent of all four and could run first if that is more convenient.

**One risk worth naming before execution.** Task 4 makes `IDocumentLockRegistry` a required constructor parameter on `DocumentService`, which breaks every construction site in the test project. That is intentional — an optional parameter with a no-op default would let the lock silently vanish — but it means Task 4's diff touches more test files than its own new cases, and a reviewer should expect that rather than read it as scope creep.
