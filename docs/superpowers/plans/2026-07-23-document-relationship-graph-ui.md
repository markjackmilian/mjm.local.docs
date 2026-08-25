# Document Relationship Graph — UI Implementation Plan (Piano 2)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build the Blazor UI for the per-project knowledge graph on top of the already-shipped backend (Piano 1): an interactive, filterable `ProjectGraph.razor` page (Cytoscape.js bundled locally) with a filters rail, a graph canvas, and a source-documents drawer, on-demand "Analyze project / document" buttons with streaming progress, "isolate the neighborhood" search, and gating on `LocalDocs:Graph:Enabled` + an available `IChatClient`.

**Architecture:** A new interactive Blazor Server page under `Components/Pages/Projects/`, reached from a conditional button on `ProjectDetail.razor` (mirroring the existing Chat button — there is no project context in the global `NavMenu`, so no nav link is added, exactly as Chat is done today). The page reads the graph via the existing `IGraphRepository`, serializes it to JSON, and hands it to a locally-bundled Cytoscape.js instance through a small JS interop module. Node/edge taps call back into .NET (`DotNetObjectReference`) to populate the drawer from `IGraphRepository`. Analysis streams `GraphBuildService.AnalyzeProjectAsync` / `AnalyzeDocumentAsync` events with the same `await foreach` + `StateHasChanged` pattern as `ProjectChat.razor`. One small read-only backend method is added (`IGraphRepository.GetCurrentSourcesAsync`) to power the per-document filter and the "unanalyzed documents" badge efficiently.

**Tech Stack:** .NET 10, C#, Blazor Server (InteractiveServer), MudBlazor 8.15.0, Cytoscape.js (vendored UMD build, built-in `cose` layout — no CDN), `Microsoft.Extensions.AI` (`IChatClient`, consumed only via the existing extraction service), xUnit v3 + NSubstitute.

**Design spec:** `docs/superpowers/specs/2026-07-23-document-relationship-graph-design.md`
**Backend plan (Piano 1, already implemented & green):** `docs/superpowers/plans/2026-07-23-document-relationship-graph-backend.md`

## Global Constraints

- **Solution root:** `C:\Projects\mjm.local.docs\mjm.local.docs` (nested). **All `dotnet` commands and every `src/...` / `tests/...` path below are relative to this folder.** (This plan document itself lives in the outer repo at `docs/superpowers/plans/`.)
- **Branch:** work on `feature/graph` (already checked out, Piano 1 commits present).
- **Target framework:** `net10.0`; `Nullable` + `ImplicitUsings` enabled (already set in all csproj).
- **Commits:** concise messages matching repo style (e.g. `Add graph UI page`). **NEVER** add a `Co-Authored-By` trailer (user global instruction — overrides any default).
- **Domain/EF conventions:** unchanged from Piano 1 — `sealed class`, `required` members, string GUID ids. The only backend change here is one additive read-only repository method + its two implementations + tests.
- **Blazor conventions (established in this repo, follow exactly):**
  - Every interactive page starts with `@rendermode InteractiveServer` and `@attribute [Authorize]`.
  - Config is read via `@inject IOptions<LocalDocsOptions> Options` then `Options.Value.<Section>` (there is a single `IOptions<LocalDocsOptions>` singleton; `GraphOptions` is reachable as `Options.Value.Graph`). There is no separate `Configure<GraphOptions>`.
  - `MudBlazor` and `Microsoft.JSInterop` are globally imported via `Components/_Imports.razor`. Pages must add their own `@using` for `Mjm.LocalDocs.Core.Abstractions`, `Mjm.LocalDocs.Core.Configuration`, `Mjm.LocalDocs.Core.Models`, `Mjm.LocalDocs.Core.Services`, and `Microsoft.Extensions.Options`.
  - JS interop is invoked with `await JS.InvokeVoidAsync(...)` **wrapped in try/catch** (interop can fail during prerender / after circuit drop).
  - Dialogs use `IDialogService` + the shared `ConfirmDialog`; toasts use `ISnackbar`.
  - CSS uses the existing custom properties: `--ld-border`, `--ld-surface`, `--ld-text-primary`, `--ld-text-secondary`, `--ld-text-muted`, `--ld-radius-sm/md/lg/pill`, `--mud-palette-primary`, `--mud-palette-surface`.
- **Static assets:** `wwwroot` currently has only `app.css` + `favicon.svg` (no `lib/`, `js/`, `css/`). New JS is added as vendored files under `wwwroot/lib` and `wwwroot/js` and referenced with plain `<script src="...">` in `App.razor` (the app calls `app.MapStaticAssets()`, which serves them). Existing helper JS (`scrollToBottom`, `downloadFile`, `themeStorage`) lives **inline** in `App.razor`'s `<body>`.
- **No CDN at runtime:** Cytoscape.js is downloaded once into `wwwroot/lib/` and committed. The only network use is that one-time fetch during Task 4.
- **Gating (two-part, matches how `Chat.Enabled` gates Chat):** the entry button + page require `Options.Value.Graph.Enabled == true` (config intent); the page additionally checks `IGraphExtractionService.IsAvailable` (runtime capability — true only when an `IChatClient` is registered, i.e. `Chat.Enabled == true` AND `Chat.Provider ∈ { OpenAI, AzureOpenAI, Ollama }`). When enabled-but-unavailable the page renders an explanatory message instead of redirecting; when disabled it redirects to the project page.

---

## Backend contracts consumed (from Piano 1 — do not redefine)

Verbatim signatures the UI relies on. These already exist and are green.

**Models** (`src/Mjm.LocalDocs.Core/Models/`): `GraphEntity { Id, ProjectId, Name, NormalizedName, Type, NormalizedType, Description?, List<string> Aliases, FirstSeenAt, LastConfirmedAt, RetiredAt?, CreatedAt, UpdatedAt? }`; `GraphRelationship { Id, ProjectId, SourceEntityId, TargetEntityId, Label, NormalizedLabel, Description?, FirstSeenAt, LastConfirmedAt, RetiredAt?, CreatedAt, UpdatedAt? }`; `GraphSource { Id, ProjectId, GraphSourceTargetKind TargetKind, TargetId, DocumentId, int DocumentVersionNumber, Snippet?, ExtractedAt, bool IsCurrent }`; `enum GraphSourceTargetKind { Entity, Relationship }`.

**`IGraphRepository`** (`src/Mjm.LocalDocs.Core/Abstractions/IGraphRepository.cs`):
```csharp
Task<IReadOnlyList<GraphEntity>> GetEntitiesAsync(string projectId, bool includeRetired = false, CancellationToken cancellationToken = default);
Task<IReadOnlyList<GraphRelationship>> GetRelationshipsAsync(string projectId, bool includeRetired = false, CancellationToken cancellationToken = default);
Task<GraphEntity?> GetEntityAsync(string id, CancellationToken cancellationToken = default);
Task<GraphRelationship?> GetRelationshipAsync(string id, CancellationToken cancellationToken = default);
Task<IReadOnlyList<GraphSource>> GetSourcesForTargetAsync(GraphSourceTargetKind kind, string targetId, bool currentOnly = true, CancellationToken cancellationToken = default);
Task<bool> HasCurrentSourcesForDocumentAsync(string documentId, CancellationToken cancellationToken = default);
Task MarkDocumentSourcesSupersededAsync(string documentId, CancellationToken cancellationToken = default);
Task RetireTargetsWithoutCurrentSourcesAsync(string projectId, DateTimeOffset retiredAt, CancellationToken cancellationToken = default);
Task DeleteByProjectAsync(string projectId, CancellationToken cancellationToken = default);
// (Task 2 of THIS plan adds: Task<IReadOnlyList<GraphSource>> GetCurrentSourcesAsync(string projectId, CancellationToken cancellationToken = default);)
```

**`IGraphExtractionService`** (`src/Mjm.LocalDocs.Core/Abstractions/IGraphExtractionService.cs`): `bool IsAvailable { get; }` (+ extraction methods the UI does not call).

**`GraphBuildService`** (`src/Mjm.LocalDocs.Core/Services/GraphBuildService.cs`, registered `Scoped`):
```csharp
IAsyncEnumerable<GraphBuildEvent> AnalyzeDocumentAsync(string documentId, bool forceReanalyze, CancellationToken cancellationToken = default);
IAsyncEnumerable<GraphBuildEvent> AnalyzeProjectAsync(string projectId, bool forceReanalyze, CancellationToken cancellationToken = default);
```

**`GraphBuildEvent`** hierarchy (`src/Mjm.LocalDocs.Core/Services/GraphBuildEvent.cs`):
```csharp
public abstract record GraphBuildEvent;
public sealed record DocumentStartedEvent(string DocumentId, string FileName, int Index, int Total) : GraphBuildEvent;
public sealed record DocumentProgressEvent(string DocumentId, string Message) : GraphBuildEvent;
public sealed record DocumentCompletedEvent(string DocumentId, int Entities, int Relationships) : GraphBuildEvent;
public sealed record DocumentSkippedEvent(string DocumentId, string Reason) : GraphBuildEvent;
public sealed record BuildErrorEvent(string DocumentId, string Message) : GraphBuildEvent;
public sealed record BuildDoneEvent(int DocumentsProcessed, int EntitiesTotal, int RelationshipsTotal) : GraphBuildEvent;
```

**`GraphOptions`** (`Options.Value.Graph`): `bool Enabled`, `int NeighborhoodHops` (default 2), `int MaxContextChunksPerDoc`, `string? ModelOverride`.

**`DocumentService`** (concrete, registered `Scoped`): `Task<IReadOnlyList<Document>> GetDocumentsByProjectAsync(string projectId, bool includeSuperseded = true, CancellationToken = default)`; `Task<Document?> GetDocumentAsync(string documentId, CancellationToken = default)`; `Task<bool> DeleteDocumentAsync(string documentId, CancellationToken = default)`. **No `IDocumentService`/`IProjectService` exist**; projects use `IProjectRepository` (`Task<Project?> GetByIdAsync(string)`, `Task<bool> DeleteAsync(string id, CancellationToken = default)`).

**`Document`** fields used: `Id`, `ProjectId`, `FileName`, `VersionNumber`, `IsSuperseded`, `CreatedAt`.

---

## File Structure

**Backend (one small additive read method + tests)**
- `src/Mjm.LocalDocs.Core/Abstractions/IGraphRepository.cs` — add `GetCurrentSourcesAsync` (modify)
- `src/Mjm.LocalDocs.Infrastructure/Persistence/Repositories/EfCoreGraphRepository.cs` — implement it (modify)
- `src/Mjm.LocalDocs.Infrastructure/VectorStore/InMemoryGraphRepository.cs` — implement it (modify)
- `tests/Mjm.LocalDocs.Tests/Persistence/EfCoreGraphRepositoryTests.cs` — add a test (modify)
- `tests/Mjm.LocalDocs.Tests/VectorStore/InMemoryGraphRepositoryTests.cs` — add a test (modify)

**Server (UI)**
- `src/Mjm.LocalDocs.Server/wwwroot/lib/cytoscape.min.js` — vendored Cytoscape.js UMD build (new, downloaded)
- `src/Mjm.LocalDocs.Server/wwwroot/js/graph-interop.js` — Cytoscape interop module (new)
- `src/Mjm.LocalDocs.Server/Components/App.razor` — two `<script>` tags (modify)
- `src/Mjm.LocalDocs.Server/Components/Pages/Projects/ProjectGraph.razor` — the page (new)
- `src/Mjm.LocalDocs.Server/Components/Pages/Projects/ProjectDetail.razor` — conditional "Graph" entry button + graph cleanup on document/project delete (modify)
- `src/Mjm.LocalDocs.Server/Components/Pages/Projects/ProjectList.razor` — graph cleanup on project delete (modify)

**Configuration**
- `src/Mjm.LocalDocs.Server/appsettings.json` — add `LocalDocs:Graph` block with `Enabled: true` (modify)

**Deliberate v1 simplifications (surfaced, not hidden):**
- **Layout algorithm:** Cytoscape's built-in `cose` force-directed layout is used (organic look, ships with core → a single vendored file). The spec named `fcose`; that is a nicer variant but needs 3 extra vendored scripts (`layout-base`, `cose-base`, `cytoscape-fcose`) and extension registration. Deferred as a future enhancement; the interop is written so swapping the layout name is a one-line change.
- **"Open document" link:** there is **no** document-detail route in this app (documents are managed inline inside `ProjectDetail`). The drawer's source rows therefore link to the project page (`/projects/{ProjectId}`) and show the stored `Snippet` inline. A dedicated document viewer is out of scope.
- **Graph cleanup on delete** is wired at the **Blazor call sites** (`ProjectDetail`/`ProjectList`), not inside `DocumentService`/`IProjectRepository` (which would force constructor/DI/test churn across Core). The MCP `delete_document`/`delete_project` tools are **not** wired for graph cleanup in v1 (documented follow-up); the EF cascade still removes graph rows on project delete for SQLite/SqlServer regardless.
- **Analysis is synchronous in the Blazor circuit** (same as Chat). Large projects can take a while; mitigated by the backend's skip-already-analyzed + per-document incremental commit (a dropped circuit does not lose completed documents — just re-run).

---

## Task 0: Baseline — confirm the branch is green

**Files:** none (verification only).

Establishes the known-good starting point before any change.

- [ ] **Step 1: Restore + build the whole solution**

Run: `dotnet build`
Expected: Build succeeded, 0 errors (warnings ok).

- [ ] **Step 2: Run the full test suite**

Run: `dotnet test`
Expected: All tests pass. Note the passing test count (e.g. "Passed! - Failed: 0"). This is the regression baseline for later tasks.

- [ ] **Step 3: Confirm branch**

Run: `git status` and `git branch --show-current`
Expected: on `feature/graph`, working tree clean apart from this plan doc (untracked `.superpowers/` is fine).

No commit (nothing changed).

---

## Task 1: Add the `LocalDocs:Graph` configuration block

**Files:**
- Modify: `src/Mjm.LocalDocs.Server/appsettings.json`

**Interfaces:**
- Produces: a bound `Options.Value.Graph` with `Enabled = true` so the feature's entry points become visible. Property names must match `GraphOptions` exactly (`Enabled`, `NeighborhoodHops`, `MaxContextChunksPerDoc`, `ModelOverride`).

- [ ] **Step 1: Add the `Graph` block as a peer of `Chat`**

In `src/Mjm.LocalDocs.Server/appsettings.json`, the `LocalDocs` object ends with the `Chat` section. Find the closing of the `Chat` block:

```json
      "Ollama": {
        "Endpoint": "http://localhost:11434",
        "Model": "llama3"
      }
    }
  }
}
```

Replace it with (adds a `,` after `Chat`'s closing brace and a new `Graph` sibling):

```json
      "Ollama": {
        "Endpoint": "http://localhost:11434",
        "Model": "llama3"
      }
    },
    "Graph": {
      "Enabled": true,
      "NeighborhoodHops": 2,
      "MaxContextChunksPerDoc": 20,
      "ModelOverride": null
    }
  }
}
```

- [ ] **Step 2: Verify the JSON is valid and still builds**

Run: `dotnet build src/Mjm.LocalDocs.Server/Mjm.LocalDocs.Server.csproj`
Expected: Build succeeded (appsettings.json is copied to output; malformed JSON would still build but fail at runtime — so also do Step 3).

- [ ] **Step 3: Sanity-check the JSON parses**

Run: `pwsh -NoProfile -Command "Get-Content src/Mjm.LocalDocs.Server/appsettings.json -Raw | ConvertFrom-Json | Out-Null; 'ok'"`
Expected: prints `ok` (throws if the JSON is malformed).

- [ ] **Step 4: Commit**

```bash
git add src/Mjm.LocalDocs.Server/appsettings.json
git commit -m "Enable graph feature in appsettings"
```

---

## Task 2: Backend read model — `IGraphRepository.GetCurrentSourcesAsync`

**Files:**
- Modify: `src/Mjm.LocalDocs.Core/Abstractions/IGraphRepository.cs`
- Modify: `src/Mjm.LocalDocs.Infrastructure/Persistence/Repositories/EfCoreGraphRepository.cs`
- Modify: `src/Mjm.LocalDocs.Infrastructure/VectorStore/InMemoryGraphRepository.cs`
- Modify: `tests/Mjm.LocalDocs.Tests/Persistence/EfCoreGraphRepositoryTests.cs`
- Modify: `tests/Mjm.LocalDocs.Tests/VectorStore/InMemoryGraphRepositoryTests.cs`

**Interfaces:**
- Consumes: `GraphSource`, `GraphSourceTargetKind` (Piano 1).
- Produces: `Task<IReadOnlyList<GraphSource>> GetCurrentSourcesAsync(string projectId, CancellationToken cancellationToken = default)` — returns all `IsCurrent == true` sources for a project (both entity- and relationship-targeted). Consumed by the page (Task 6 badge, Task 7 payload `docIds`, Task 9 document filter).

Read-only aggregate query; TDD with one integration test per implementation.

- [ ] **Step 1: Add the failing EF integration test**

In `tests/Mjm.LocalDocs.Tests/Persistence/EfCoreGraphRepositoryTests.cs`, add this test method inside the `EfCoreGraphRepositoryTests` class (the class already defines the `NewEntity` / `NewSource` helpers and `CreateContext()` used here):

```csharp
    [Fact]
    public async Task GetCurrentSourcesAsync_ReturnsOnlyCurrentSourcesForProject()
    {
        // Arrange
        var repo = new EfCoreGraphRepository(CreateContext());
        await repo.AddSourcesAsync(
        [
            NewSource("s1", GraphSourceTargetKind.Entity, "e1", "doc-1", 1, isCurrent: true),
            NewSource("s2", GraphSourceTargetKind.Relationship, "r1", "doc-1", 1, isCurrent: true),
            NewSource("s3", GraphSourceTargetKind.Entity, "e2", "doc-0", 1, isCurrent: false)
        ]);

        // Act
        var current = await new EfCoreGraphRepository(CreateContext()).GetCurrentSourcesAsync("proj-1");

        // Assert
        Assert.Equal(2, current.Count);
        Assert.All(current, s => Assert.True(s.IsCurrent));
        Assert.Contains(current, s => s.TargetId == "e1");
        Assert.Contains(current, s => s.TargetId == "r1");
    }
```

- [ ] **Step 2: Add the failing in-memory test**

In `tests/Mjm.LocalDocs.Tests/VectorStore/InMemoryGraphRepositoryTests.cs`, add this test inside the `InMemoryGraphRepositoryTests` class (it already has `NewSource(string id, string targetId, string documentId, bool isCurrent)`):

```csharp
    [Fact]
    public async Task GetCurrentSourcesAsync_ReturnsOnlyCurrentSourcesForProject()
    {
        // Arrange
        var repo = new InMemoryGraphRepository();
        await repo.AddSourcesAsync(
        [
            NewSource("s1", "e1", "doc-1", isCurrent: true),
            NewSource("s2", "e2", "doc-1", isCurrent: false)
        ]);

        // Act
        var current = await repo.GetCurrentSourcesAsync("proj-1");

        // Assert
        var one = Assert.Single(current);
        Assert.Equal("e1", one.TargetId);
    }
```

- [ ] **Step 3: Run the tests to verify they fail**

Run: `dotnet test tests/Mjm.LocalDocs.Tests/Mjm.LocalDocs.Tests.csproj --filter "FullyQualifiedName~GetCurrentSourcesAsync"`
Expected: FAIL — `GetCurrentSourcesAsync` is not defined on `IGraphRepository` (compile error).

- [ ] **Step 4: Add the method to the interface**

In `src/Mjm.LocalDocs.Core/Abstractions/IGraphRepository.cs`, add this after the `GetSourcesForTargetAsync` declaration (before `HasCurrentSourcesForDocumentAsync`):

```csharp
    /// <summary>All current (IsCurrent) source rows for a project, entity- and relationship-targeted.</summary>
    Task<IReadOnlyList<GraphSource>> GetCurrentSourcesAsync(
        string projectId, CancellationToken cancellationToken = default);
```

- [ ] **Step 5: Implement in `EfCoreGraphRepository`**

In `src/Mjm.LocalDocs.Infrastructure/Persistence/Repositories/EfCoreGraphRepository.cs`, add this method right after `GetSourcesForTargetAsync` (the private `MapSource` mapper already exists in this file):

```csharp
    public async Task<IReadOnlyList<GraphSource>> GetCurrentSourcesAsync(
        string projectId, CancellationToken cancellationToken = default)
    {
        var rows = await _context.GraphSources.AsNoTracking()
            .Where(s => s.ProjectId == projectId && s.IsCurrent)
            .ToListAsync(cancellationToken);
        return rows.Select(MapSource).ToList();
    }
```

- [ ] **Step 6: Implement in `InMemoryGraphRepository`**

In `src/Mjm.LocalDocs.Infrastructure/VectorStore/InMemoryGraphRepository.cs`, add this method right after `GetSourcesForTargetAsync`:

```csharp
    public Task<IReadOnlyList<GraphSource>> GetCurrentSourcesAsync(
        string projectId, CancellationToken cancellationToken = default)
    {
        IReadOnlyList<GraphSource> result = _sources.Values
            .Where(s => s.ProjectId == projectId && s.IsCurrent)
            .ToList();
        return Task.FromResult(result);
    }
```

- [ ] **Step 7: Run the tests to verify they pass**

Run: `dotnet test tests/Mjm.LocalDocs.Tests/Mjm.LocalDocs.Tests.csproj --filter "FullyQualifiedName~GetCurrentSourcesAsync"`
Expected: PASS (2 tests).

- [ ] **Step 8: Run the full graph repo test classes to confirm no regression**

Run: `dotnet test tests/Mjm.LocalDocs.Tests/Mjm.LocalDocs.Tests.csproj --filter "FullyQualifiedName~GraphRepositoryTests"`
Expected: PASS (all EfCore + InMemory graph repo tests).

- [ ] **Step 9: Commit**

```bash
git add src/Mjm.LocalDocs.Core/Abstractions/IGraphRepository.cs src/Mjm.LocalDocs.Infrastructure/Persistence/Repositories/EfCoreGraphRepository.cs src/Mjm.LocalDocs.Infrastructure/VectorStore/InMemoryGraphRepository.cs tests/Mjm.LocalDocs.Tests/Persistence/EfCoreGraphRepositoryTests.cs tests/Mjm.LocalDocs.Tests/VectorStore/InMemoryGraphRepositoryTests.cs
git commit -m "Add GetCurrentSourcesAsync to graph repository"
```

---

## Task 3: Graph cleanup on document / project deletion (UI call sites)

**Files:**
- Modify: `src/Mjm.LocalDocs.Server/Components/Pages/Projects/ProjectDetail.razor`
- Modify: `src/Mjm.LocalDocs.Server/Components/Pages/Projects/ProjectList.razor`

**Interfaces:**
- Consumes: `IGraphRepository.MarkDocumentSourcesSupersededAsync`, `RetireTargetsWithoutCurrentSourcesAsync`, `DeleteByProjectAsync`; `Options.Value.Graph.Enabled`.
- Produces: nothing new. Fulfils the Piano 1 deferral: deleting a document retires its now-orphaned graph entities/relationships; deleting a project purges its graph rows for **all** providers (redundant-but-harmless for EF, load-bearing for InMemory).

Blazor-only edits; verified by build + running the app (no automated test — the repo does not unit-test `.razor`).

- [ ] **Step 1: Inject `IGraphRepository` into `ProjectDetail.razor`**

In `src/Mjm.LocalDocs.Server/Components/Pages/Projects/ProjectDetail.razor`, the `@inject` block ends with `@inject IOptions<LocalDocsOptions> LocalDocsOptions` (line ~21). Add directly below it:

```razor
@inject IGraphRepository GraphRepository
```

(`IGraphRepository` is in `Mjm.LocalDocs.Core.Abstractions`, already imported via the existing `@using Mjm.LocalDocs.Core.Abstractions` on line 7.)

- [ ] **Step 2: Retire graph rows after a document is deleted**

In `ProjectDetail.razor`, find the success branch inside `DeleteDocumentAsync` (the `if (deleted)` block):

```csharp
            var deleted = await DocumentService.DeleteDocumentAsync(document.Id);
            if (deleted)
            {
                _documents.Remove(document);
                Snackbar.Add($"Document \"{document.FileName}\" deleted successfully.", Severity.Success);
                StateHasChanged();
            }
```

Replace it with (adds graph cleanup guarded by the feature flag):

```csharp
            var deleted = await DocumentService.DeleteDocumentAsync(document.Id);
            if (deleted)
            {
                if (LocalDocsOptions.Value.Graph.Enabled)
                {
                    // The deleted document's graph sources have no FK cascade — mark them
                    // non-current and retire any entity/relationship left without a source.
                    await GraphRepository.MarkDocumentSourcesSupersededAsync(document.Id);
                    await GraphRepository.RetireTargetsWithoutCurrentSourcesAsync(document.ProjectId, DateTimeOffset.UtcNow);
                }

                _documents.Remove(document);
                Snackbar.Add($"Document \"{document.FileName}\" deleted successfully.", Severity.Success);
                StateHasChanged();
            }
```

- [ ] **Step 3: Purge graph rows before a project is deleted (ProjectDetail)**

In `ProjectDetail.razor`, find the success branch inside `DeleteProjectAsync`:

```csharp
            var deleted = await ProjectRepository.DeleteAsync(_project.Id);
            if (deleted)
```

Replace with (purge first so the InMemory provider — which has no cascade — is also cleaned):

```csharp
            if (LocalDocsOptions.Value.Graph.Enabled)
                await GraphRepository.DeleteByProjectAsync(_project.Id);

            var deleted = await ProjectRepository.DeleteAsync(_project.Id);
            if (deleted)
```

- [ ] **Step 4: Inject `IGraphRepository` into `ProjectList.razor` and purge on delete**

Open `src/Mjm.LocalDocs.Server/Components/Pages/Projects/ProjectList.razor`. Confirm it has `@using Mjm.LocalDocs.Core.Abstractions`, `@using Mjm.LocalDocs.Core.Configuration`, `@using Microsoft.Extensions.Options`, `@inject IProjectRepository ProjectRepository`, and `@inject IOptions<LocalDocsOptions> LocalDocsOptions` (add any that are missing to the existing `@using`/`@inject` block at the top). Then add:

```razor
@inject IGraphRepository GraphRepository
```

Find, inside `DeleteProjectAsync(Project project)`:

```csharp
            var deleted = await ProjectRepository.DeleteAsync(project.Id);
```

Replace with:

```csharp
            if (LocalDocsOptions.Value.Graph.Enabled)
                await GraphRepository.DeleteByProjectAsync(project.Id);

            var deleted = await ProjectRepository.DeleteAsync(project.Id);
```

- [ ] **Step 5: Build**

Run: `dotnet build src/Mjm.LocalDocs.Server/Mjm.LocalDocs.Server.csproj`
Expected: Build succeeded.

- [ ] **Step 6: Commit**

```bash
git add src/Mjm.LocalDocs.Server/Components/Pages/Projects/ProjectDetail.razor src/Mjm.LocalDocs.Server/Components/Pages/Projects/ProjectList.razor
git commit -m "Retire graph rows on document and project deletion"
```

---

## Task 4: Vendor Cytoscape.js + interop module + wire scripts in App.razor

**Files:**
- Create: `src/Mjm.LocalDocs.Server/wwwroot/lib/cytoscape.min.js` (downloaded, committed)
- Create: `src/Mjm.LocalDocs.Server/wwwroot/js/graph-interop.js`
- Modify: `src/Mjm.LocalDocs.Server/Components/App.razor`

**Interfaces:**
- Produces (JS globals, consumed by `ProjectGraph.razor`):
  - `initGraph(containerId: string, dataJson: string, dotNetRef): boolean` — build the graph; wires taps to `.NET` (`OnNodeSelected`/`OnEdgeSelected`/`OnSelectionCleared`); returns `false` if the container/library is missing.
  - `setGraphData(dataJson: string): void` — replace elements + re-run layout.
  - `applyGraphFilter(query: string, typesJson: string|null, documentId: string|null, hops: number): void` — "isolate the neighborhood".
  - `focusGraphNode(entityId: string): void` — center + select a node.
  - `destroyGraph(): void` — tear down.
- Payload JSON shape (produced by C# in Task 7): `{ "nodes": [ { "id", "name", "type", "color", "docIds": [] } ], "edges": [ { "id", "source", "target", "label", "docIds": [] } ] }`.
- .NET callbacks (implemented in Task 8): `[JSInvokable] Task OnNodeSelected(string id)`, `[JSInvokable] Task OnEdgeSelected(string id)`, `[JSInvokable] Task OnSelectionCleared()`.

- [ ] **Step 1: Create the `lib` folder and download Cytoscape.js (pinned)**

Run (PowerShell; creates the folder and fetches the UMD build once — this is the only network use):

```bash
pwsh -NoProfile -Command "New-Item -ItemType Directory -Force src/Mjm.LocalDocs.Server/wwwroot/lib | Out-Null; Invoke-WebRequest -Uri 'https://cdn.jsdelivr.net/npm/cytoscape@3.30.2/dist/cytoscape.min.js' -OutFile 'src/Mjm.LocalDocs.Server/wwwroot/lib/cytoscape.min.js'"
```

- [ ] **Step 2: Verify the download is a real, non-empty Cytoscape build**

Run:

```bash
pwsh -NoProfile -Command "$f='src/Mjm.LocalDocs.Server/wwwroot/lib/cytoscape.min.js'; $len=(Get-Item $f).Length; $hit=Select-String -Path $f -Pattern 'cytoscape' -Quiet; \"len=$len hit=$hit\""
```

Expected: `len` is > 300000 and `hit=True`. If `len` is small or `hit=False`, the fetch failed (e.g. a proxy error page) — delete the file and retry Step 1, or fetch a `@3` fallback: replace `cytoscape@3.30.2` with `cytoscape@3` in the URL. Do **not** proceed until this passes.

- [ ] **Step 3: Create the interop module `wwwroot/js/graph-interop.js`**

Create `src/Mjm.LocalDocs.Server/wwwroot/js/graph-interop.js` with exactly:

```javascript
// Cytoscape.js interop for the project knowledge-graph page.
// Node/edge colors are computed server-side (data(color)); layout is the built-in
// force-directed 'cose' (swap LAYOUT_NAME for 'fcose' after vendoring that extension).
(function () {
    "use strict";

    var cy = null;
    var dotNetRef = null;
    var LAYOUT_NAME = "cose";

    function isDark() {
        return document.documentElement.classList.contains("dark");
    }

    function toElements(data) {
        var els = [];
        var i;
        for (i = 0; i < data.nodes.length; i++) {
            var n = data.nodes[i];
            els.push({ group: "nodes", data: { id: n.id, name: n.name, type: n.type, color: n.color, docIds: n.docIds || [] } });
        }
        for (i = 0; i < data.edges.length; i++) {
            var e = data.edges[i];
            els.push({ group: "edges", data: { id: e.id, source: e.source, target: e.target, label: e.label, docIds: e.docIds || [] } });
        }
        return els;
    }

    function buildStyle() {
        var dark = isDark();
        var labelColor = dark ? "#e2e8f0" : "#334155";
        var edgeColor = dark ? "#64748b" : "#94a3b8";
        var edgeLabel = dark ? "#94a3b8" : "#64748b";
        var textBg = dark ? "#0f172a" : "#f8fafc";
        var nodeBorder = dark ? "#1e293b" : "#e2e8f0";
        return [
            { selector: "node", style: {
                "background-color": "data(color)",
                "label": "data(name)",
                "font-size": 10,
                "color": labelColor,
                "text-valign": "bottom",
                "text-halign": "center",
                "text-margin-y": 4,
                "width": 30, "height": 30,
                "border-width": 2,
                "border-color": nodeBorder
            }},
            { selector: "edge", style: {
                "width": 1.5,
                "line-color": edgeColor,
                "target-arrow-color": edgeColor,
                "target-arrow-shape": "triangle",
                "curve-style": "bezier",
                "label": "data(label)",
                "font-size": 8,
                "color": edgeLabel,
                "text-rotation": "autorotate",
                "text-background-color": textBg,
                "text-background-opacity": 0.75,
                "text-background-padding": 2
            }},
            { selector: ".faded", style: { "opacity": 0.12, "text-opacity": 0.05 } },
            { selector: ".highlight", style: { "border-color": "#f59e0b", "border-width": 4 } },
            { selector: "node:selected", style: { "border-color": "#3b82f6", "border-width": 4 } }
        ];
    }

    function layoutConfig() {
        return { name: LAYOUT_NAME, animate: false, padding: 30, nodeDimensionsIncludeLabels: true, idealEdgeLength: 120, nodeRepulsion: 8000, randomize: true };
    }

    window.initGraph = function (containerId, dataJson, ref) {
        var container = document.getElementById(containerId);
        if (!container || typeof cytoscape === "undefined") {
            return false;
        }
        dotNetRef = ref;
        var data = JSON.parse(dataJson);
        if (cy) { cy.destroy(); cy = null; }
        cy = cytoscape({
            container: container,
            elements: toElements(data),
            style: buildStyle(),
            layout: layoutConfig(),
            wheelSensitivity: 0.2
        });
        cy.on("tap", "node", function (evt) {
            if (dotNetRef) { dotNetRef.invokeMethodAsync("OnNodeSelected", evt.target.id()); }
        });
        cy.on("tap", "edge", function (evt) {
            if (dotNetRef) { dotNetRef.invokeMethodAsync("OnEdgeSelected", evt.target.id()); }
        });
        cy.on("tap", function (evt) {
            if (evt.target === cy && dotNetRef) { dotNetRef.invokeMethodAsync("OnSelectionCleared"); }
        });
        return true;
    };

    window.setGraphData = function (dataJson) {
        if (!cy) { return; }
        var data = JSON.parse(dataJson);
        cy.elements().remove();
        cy.add(toElements(data));
        cy.layout(layoutConfig()).run();
    };

    window.applyGraphFilter = function (query, typesJson, documentId, hops) {
        if (!cy) { return; }
        var activeTypes = typesJson ? JSON.parse(typesJson) : null; // array of visible type strings, null = all
        var q = (query || "").trim().toLowerCase();
        var hopCount = Math.max(0, hops || 0);

        cy.batch(function () {
            cy.elements().removeClass("faded").removeClass("highlight");

            function typeVisible(n) { return !activeTypes || activeTypes.indexOf(n.data("type")) >= 0; }
            function docVisible(el) {
                if (!documentId) { return true; }
                var ids = el.data("docIds") || [];
                return ids.indexOf(documentId) >= 0;
            }

            if (q === "") {
                // No search term: only apply the type/document visibility filters.
                cy.nodes().forEach(function (n) { if (!(typeVisible(n) && docVisible(n))) { n.addClass("faded"); } });
                cy.edges().forEach(function (e) {
                    if (e.source().hasClass("faded") || e.target().hasClass("faded") || !docVisible(e)) { e.addClass("faded"); }
                });
                return;
            }

            // Search: seeds = name-matching nodes (respecting type/doc), then expand N hops.
            var seeds = cy.nodes().filter(function (n) {
                return typeVisible(n) && docVisible(n) && n.data("name").toLowerCase().indexOf(q) >= 0;
            });
            var keep = seeds;
            for (var i = 0; i < hopCount; i++) {
                keep = keep.union(keep.connectedEdges().connectedNodes());
            }
            keep = keep.filter(function (n) { return typeVisible(n) && docVisible(n); });
            var keepIds = {};
            keep.forEach(function (n) { keepIds[n.id()] = true; });

            cy.nodes().forEach(function (n) { if (!keepIds[n.id()]) { n.addClass("faded"); } });
            cy.edges().forEach(function (e) {
                if (!keepIds[e.source().id()] || !keepIds[e.target().id()]) { e.addClass("faded"); }
            });
            seeds.addClass("highlight");
        });
    };

    window.focusGraphNode = function (id) {
        if (!cy) { return; }
        var n = cy.getElementById(id);
        if (n && n.length) {
            cy.animate({ center: { eles: n }, zoom: 1.4 }, { duration: 300 });
            cy.elements().unselect();
            n.select();
        }
    };

    window.destroyGraph = function () {
        if (cy) { cy.destroy(); cy = null; }
        dotNetRef = null;
    };
})();
```

- [ ] **Step 4: Reference both scripts in `App.razor`**

In `src/Mjm.LocalDocs.Server/Components/App.razor`, find (in `<body>`):

```html
    <script src="_content/MudBlazor/MudBlazor.min.js"></script>
    <script src="@Assets["_framework/blazor.web.js"]"></script>
```

Add the two graph scripts immediately after them:

```html
    <script src="_content/MudBlazor/MudBlazor.min.js"></script>
    <script src="@Assets["_framework/blazor.web.js"]"></script>
    <script src="lib/cytoscape.min.js"></script>
    <script src="js/graph-interop.js"></script>
```

- [ ] **Step 5: Build and confirm the assets are served**

Run: `dotnet build src/Mjm.LocalDocs.Server/Mjm.LocalDocs.Server.csproj`
Expected: Build succeeded.

Then (optional runtime check) run the app and confirm the files load with HTTP 200:

```bash
dotnet run --project src/Mjm.LocalDocs.Server/Mjm.LocalDocs.Server.csproj
```

In the browser dev console at `http://localhost:5024`, `typeof cytoscape` should be `"function"` and `typeof initGraph` should be `"function"`. Stop the app (Ctrl+C) when confirmed.

- [ ] **Step 6: Commit**

```bash
git add src/Mjm.LocalDocs.Server/wwwroot/lib/cytoscape.min.js src/Mjm.LocalDocs.Server/wwwroot/js/graph-interop.js src/Mjm.LocalDocs.Server/Components/App.razor
git commit -m "Bundle Cytoscape.js and graph interop module"
```

---

## Task 5: `ProjectGraph.razor` scaffold — route, gating, layout skeleton, entry button

**Files:**
- Create: `src/Mjm.LocalDocs.Server/Components/Pages/Projects/ProjectGraph.razor`
- Modify: `src/Mjm.LocalDocs.Server/Components/Pages/Projects/ProjectDetail.razor`

**Interfaces:**
- Produces: the page at route `/projects/{ProjectId}/graph`; the fields/regions (`_entities`, `_relationships`, rail/canvas/drawer divs, `@code` method anchors) that Tasks 6–9 fill. Establishes the two-part gate and Layout A skeleton (rail · canvas · drawer).

This task renders a working page: correct gating (redirect when disabled, message when unavailable), Layout A shell, and an empty-state prompting analysis. No Cytoscape yet.

- [ ] **Step 1: Create the page**

Create `src/Mjm.LocalDocs.Server/Components/Pages/Projects/ProjectGraph.razor` with:

```razor
@page "/projects/{ProjectId}/graph"
@attribute [Authorize]
@rendermode InteractiveServer
@using Mjm.LocalDocs.Core.Abstractions
@using Mjm.LocalDocs.Core.Configuration
@using Mjm.LocalDocs.Core.Models
@using Mjm.LocalDocs.Core.Services
@using Microsoft.Extensions.Options
@inject IProjectRepository ProjectRepository
@inject IGraphRepository GraphRepository
@inject IGraphExtractionService Extraction
@inject GraphBuildService GraphBuild
@inject DocumentService DocumentService
@inject IOptions<LocalDocsOptions> Options
@inject NavigationManager Navigation
@inject IJSRuntime JS
@inject ISnackbar Snackbar
@implements IAsyncDisposable

<PageTitle>@(_project?.Name ?? "Project") — Graph - Local Docs</PageTitle>

@if (_loading)
{
    <MudProgressCircular Color="Color.Primary" Indeterminate="true" Class="mx-auto d-block my-8" />
}
else if (_project is null)
{
    <MudPaper Elevation="0" Class="pa-8 d-flex flex-column align-center"
              Style="border: 1px dashed var(--ld-border); border-radius: var(--ld-radius-lg); text-align: center;">
        <MudIcon Icon="@Icons.Material.Rounded.SearchOff"
                 Style="font-size: 3rem; color: var(--ld-text-muted); margin-bottom: 12px;" />
        <MudText Style="font-size: 0.9375rem; font-weight: 500; color: var(--ld-text-primary); margin-bottom: 4px;">
            Project not found
        </MudText>
        <MudButton Variant="Variant.Outlined" Color="Color.Primary" Class="mt-4"
                   Style="border-radius: var(--ld-radius-pill);"
                   OnClick="@(() => Navigation.NavigateTo("/projects"))">
            Back to projects
        </MudButton>
    </MudPaper>
}
else
{
    <div id="graph-page-root" style="display: flex; flex-direction: column; flex: 1; min-height: 0;">

        <MudBreadcrumbs Items="_breadcrumbs" Class="mb-2 px-0" Style="flex-shrink: 0;" />

        <div class="d-flex justify-space-between align-center mb-2" style="flex-shrink: 0;">
            <div class="d-flex align-center gap-2">
                <MudIconButton Icon="@Icons.Material.Rounded.ArrowBack"
                               Size="Size.Small"
                               Href="@($"/projects/{ProjectId}")"
                               Title="Back to project" />
                <MudText Typo="Typo.h5" Class="ld-heading">@_project.Name — Graph</MudText>
            </div>
            @* Analyze toolbar is added in Task 6 (anchor: graph-toolbar-actions) *@
            <div class="d-flex align-center gap-2" id="graph-toolbar-actions"></div>
        </div>

        @if (!_available)
        {
            <MudAlert Severity="Severity.Warning" Variant="Variant.Outlined" Class="mb-2">
                Graph analysis is unavailable: no chat client is configured. Set <code>LocalDocs:Chat</code> to an
                OpenAI, Azure OpenAI, or Ollama provider (Anthropic and Fake are not supported for extraction).
            </MudAlert>
        }

        <div style="display: flex; flex: 1; min-height: 0; gap: 12px;">

            @* ---------- LEFT RAIL (filters) ---------- *@
            <MudPaper Elevation="0"
                      Style="width: 300px; flex-shrink: 0; border: 1px solid var(--ld-border); border-radius: var(--ld-radius-lg); padding: 16px; overflow-y: auto;">
                <p class="ld-section-title">FILTERS</p>
                @* Search / type toggles / document filter / hop slider are added in Task 9 (anchor: graph-rail-body) *@
                <div id="graph-rail-body">
                    <MudText Typo="Typo.body2" Style="color: var(--ld-text-muted);">
                        Filters appear once the graph has entities.
                    </MudText>
                </div>
            </MudPaper>

            @* ---------- CENTER CANVAS ---------- *@
            <div style="flex: 1; min-width: 0; position: relative; border: 1px solid var(--ld-border); border-radius: var(--ld-radius-lg); background: var(--mud-palette-surface); overflow: hidden;">
                @if (!_hasData)
                {
                    <div style="position: absolute; inset: 0; display: flex; flex-direction: column; align-items: center; justify-content: center; color: var(--ld-text-muted); text-align: center; padding: 24px;">
                        <MudIcon Icon="@Icons.Material.Rounded.Hub" Style="font-size: 3rem; opacity: 0.4; margin-bottom: 12px;" />
                        <MudText Typo="Typo.body1" Style="color: var(--ld-text-muted);">
                            No graph yet for this project.
                        </MudText>
                        <MudText Typo="Typo.body2" Style="color: var(--ld-text-muted); margin-top: 4px;">
                            Use "Analyze project" to extract entities and relationships from its documents.
                        </MudText>
                    </div>
                }
                <div id="graph-canvas" style="position: absolute; inset: 0;"></div>
                @* Progress overlay added in Task 6 (anchor: graph-progress-overlay) *@
            </div>

            @* ---------- RIGHT DRAWER (details + source documents) ---------- *@
            @* Drawer markup added in Task 8 (anchor: graph-drawer) *@
        </div>
    </div>
}

<style>
    .mud-main-content:has(#graph-page-root) {
        height: 100vh !important;
        min-height: 0 !important;
        overflow: hidden !important;
        display: flex !important;
        flex-direction: column !important;
        box-sizing: border-box !important;
    }

    .mud-main-content:has(#graph-page-root) > .mud-container {
        flex: 1 !important;
        display: flex !important;
        flex-direction: column !important;
        min-height: 0 !important;
        overflow: hidden !important;
        padding-top: 8px !important;
        padding-bottom: 8px !important;
    }
</style>

@code {
    [Parameter]
    public string ProjectId { get; set; } = string.Empty;

    private bool _loading = true;
    private bool _available;
    private bool _hasData;
    private Project? _project;
    private List<BreadcrumbItem> _breadcrumbs = [];

    private List<GraphEntity> _entities = [];
    private List<GraphRelationship> _relationships = [];

    protected override async Task OnInitializedAsync()
    {
        // Gate 1 — feature disabled by config: leave the page.
        if (!Options.Value.Graph.Enabled)
        {
            Navigation.NavigateTo($"/projects/{ProjectId}");
            return;
        }

        // Gate 2 — enabled but no chat client: stay and explain (rendered above).
        _available = Extraction.IsAvailable;

        _project = await ProjectRepository.GetByIdAsync(ProjectId);
        if (_project is not null)
        {
            _breadcrumbs =
            [
                new BreadcrumbItem("Projects", href: "/projects"),
                new BreadcrumbItem(_project.Name, href: $"/projects/{ProjectId}"),
                new BreadcrumbItem("Graph", href: null, disabled: true)
            ];
        }

        _loading = false;
    }

    // Graph loading + Cytoscape init are added in Task 7.
    // Selection callbacks + drawer are added in Task 8.
    // Filters are added in Task 9.
    // Analysis streaming is added in Task 6.

    public ValueTask DisposeAsync() => ValueTask.CompletedTask; // replaced in Task 7
}
```

- [ ] **Step 2: Add the conditional "Graph" entry button on `ProjectDetail.razor`**

In `src/Mjm.LocalDocs.Server/Components/Pages/Projects/ProjectDetail.razor`, find the Chat button block:

```razor
            @if (LocalDocsOptions.Value.Chat.Enabled)
            {
                <MudButton Variant="Variant.Outlined"
                           Color="Color.Primary"
                           StartIcon="@Icons.Material.Rounded.Chat"
                           Size="Size.Small"
                           Style="border-radius: var(--ld-radius-pill);"
                           Href="@($"/projects/{ProjectId}/chat")">
                    Chat
                </MudButton>
            }
```

Add a sibling Graph button immediately after it (same gating style, using `Graph.Enabled`):

```razor
            @if (LocalDocsOptions.Value.Graph.Enabled)
            {
                <MudButton Variant="Variant.Outlined"
                           Color="Color.Primary"
                           StartIcon="@Icons.Material.Rounded.Hub"
                           Size="Size.Small"
                           Style="border-radius: var(--ld-radius-pill);"
                           Href="@($"/projects/{ProjectId}/graph")">
                    Graph
                </MudButton>
            }
```

- [ ] **Step 3: Build**

Run: `dotnet build src/Mjm.LocalDocs.Server/Mjm.LocalDocs.Server.csproj`
Expected: Build succeeded.

- [ ] **Step 4: Run and verify gating + skeleton**

Run: `dotnet run --project src/Mjm.LocalDocs.Server/Mjm.LocalDocs.Server.csproj` (login `admin`/`admin`).
Verify:
1. On a project page, a **Graph** button appears next to Chat.
2. Clicking it opens `/projects/{id}/graph` showing the breadcrumb, "{Name} — Graph" header, the filters rail, and the centered "No graph yet" empty state.
3. Because the base config has `Chat.Enabled=true` + `Provider=OpenAI`, the amber "unavailable" alert should **not** appear. (If your environment has no OpenAI key, the alert will show — that is correct behavior.)
4. Temporarily set `LocalDocs:Graph:Enabled` to `false` in `appsettings.json`, restart, and confirm the page redirects to `/projects/{id}` and the button disappears. Revert to `true`.

Stop the app.

- [ ] **Step 5: Commit**

```bash
git add src/Mjm.LocalDocs.Server/Components/Pages/Projects/ProjectGraph.razor src/Mjm.LocalDocs.Server/Components/Pages/Projects/ProjectDetail.razor
git commit -m "Add graph page scaffold with gating and entry button"
```

---

## Task 6: Analyze project / document with streaming progress

**Files:**
- Modify: `src/Mjm.LocalDocs.Server/Components/Pages/Projects/ProjectGraph.razor`

**Interfaces:**
- Consumes: `GraphBuild.AnalyzeProjectAsync`, `GraphBuild.AnalyzeDocumentAsync`, all `GraphBuildEvent` subtypes; `DocumentService.GetDocumentsByProjectAsync`; `GraphRepository.GetCurrentSourcesAsync` (Task 2).
- Produces: the analyze toolbar + a live progress overlay; `_currentDocs`, `_analyzedDocIds`, `UnanalyzedCount`; a `ReloadAsync()` hook (implemented minimally here, extended in Task 7). Sets `_hasData` based on whether the project has entities.

Mirrors the `ProjectChat.razor` streaming pattern (`await foreach` + `InvokeAsync(StateHasChanged)` + a `CancellationTokenSource`).

- [ ] **Step 1: Fill the toolbar (`graph-toolbar-actions`)**

Replace this line in `ProjectGraph.razor`:

```razor
            @* Analyze toolbar is added in Task 6 (anchor: graph-toolbar-actions) *@
            <div class="d-flex align-center gap-2" id="graph-toolbar-actions"></div>
```

with:

```razor
            <div class="d-flex align-center gap-2" id="graph-toolbar-actions">
                @if (UnanalyzedCount > 0)
                {
                    <MudChip T="string" Size="Size.Small" Variant="Variant.Outlined" Color="Color.Warning"
                             Icon="@Icons.Material.Rounded.PendingActions"
                             Style="border-radius: var(--ld-radius-pill); font-size: 0.75rem;">
                        @UnanalyzedCount not analyzed
                    </MudChip>
                }
                <MudTooltip Text="Re-extract even already-analyzed documents">
                    <MudSwitch @bind-Value="_forceReanalyze" Label="Force" Color="Color.Secondary" Size="Size.Small"
                               Disabled="@(_analyzing || !_available)" />
                </MudTooltip>
                @if (_currentDocs.Count > 0)
                {
                    <MudMenu Label="Analyze document" Variant="Variant.Outlined" Color="Color.Primary" Size="Size.Small"
                             EndIcon="@Icons.Material.Rounded.ArrowDropDown" Dense="true"
                             Style="border-radius: var(--ld-radius-pill);"
                             Disabled="@(_analyzing || !_available)">
                        @foreach (var doc in _currentDocs)
                        {
                            <MudMenuItem OnClick="@(() => AnalyzeDocumentAsync(doc.Id))">@doc.FileName</MudMenuItem>
                        }
                    </MudMenu>
                }
                @if (_analyzing)
                {
                    <MudButton Variant="Variant.Filled" Color="Color.Error" Size="Size.Small"
                               StartIcon="@Icons.Material.Rounded.Stop"
                               Style="border-radius: var(--ld-radius-pill);"
                               OnClick="CancelAnalysis">
                        Stop
                    </MudButton>
                }
                else
                {
                    <MudButton Variant="Variant.Filled" Color="Color.Primary" Size="Size.Small"
                               StartIcon="@Icons.Material.Rounded.AutoAwesome"
                               Style="border-radius: var(--ld-radius-pill);"
                               OnClick="AnalyzeProjectAsync"
                               Disabled="@(!_available)">
                        Analyze project
                    </MudButton>
                }
            </div>
```

- [ ] **Step 2: Add the progress overlay on the canvas (`graph-progress-overlay`)**

Replace this line:

```razor
                @* Progress overlay added in Task 6 (anchor: graph-progress-overlay) *@
```

with:

```razor
                @if (_analyzing || _progressLog.Count > 0)
                {
                    <div style="position: absolute; right: 12px; bottom: 12px; width: 340px; max-height: 45%; display: flex; flex-direction: column; background: var(--mud-palette-surface); border: 1px solid var(--ld-border); border-radius: var(--ld-radius-md); box-shadow: 0 4px 16px rgba(0,0,0,0.15); overflow: hidden;">
                        <div class="d-flex justify-space-between align-center px-3 py-2" style="border-bottom: 1px solid var(--ld-border);">
                            <div class="d-flex align-center gap-2">
                                @if (_analyzing)
                                {
                                    <MudProgressCircular Size="Size.Small" Indeterminate="true" Color="Color.Primary" />
                                }
                                <MudText Typo="Typo.body2" Style="font-weight: 600;">Analysis</MudText>
                            </div>
                            @if (!_analyzing)
                            {
                                <MudIconButton Icon="@Icons.Material.Rounded.Close" Size="Size.Small"
                                               OnClick="@(() => _progressLog.Clear())" Title="Dismiss" />
                            }
                        </div>
                        <div style="overflow-y: auto; padding: 8px 12px; font-size: 0.78rem; line-height: 1.5;">
                            @foreach (var line in _progressLog)
                            {
                                <div style="color: var(--ld-text-secondary); word-break: break-word;">@line</div>
                            }
                        </div>
                    </div>
                }
```

- [ ] **Step 3: Add analysis state, doc list, and methods to `@code`**

In `ProjectGraph.razor`, replace the comment line:

```csharp
    // Graph loading + Cytoscape init are added in Task 7.
```

with the analysis fields + methods (the `ReloadAsync` here loads entities/relationships/sources and recomputes the badge; Task 7 extends it to also refresh the canvas):

```csharp
    private bool _analyzing;
    private bool _forceReanalyze;
    private readonly List<string> _progressLog = [];
    private CancellationTokenSource? _analyzeCts;

    private List<Document> _currentDocs = [];
    private HashSet<string> _analyzedDocIds = [];
    private int UnanalyzedCount => _currentDocs.Count(d => !_analyzedDocIds.Contains(d.Id));

    /// <summary>Reload entities/relationships/current-sources and recompute derived state.</summary>
    private async Task ReloadAsync()
    {
        _entities = (await GraphRepository.GetEntitiesAsync(ProjectId)).ToList();
        _relationships = (await GraphRepository.GetRelationshipsAsync(ProjectId)).ToList();
        _currentSources = (await GraphRepository.GetCurrentSourcesAsync(ProjectId)).ToList();
        _currentDocs = (await DocumentService.GetDocumentsByProjectAsync(ProjectId, includeSuperseded: false)).ToList();
        _analyzedDocIds = _currentSources.Select(s => s.DocumentId).ToHashSet();
        _hasData = _entities.Count > 0;
    }

    private Task AnalyzeProjectAsync() => RunAnalysisAsync(GraphBuild.AnalyzeProjectAsync(ProjectId, _forceReanalyze, _analyzeCtsToken()));

    private Task AnalyzeDocumentAsync(string documentId) => RunAnalysisAsync(GraphBuild.AnalyzeDocumentAsync(documentId, _forceReanalyze, _analyzeCtsToken()));

    private CancellationToken _analyzeCtsToken()
    {
        _analyzeCts = new CancellationTokenSource();
        return _analyzeCts.Token;
    }

    private async Task RunAnalysisAsync(IAsyncEnumerable<GraphBuildEvent> stream)
    {
        if (_analyzing || !_available)
            return;

        _analyzing = true;
        _progressLog.Clear();
        await InvokeAsync(StateHasChanged);

        try
        {
            await foreach (var evt in stream)
            {
                _progressLog.Add(Describe(evt));
                await InvokeAsync(StateHasChanged);
            }
        }
        catch (OperationCanceledException)
        {
            _progressLog.Add("— cancelled —");
        }
        catch (Exception ex)
        {
            _progressLog.Add($"Error: {ex.Message}");
        }
        finally
        {
            _analyzing = false;
            _analyzeCts?.Dispose();
            _analyzeCts = null;
            await ReloadAsync();
            await InvokeAsync(StateHasChanged);
        }
    }

    private void CancelAnalysis() => _analyzeCts?.Cancel();

    private static string Describe(GraphBuildEvent evt) => evt switch
    {
        DocumentStartedEvent e   => $"[{e.Index}/{e.Total}] {e.FileName} — analyzing…",
        DocumentProgressEvent e  => $"    {e.Message}",
        DocumentCompletedEvent e => $"    ✓ {e.Entities} entities, {e.Relationships} relationships",
        DocumentSkippedEvent e   => $"    ↷ skipped ({e.Reason})",
        BuildErrorEvent e        => $"    ✗ {e.Message}",
        BuildDoneEvent e         => $"Done — {e.DocumentsProcessed} docs, {e.EntitiesTotal} entities, {e.RelationshipsTotal} relationships.",
        _                        => evt.ToString() ?? string.Empty
    };
```

- [ ] **Step 4: Add the `_currentSources` field and load data on init**

In `ProjectGraph.razor`, find the field declarations:

```csharp
    private List<GraphEntity> _entities = [];
    private List<GraphRelationship> _relationships = [];
```

Replace with (add `_currentSources`):

```csharp
    private List<GraphEntity> _entities = [];
    private List<GraphRelationship> _relationships = [];
    private List<GraphSource> _currentSources = [];
```

Then, in `OnInitializedAsync`, find:

```csharp
            _breadcrumbs =
            [
                new BreadcrumbItem("Projects", href: "/projects"),
                new BreadcrumbItem(_project.Name, href: $"/projects/{ProjectId}"),
                new BreadcrumbItem("Graph", href: null, disabled: true)
            ];
        }

        _loading = false;
```

Replace with (load the graph before clearing the loading flag):

```csharp
            _breadcrumbs =
            [
                new BreadcrumbItem("Projects", href: "/projects"),
                new BreadcrumbItem(_project.Name, href: $"/projects/{ProjectId}"),
                new BreadcrumbItem("Graph", href: null, disabled: true)
            ];

            await ReloadAsync();
        }

        _loading = false;
```

- [ ] **Step 5: Build**

Run: `dotnet build src/Mjm.LocalDocs.Server/Mjm.LocalDocs.Server.csproj`
Expected: Build succeeded.

- [ ] **Step 6: Run and verify streaming**

Run the app. On a project that has documents and with a working chat client:
1. The toolbar shows "Analyze project", a "Force" switch, and (if the project has current documents) an "Analyze document" menu; an amber "N not analyzed" chip appears when applicable.
2. Click "Analyze project" — the progress overlay streams `[i/N] file — analyzing…`, per-doc `✓`/`↷`/`✗` lines, and a final "Done — …" summary. During the run the button becomes a red "Stop".
3. If no chat client is configured, the "Analyze project" button is disabled and the amber unavailable alert shows (the stream would emit `BuildErrorEvent` lines otherwise). Verify "Stop" cancels a run.
4. After completion, the "not analyzed" chip count drops. (The canvas still shows the empty state until Task 7 — that is expected.)

Stop the app.

- [ ] **Step 7: Commit**

```bash
git add src/Mjm.LocalDocs.Server/Components/Pages/Projects/ProjectGraph.razor
git commit -m "Add streaming project and document graph analysis"
```

---

## Task 7: Render the graph with Cytoscape + type legend

**Files:**
- Modify: `src/Mjm.LocalDocs.Server/Components/Pages/Projects/ProjectGraph.razor`

**Interfaces:**
- Consumes: `initGraph`/`setGraphData`/`destroyGraph` (Task 4); `_entities`, `_relationships`, `_currentSources` (Tasks 5–6).
- Produces: `BuildPayloadJson()`, the stable type→color palette, `_types` (type/count/color for the legend, consumed by Task 9), `RenderGraphAsync()`; a real `DisposeAsync`. The canvas now renders after load and after each analysis.

- [ ] **Step 1: Add payload DTOs, palette, and rendering to `@code`**

In `ProjectGraph.razor`, replace the comment:

```csharp
    // Selection callbacks + drawer are added in Task 8.
```

with:

```csharp
    private bool _graphInitialized;
    private DotNetObjectReference<ProjectGraph>? _selfRef;

    // Stable, theme-neutral palette; a normalized type always maps to the same swatch.
    private static readonly string[] Palette =
    [
        "#4f83cc", "#e57373", "#81c784", "#ffb74d", "#ba68c8", "#4db6ac", "#f06292", "#a1887f",
        "#7986cb", "#9ccc65", "#ff8a65", "#4fc3f7", "#dce775", "#f48fb1", "#90a4ae", "#aed581"
    ];

    private static string ColorForType(string normalizedType)
    {
        var hash = 0;
        foreach (var ch in normalizedType)
            hash = unchecked(hash * 31 + ch) & 0x7fffffff;
        return Palette[hash % Palette.Length];
    }

    private sealed record GraphTypeInfo(string Type, string NormalizedType, int Count, string Color);

    private List<GraphTypeInfo> _types = [];

    private void RecomputeTypes() =>
        _types = _entities
            .GroupBy(e => e.NormalizedType)
            .Select(g => new GraphTypeInfo(g.First().Type, g.Key, g.Count(), ColorForType(g.Key)))
            .OrderByDescending(t => t.Count)
            .ToList();

    private string BuildPayloadJson()
    {
        // targetId -> distinct current document ids (drives the per-document filter)
        var docIdsByTarget = _currentSources
            .GroupBy(s => s.TargetId)
            .ToDictionary(g => g.Key, g => g.Select(s => s.DocumentId).Distinct().ToArray());

        var nodes = _entities.Select(e => new
        {
            id = e.Id,
            name = e.Name,
            type = e.Type,
            color = ColorForType(e.NormalizedType),
            docIds = docIdsByTarget.GetValueOrDefault(e.Id, [])
        });

        var edges = _relationships.Select(r => new
        {
            id = r.Id,
            source = r.SourceEntityId,
            target = r.TargetEntityId,
            label = r.Label,
            docIds = docIdsByTarget.GetValueOrDefault(r.Id, [])
        });

        return System.Text.Json.JsonSerializer.Serialize(new { nodes, edges });
    }

    /// <summary>Push current data into Cytoscape (init on first data, else replace).</summary>
    private async Task RenderGraphAsync()
    {
        if (!_hasData)
            return;
        var json = BuildPayloadJson();
        try
        {
            if (!_graphInitialized)
            {
                _selfRef ??= DotNetObjectReference.Create(this);
                var ok = await JS.InvokeAsync<bool>("initGraph", "graph-canvas", json, _selfRef);
                _graphInitialized = ok;
            }
            else
            {
                await JS.InvokeVoidAsync("setGraphData", json);
            }
        }
        catch
        {
            // interop can fail during prerender / circuit teardown — ignore
        }
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        // Render whenever there is data but the canvas has not been initialized yet
        // (covers first load and the first analysis that produces data).
        if (_hasData && !_graphInitialized)
            await RenderGraphAsync();
    }
```

- [ ] **Step 2: Recompute types + refresh canvas inside `ReloadAsync`**

In `ReloadAsync` (added in Task 6), find:

```csharp
        _analyzedDocIds = _currentSources.Select(s => s.DocumentId).ToHashSet();
        _hasData = _entities.Count > 0;
    }
```

Replace with:

```csharp
        _analyzedDocIds = _currentSources.Select(s => s.DocumentId).ToHashSet();
        _hasData = _entities.Count > 0;
        RecomputeTypes();
        if (_graphInitialized)
            await RenderGraphAsync();
    }
```

- [ ] **Step 3: Replace `DisposeAsync` with a real teardown**

Find:

```csharp
    public ValueTask DisposeAsync() => ValueTask.CompletedTask; // replaced in Task 7
```

Replace with:

```csharp
    public async ValueTask DisposeAsync()
    {
        _analyzeCts?.Cancel();
        try
        {
            if (_graphInitialized)
                await JS.InvokeVoidAsync("destroyGraph");
        }
        catch
        {
            // circuit may already be gone
        }
        _selfRef?.Dispose();
    }
```

- [ ] **Step 4: Add a type legend to the canvas**

In the canvas `<div>`, find:

```razor
                <div id="graph-canvas" style="position: absolute; inset: 0;"></div>
```

Replace with (adds a legend chip strip; hidden when empty):

```razor
                <div id="graph-canvas" style="position: absolute; inset: 0;"></div>
                @if (_hasData && _types.Count > 0)
                {
                    <div style="position: absolute; left: 12px; top: 12px; display: flex; flex-wrap: wrap; gap: 6px; max-width: 60%;">
                        @foreach (var t in _types)
                        {
                            <span style="display: inline-flex; align-items: center; gap: 5px; background: var(--mud-palette-surface); border: 1px solid var(--ld-border); border-radius: var(--ld-radius-pill); padding: 2px 8px; font-size: 0.72rem; color: var(--ld-text-secondary);">
                                <span style="width: 10px; height: 10px; border-radius: 50%; background: @t.Color; display: inline-block;"></span>
                                @t.Type (@t.Count)
                            </span>
                        }
                    </div>
                }
```

- [ ] **Step 5: Build**

Run: `dotnet build src/Mjm.LocalDocs.Server/Mjm.LocalDocs.Server.csproj`
Expected: Build succeeded.

- [ ] **Step 6: Run and verify rendering**

Run the app on a project that already has a graph (analyze first if needed):
1. Nodes render on the canvas with colored circles and name labels; edges show direction arrows + labels; the layout is organic (`cose`).
2. The type legend (top-left) lists each type with its color swatch and count; swatch colors match node colors.
3. Run "Analyze project" again — after it completes the canvas refreshes with any new nodes/edges (no full page reload).
4. Toggle dark/light theme and reload the page — labels/edges remain legible in both.

Stop the app.

- [ ] **Step 7: Commit**

```bash
git add src/Mjm.LocalDocs.Server/Components/Pages/Projects/ProjectGraph.razor
git commit -m "Render project graph with Cytoscape and type legend"
```

---

## Task 8: Selection drawer with entity/relationship details + source documents

**Files:**
- Modify: `src/Mjm.LocalDocs.Server/Components/Pages/Projects/ProjectGraph.razor`

**Interfaces:**
- Consumes: `GraphRepository.GetEntityAsync`, `GetRelationshipAsync`, `GetSourcesForTargetAsync`; `DocumentService.GetDocumentAsync`; the JS callbacks declared in Task 4.
- Produces: `[JSInvokable] OnNodeSelected/OnEdgeSelected/OnSelectionCleared`, the right-hand drawer rendering the selected node/edge and its **current** source documents (name, version, snippet, "open project" link).

- [ ] **Step 1: Add the drawer markup (`graph-drawer`)**

In `ProjectGraph.razor`, find:

```razor
            @* ---------- RIGHT DRAWER (details + source documents) ---------- *@
            @* Drawer markup added in Task 8 (anchor: graph-drawer) *@
```

Replace with:

```razor
            @* ---------- RIGHT DRAWER (details + source documents) ---------- *@
            @if (_selection is not null)
            {
                <MudPaper Elevation="0"
                          Style="width: 360px; flex-shrink: 0; border: 1px solid var(--ld-border); border-radius: var(--ld-radius-lg); padding: 16px; overflow-y: auto;">
                    <div class="d-flex justify-space-between align-center mb-2">
                        <MudChip T="string" Size="Size.Small" Variant="Variant.Filled"
                                 Style="@($"background: {_selection.AccentColor}; color: #fff; border-radius: var(--ld-radius-pill);")">
                            @_selection.Kind
                        </MudChip>
                        <MudIconButton Icon="@Icons.Material.Rounded.Close" Size="Size.Small"
                                       OnClick="ClearSelection" Title="Close" />
                    </div>

                    <MudText Typo="Typo.h6" Style="word-break: break-word;">@_selection.Title</MudText>
                    @if (!string.IsNullOrWhiteSpace(_selection.Subtitle))
                    {
                        <MudText Typo="Typo.body2" Style="color: var(--ld-text-secondary); margin-bottom: 4px;">@_selection.Subtitle</MudText>
                    }
                    @if (!string.IsNullOrWhiteSpace(_selection.Description))
                    {
                        <MudText Typo="Typo.body2" Style="color: var(--ld-text-secondary); margin-top: 8px;">@_selection.Description</MudText>
                    }
                    @if (_selection.Aliases.Count > 0)
                    {
                        <div class="d-flex flex-wrap gap-1 mt-2">
                            @foreach (var alias in _selection.Aliases)
                            {
                                <MudChip T="string" Size="Size.Small" Variant="Variant.Outlined"
                                         Style="border-radius: var(--ld-radius-pill); font-size: 0.72rem;">@alias</MudChip>
                            }
                        </div>
                    }

                    <MudDivider Class="my-3" />
                    <p class="ld-section-title" style="margin-bottom: 8px;">SOURCE DOCUMENTS (@_selection.Sources.Count)</p>

                    @if (_selection.Sources.Count == 0)
                    {
                        <MudText Typo="Typo.body2" Style="color: var(--ld-text-muted);">No current source documents.</MudText>
                    }
                    else
                    {
                        @foreach (var src in _selection.Sources)
                        {
                            <div style="border: 1px solid var(--ld-border); border-radius: var(--ld-radius-md); padding: 10px 12px; margin-bottom: 8px;">
                                <div class="d-flex justify-space-between align-center">
                                    <MudText Typo="Typo.body2" Style="font-weight: 600; word-break: break-word;">
                                        @src.FileName
                                    </MudText>
                                    <MudChip T="string" Size="Size.Small" Variant="Variant.Outlined"
                                             Style="border-radius: var(--ld-radius-pill); font-size: 0.7rem;">v@(src.VersionNumber)</MudChip>
                                </div>
                                @if (!string.IsNullOrWhiteSpace(src.Snippet))
                                {
                                    <MudText Typo="Typo.caption" Style="color: var(--ld-text-muted); font-style: italic; display: block; margin-top: 4px; word-break: break-word;">
                                        "@src.Snippet"
                                    </MudText>
                                }
                                @if (src.Exists)
                                {
                                    <MudButton Variant="Variant.Text" Color="Color.Primary" Size="Size.Small"
                                               StartIcon="@Icons.Material.Rounded.OpenInNew" Class="mt-1"
                                               OnClick="@(() => Navigation.NavigateTo($"/projects/{ProjectId}"))">
                                        Open in project
                                    </MudButton>
                                }
                                else
                                {
                                    <MudText Typo="Typo.caption" Style="color: var(--ld-text-muted); display: block; margin-top: 4px;">
                                        (document deleted)
                                    </MudText>
                                }
                            </div>
                        }
                    }
                </MudPaper>
            }
```

- [ ] **Step 2: Add selection state + callbacks to `@code`**

In `ProjectGraph.razor`, replace the comment:

```csharp
    // Filters are added in Task 9.
```

with:

```csharp
    private SelectionView? _selection;

    private sealed record SourceView(string FileName, int VersionNumber, string? Snippet, bool Exists);

    private sealed record SelectionView(
        string Kind, string Title, string? Subtitle, string? Description,
        string AccentColor, IReadOnlyList<string> Aliases, IReadOnlyList<SourceView> Sources);

    [JSInvokable]
    public async Task OnNodeSelected(string id)
    {
        var entity = await GraphRepository.GetEntityAsync(id);
        if (entity is null)
            return;
        var sources = await LoadSourceViewsAsync(GraphSourceTargetKind.Entity, id);
        _selection = new SelectionView(
            Kind: "Entity",
            Title: entity.Name,
            Subtitle: entity.Type,
            Description: entity.Description,
            AccentColor: ColorForType(entity.NormalizedType),
            Aliases: entity.Aliases,
            Sources: sources);
        await InvokeAsync(StateHasChanged);
    }

    [JSInvokable]
    public async Task OnEdgeSelected(string id)
    {
        var rel = await GraphRepository.GetRelationshipAsync(id);
        if (rel is null)
            return;
        var source = await GraphRepository.GetEntityAsync(rel.SourceEntityId);
        var target = await GraphRepository.GetEntityAsync(rel.TargetEntityId);
        var sources = await LoadSourceViewsAsync(GraphSourceTargetKind.Relationship, id);
        _selection = new SelectionView(
            Kind: "Relationship",
            Title: rel.Label,
            Subtitle: $"{source?.Name ?? "?"}  →  {target?.Name ?? "?"}",
            Description: rel.Description,
            AccentColor: "#64748b",
            Aliases: [],
            Sources: sources);
        await InvokeAsync(StateHasChanged);
    }

    [JSInvokable]
    public Task OnSelectionCleared()
    {
        _selection = null;
        return InvokeAsync(StateHasChanged);
    }

    private void ClearSelection() => _selection = null;

    private async Task<IReadOnlyList<SourceView>> LoadSourceViewsAsync(GraphSourceTargetKind kind, string targetId)
    {
        var sources = await GraphRepository.GetSourcesForTargetAsync(kind, targetId, currentOnly: true);
        var views = new List<SourceView>();
        foreach (var s in sources)
        {
            var doc = await DocumentService.GetDocumentAsync(s.DocumentId);
            views.Add(new SourceView(
                FileName: doc?.FileName ?? "(unknown)",
                VersionNumber: s.DocumentVersionNumber,
                Snippet: s.Snippet,
                Exists: doc is not null));
        }
        return views;
    }
```

- [ ] **Step 3: Build**

Run: `dotnet build src/Mjm.LocalDocs.Server/Mjm.LocalDocs.Server.csproj`
Expected: Build succeeded.

- [ ] **Step 4: Run and verify the drawer**

Run the app on a project with a rendered graph:
1. Click a node — the right drawer opens with the "Entity" chip (accent color = node color), name, type, description (if any), aliases (if any), and a "SOURCE DOCUMENTS (n)" list with file name, `v{version}`, the italic snippet, and an "Open in project" link.
2. Click an edge — drawer shows "Relationship", the label, `source → target`, and the relationship's source documents.
3. Click empty canvas space — drawer closes. The `×` button also closes it.
4. If a source document was deleted, its row shows "(document deleted)".

Stop the app.

- [ ] **Step 5: Commit**

```bash
git add src/Mjm.LocalDocs.Server/Components/Pages/Projects/ProjectGraph.razor
git commit -m "Add graph selection drawer with source documents"
```

---

## Task 9: Rail filters — search (isolate neighborhood), type toggles, document filter

**Files:**
- Modify: `src/Mjm.LocalDocs.Server/Components/Pages/Projects/ProjectGraph.razor`

**Interfaces:**
- Consumes: `applyGraphFilter`/`focusGraphNode` (Task 4); `_types` (Task 7); `_currentDocs` (Task 6); `Options.Value.Graph.NeighborhoodHops`.
- Produces: the rail controls (search box, hop slider, per-type toggle chips, document dropdown) and `ApplyFilterAsync()` wiring them to the canvas. Completes Layout A.

- [ ] **Step 1: Add filter state to `@code`**

In `ProjectGraph.razor`, add these fields next to the other private fields (e.g. right after `private List<GraphTypeInfo> _types = [];`):

```csharp
    private string _search = string.Empty;
    private int _hops = 2;
    private string? _documentFilter;
    private HashSet<string> _activeTypes = []; // normalized types currently visible; empty = all
```

Set the default hop depth from config. In `OnInitializedAsync`, find:

```csharp
        // Gate 2 — enabled but no chat client: stay and explain (rendered above).
        _available = Extraction.IsAvailable;
```

Replace with:

```csharp
        // Gate 2 — enabled but no chat client: stay and explain (rendered above).
        _available = Extraction.IsAvailable;
        _hops = Options.Value.Graph.NeighborhoodHops;
```

- [ ] **Step 2: Add the filter methods to `@code`**

Add these methods to the `@code` block (e.g. after `RecomputeTypes`):

```csharp
    private bool IsTypeActive(string normalizedType) =>
        _activeTypes.Count == 0 || _activeTypes.Contains(normalizedType);

    private async Task ToggleTypeAsync(string normalizedType)
    {
        // Empty set means "all". First toggle-off materializes the full set minus the clicked one.
        if (_activeTypes.Count == 0)
            _activeTypes = _types.Select(t => t.NormalizedType).ToHashSet();

        if (!_activeTypes.Remove(normalizedType))
            _activeTypes.Add(normalizedType);

        if (_activeTypes.Count == _types.Count)
            _activeTypes.Clear(); // back to "all"

        await ApplyFilterAsync();
    }

    private async Task ApplyFilterAsync()
    {
        if (!_graphInitialized)
            return;
        // null typesJson => all types visible
        string? typesJson = _activeTypes.Count == 0
            ? null
            : System.Text.Json.JsonSerializer.Serialize(
                _types.Where(t => _activeTypes.Contains(t.NormalizedType)).Select(t => t.Type).ToArray());
        try
        {
            await JS.InvokeVoidAsync("applyGraphFilter", _search, typesJson, _documentFilter, _hops);
        }
        catch
        {
            // ignore interop failures
        }
    }

    private async Task OnSearchChanged(string value)
    {
        _search = value;
        await ApplyFilterAsync();
    }

    private async Task OnHopsChanged(int value)
    {
        _hops = value;
        await ApplyFilterAsync();
    }

    private async Task OnDocumentFilterChanged(string? value)
    {
        _documentFilter = value;
        await ApplyFilterAsync();
    }
```

- [ ] **Step 3: Replace the rail body (`graph-rail-body`) with the real controls**

Find:

```razor
                <div id="graph-rail-body">
                    <MudText Typo="Typo.body2" Style="color: var(--ld-text-muted);">
                        Filters appear once the graph has entities.
                    </MudText>
                </div>
```

Replace with:

```razor
                <div id="graph-rail-body">
                    @if (!_hasData)
                    {
                        <MudText Typo="Typo.body2" Style="color: var(--ld-text-muted);">
                            Filters appear once the graph has entities.
                        </MudText>
                    }
                    else
                    {
                        <MudTextField T="string" Value="_search" ValueChanged="OnSearchChanged"
                                      Placeholder="Search entities…"
                                      Adornment="Adornment.Start" AdornmentIcon="@Icons.Material.Rounded.Search"
                                      Immediate="true" DebounceInterval="250"
                                      Variant="Variant.Outlined" Margin="Margin.Dense" Clearable="true"
                                      Class="mb-1" />
                        <MudText Typo="Typo.caption" Style="color: var(--ld-text-muted);">
                            Matches are highlighted; their neighborhood within the hop depth stays visible, the rest fades.
                        </MudText>

                        <div class="mt-4">
                            <MudText Typo="Typo.body2" Style="font-weight: 600; margin-bottom: 4px;">Neighborhood depth: @_hops</MudText>
                            <MudSlider T="int" Value="_hops" ValueChanged="OnHopsChanged" Min="0" Max="4" Step="1" Color="Color.Primary" />
                        </div>

                        <div class="mt-4">
                            <MudText Typo="Typo.body2" Style="font-weight: 600; margin-bottom: 6px;">Types</MudText>
                            <div class="d-flex flex-wrap gap-1">
                                @foreach (var t in _types)
                                {
                                    <div @onclick="@(() => ToggleTypeAsync(t.NormalizedType))" style="cursor: pointer;">
                                        <MudChip T="string" Size="Size.Small"
                                                 Variant="@(IsTypeActive(t.NormalizedType) ? Variant.Filled : Variant.Outlined)"
                                                 Style="@($"border-radius: var(--ld-radius-pill); font-size: 0.72rem; {(IsTypeActive(t.NormalizedType) ? $"background: {t.Color}; color: #fff;" : "")}")">
                                            @t.Type (@t.Count)
                                        </MudChip>
                                    </div>
                                }
                            </div>
                        </div>

                        @if (_currentDocs.Count > 0)
                        {
                            <div class="mt-4">
                                <MudText Typo="Typo.body2" Style="font-weight: 600; margin-bottom: 6px;">Document</MudText>
                                <MudSelect T="string" Value="_documentFilter" ValueChanged="OnDocumentFilterChanged"
                                           Placeholder="All documents" Clearable="true" Dense="true"
                                           Variant="Variant.Outlined" Margin="Margin.Dense">
                                    <MudSelectItem T="string" Value="@((string?)null)">All documents</MudSelectItem>
                                    @foreach (var doc in _currentDocs)
                                    {
                                        <MudSelectItem T="string" Value="@doc.Id">@doc.FileName</MudSelectItem>
                                    }
                                </MudSelect>
                            </div>
                        }
                    }
                </div>
```

- [ ] **Step 4: Re-apply the active filter after a re-render/re-analysis**

So filters survive a data refresh, re-apply them after (re)rendering. In `RenderGraphAsync`, find:

```csharp
            else
            {
                await JS.InvokeVoidAsync("setGraphData", json);
            }
        }
        catch
        {
            // interop can fail during prerender / circuit teardown — ignore
        }
    }
```

Replace with:

```csharp
            else
            {
                await JS.InvokeVoidAsync("setGraphData", json);
            }
            // Re-assert any active filter against the fresh elements.
            if (_graphInitialized && (!string.IsNullOrEmpty(_search) || _activeTypes.Count > 0 || _documentFilter is not null))
                await ApplyFilterAsync();
        }
        catch
        {
            // interop can fail during prerender / circuit teardown — ignore
        }
    }
```

- [ ] **Step 5: Build**

Run: `dotnet build src/Mjm.LocalDocs.Server/Mjm.LocalDocs.Server.csproj`
Expected: Build succeeded.

- [ ] **Step 6: Run and verify filters**

Run the app on a project with a rendered graph:
1. **Search** — type part of an entity name. Matching nodes get an amber highlight; their neighborhood (within the hop depth) stays visible; everything else fades to near-transparent. Clearing the box restores full visibility.
2. **Hop slider** — move between 0 and 4 with a search active; the isolated neighborhood grows/shrinks accordingly (0 = only exact matches).
3. **Type toggles** — click a type chip to hide/show that type; filled = visible, outlined = hidden. Colors match nodes/legend.
4. **Document filter** — pick a document; only entities/relationships sourced from it stay visible. "All documents" restores.
5. Combine search + type + document — they compose (intersection). Re-run analysis and confirm active filters re-apply to the refreshed graph.

Stop the app.

- [ ] **Step 7: Full regression pass**

Run: `dotnet build` then `dotnet test`
Expected: solution builds; all tests pass (same count as Task 0 baseline + the 2 tests added in Task 2).

- [ ] **Step 8: Commit**

```bash
git add src/Mjm.LocalDocs.Server/Components/Pages/Projects/ProjectGraph.razor
git commit -m "Add graph rail filters with neighborhood isolation"
```

---

## Self-Review (performed while writing — checklist for the executor to re-confirm)

**1. Spec coverage** (`…-design.md` §7):
- §7.1 page + route `/projects/{ProjectId}/graph`, `[Authorize]`, `InteractiveServer` → Task 5. Gating on `Graph:Enabled` **and** `IGraphExtractionService.IsAvailable` → Task 5. Entry point: conditional button on `ProjectDetail` (the repo has no project-scoped `NavMenu`; Chat is done the same way — documented deviation) → Task 5.
- §7.2 Layout A: rail (search, type toggles + counts + color, document filter, hop slider, analyze buttons) · canvas · drawer with current `GraphSource`s → Tasks 5–9.
- §7.3 Cytoscape bundled in `wwwroot` (no CDN) + `graph-interop.js`; `initGraph`/`applyFilter`/`focusNode`/`destroyGraph`; `[JSInvokable] OnNodeSelected/OnEdgeSelected`; neighborhood isolation computed in JS → Tasks 4, 8, 9. Deviation: layout `cose` not `fcose` (documented, one-line swap).
- §7.4 analyze buttons stream `AnalyzeProjectAsync`/`AnalyzeDocumentAsync` via `await foreach` with per-document progress → Task 6.
- §5.3 badge "N documents not analyzed"; document/project deletion hooks → Tasks 6, 3.
- §8 config `LocalDocs:Graph` → Task 1.

**2. Placeholder scan:** no "TBD"/"add error handling"/"similar to Task N" — every step has complete code or an exact command.

**3. Type consistency:** JS globals `initGraph`/`setGraphData`/`applyGraphFilter`/`focusGraphNode`/`destroyGraph` match between Task 4 (definition) and Tasks 7/9 (calls). .NET callbacks `OnNodeSelected`/`OnEdgeSelected`/`OnSelectionCleared` match between Task 4 (JS side) and Task 8 (`[JSInvokable]`). Payload keys `nodes/edges/id/name/type/color/source/target/label/docIds` match between `BuildPayloadJson` (Task 7) and `toElements` (Task 4). `ReloadAsync`/`RenderGraphAsync`/`ApplyFilterAsync`/`RecomputeTypes`/`_hasData`/`_graphInitialized`/`_currentSources`/`_types`/`_currentDocs` are introduced once and reused consistently. Backend `GetCurrentSourcesAsync` signature matches across interface, both impls, both tests, and the page.

---

## Execution Handoff

This plan is written for **superpowers:subagent-driven-development**: one fresh subagent per task, with a two-stage review between tasks. Tasks 0→9 are ordered so each leaves the app building and (from Task 5 on) runnable; the only backend change is Task 2 (one tested read-only method) plus Task 3 (UI-side delete cleanup).

**Verification note for UI tasks:** `.razor` pages are not unit-tested in this repo. Tasks 5–9 are verified by `dotnet build` plus the explicit "Run and verify" manual steps (they require the app running with a configured chat client for the analysis path). Tasks 0, 2, and the final step of Task 9 run `dotnet test` for the automated regression gate.
