# Dashboard — Know-How Growth & Index Health — Design Spec

**Date:** 2026-08-20
**Branch:** `feature/dashboard`
**Status:** Approved, ready for implementation planning

## Goal

Replace the current home dashboard with one that answers a single question: **is the knowledge base actually being expanded, or is it just being touched?** Two halves:

1. A **growth chart** — stacked bars per period, splitting brand-new documents from new versions of existing ones — plus five KPI cards that turn the snapshot into a diagnosis.
2. An **index health panel** — which active documents are uploaded but not searchable — backed by a real fix to the ingestion pipeline that lets those documents exist in the first place.

## Motivating Problem

Two gaps found while reading the current code:

**The dashboard cannot answer the question.** [Home.razor](../../../mjm.local.docs/src/Mjm.LocalDocs.Server/Components/Pages/Home.razor) shows Projects / Documents / Total Size. None has a time dimension, and "Total Size" measures bytes on disk, not knowledge. A KB with 400 documents whose last contribution was 90 days ago renders as a success.

**Silent half-indexing.** In `DocumentService.AddDocumentAsync`, chunks are persisted *before* embeddings, with no transaction and no compensation. If `GenerateEmbeddingsAsync` throws — remote provider down, rate limit, Ollama stopped — the exception propagates but **the document and its chunks stay in the database with no embeddings**. The document looks uploaded, appears in listings, and is not searchable. `IVectorStore` exposes no count, so this state is currently invisible.

**Incidental performance bug.** `GetDocumentsByProjectAsync` does `ToListAsync()` on the whole entity, `FileContent` included. The dashboard calls it in a loop over every project just to count documents and sum sizes — so with `FileStorage: Database` it **loads every file blob in the database into memory on every home page load**.

## Scope Decisions

Settled during brainstorming; recorded here because each one closes off alternatives.

| Decision | Choice | Consequence |
|---|---|---|
| Attribution | **Aggregate only** | No schema change. The dashboard cannot distinguish UI uploads from MCP agent ingestion, nor identify contributors. The chart must speak for itself |
| Growth metric | **Document count** | Not text volume. A half-page note and a 200-page manual count the same |
| New vs revision | **Split, stacked bars** | Recovers the "expanding vs rewriting" signal lost by dropping text volume |
| Index detection | **Per-document reconciliation** | Detects chunks-without-embeddings, not just zero-chunk documents |
| Pipeline gap | **Prevent + repair** | Failures degrade to a clean "not indexed" state, and are repairable from the dashboard |
| UI scope | **Global home only** | No per-project filter, no `ProjectDetail` duplication. Queries stay non-parametric |
| Granularity | **Weeks/months selector** | Cheap because bucketing happens in C#, not SQL |
| KPI set | **Five cards** | Includes Δ vs previous period and days since last contribution |
| Layout | **Chart 2/3 + health column 1/3** | Everything above the fold. Accepted costs: the chart is narrower than full width for 12 stacked bars, and the health donut restates the "not indexed" card |

Deliberately **out of scope**: read-side telemetry (no search/query is recorded anywhere today, so "is anyone consulting the KB?" is unanswerable without new persistence), per-project breakdown, and contributor attribution.

## Metric Semantics

Precision here matters more than anywhere else in the spec — these definitions are the product.

### Growth chart

- 12 buckets, either calendar months or calendar weeks, chosen by the selector.
- **Solid segment** = documents with `ParentDocumentId == null` created in the bucket.
- **Faded segment** = documents with `ParentDocumentId != null` created in the bucket.
- **Superseded documents are included.** A document created in March and replaced in July *was* a March contribution. The series counts creation events, not present state; excluding superseded rows would silently rewrite history on every revision.
- Empty buckets render empty. They are never collapsed — otherwise five months of stall disappear from the chart, which is exactly the finding the chart exists to surface.
- The in-progress bucket is flagged `IsPartial` so a naturally low final bar does not read as a collapse.

### KPI cards

| Card | Definition |
|---|---|
| Active documents | `count(!IsSuperseded)` — knowledge available right now |
| New in period | Non-version documents created in the last 30 days (7 in weekly view) |
| Δ vs previous | The same count minus the same count over the 30 days before that |
| Last contribution | `now − max(CreatedAt)` over **all** documents, new and versions alike: any write is a sign of life. `—` when the KB is empty |
| Not indexed | Active documents that are not fully searchable: **zero chunks, or at least one chunk missing its embedding**. Partially-embedded counts as broken — a document searchable only in part is a trap, not a partial success |

**Rolling windows for the cards, calendar buckets for the chart.** This mismatch is deliberate. A Δ comparing the in-progress calendar month against the previous one would read "−15" on the 3rd of the month purely because the month just started — a card that lies by construction. Rolling windows compare equal-length periods. Calendar buckets give the chart readable labels ("Mar", "Apr") instead of "−60d".

## Architecture

### Read models

New, in `Core/Models/Dashboard/` — lightweight records, no extracted text, no blobs:

```csharp
record DocumentContribution(DateTimeOffset CreatedAt, bool IsNewDocument);
record DocumentChunkTally(string DocumentId, string ProjectId, string FileName, int ChunkCount);
record GrowthBucket(DateTimeOffset Start, string Label, int NewDocuments, int NewVersions, bool IsPartial);
record IndexHealth(int ActiveDocuments, int FullyIndexed, IReadOnlyList<DocumentChunkTally> Broken);
record DashboardMetrics(
    int ActiveDocumentCount,
    int NewInPeriod,
    int NewInPreviousPeriod,
    DateTimeOffset? LastContributionAt,
    IndexHealth Health,
    IReadOnlyList<GrowthBucket> Growth);

enum GrowthGranularity { Weekly, Monthly }
```

### Additions to existing abstractions

| Interface | Method | Purpose |
|---|---|---|
| `IDocumentRepository` | `GetContributionsSinceAsync(since)` | Two-field projection for the time series |
| | `CountActiveDocumentsAsync()` | KPI without materializing entities |
| | `GetLastContributionAtAsync()` | `MAX(CreatedAt)`, which may fall outside the window |
| | `CountChunksAsync()` | Left side of the anti-drift comparison |
| | `GetActiveDocumentChunkTalliesAsync()` | One `GROUP BY`: chunks per active document |
| | `GetChunkIdsByDocumentsAsync(ids)` | Reconciliation only, when needed |
| | `GetActiveDocumentCountsByProjectAsync()` | One `GROUP BY` replacing the recent-projects N+1 |
| `IVectorStore` | `CountAsync()` | Right side of the comparison |
| | `GetExistingChunkIdsAsync(ids)` | Existence probe. Callers batch; the service uses 500 ids per call (SQL Server caps parameters at 2100) |

All four vector stores support both additions without friction: `chunk_embeddings` has `chunk_id` as PRIMARY KEY on SQLite and SQL Server, `InMemoryVectorStore` is a `ConcurrentDictionary`, and `HnswVectorStore` already exposes `_graph.Count` and `_graph.GetAllIds()`.

### `Core/Services/DashboardMetricsService`

A concrete `sealed` class, mirroring how `DocumentService` is registered and consumed. Bucketing happens **in C#**, not SQL: date bucketing is provider-specific (`strftime` vs `DATEPART`) and this project supports both, so a C# fold is provider-agnostic — and it makes the granularity selector nearly free.

The service owns the window arithmetic. `GetMetricsAsync(granularity, ct)` derives `since` from the granularity — the start of the bucket 11 periods back, so that 12 buckets including the current partial one are covered — and passes it to `GetContributionsSinceAsync`. It also formats `GrowthBucket.Label`: abbreviated month name for `Monthly`, ISO week start date (`dd/MM`) for `Weekly`, using `CultureInfo.InvariantCulture` so the axis stays English like every other label on the page (a server with a non-English culture otherwise renders "set, ott, nov" under English headings). Weeks start Monday.

**Fast path for index health.** Reconciling every chunk on every home page load costs O(total chunks). Instead:

1. Compare `CountChunksAsync()` with `IVectorStore.CountAsync()` — two trivial queries.
2. If they match, the only broken documents are the zero-chunk ones, which the tally already knows. The health column renders from three cheap queries.
3. The per-document probe runs **only** when the totals diverge — that is, only when something is genuinely broken.

**Declared limitations.** Equal totals over different sets would slip past the fast path — a pathological coincidence, and the panel's "Review" button forces full reconciliation regardless. More importantly, the totals are *not* reliably equal on a healthy knowledge base: deleting a project cascades through EF to its documents and chunks, but `chunk_embeddings` is not an EF-mapped table, so every project deletion orphans the embeddings it held and the two counts diverge permanently. The fast path therefore degrades to "always take the slow route" on any database where a project was ever deleted — correct, just not fast. Fixing that leak is out of scope here; the property that matters is that divergence can only cost performance, never correctness.

## Pipeline Fix

**Shared method.** Steps 3–6 of `AddDocumentAsync` (chunk → persist chunks → embed → upsert) move into a private `IndexDocumentAsync(document, ct)`, used by both insertion and reindexing. One place where indexing logic lives.

**Prevention.** `AddDocumentAsync` wraps that call. On failure a best-effort cleanup deletes the just-written chunks and embeddings — the document survives, the partial index does not — and a new `DocumentIndexingException(documentId, cause)` is thrown. The cleanup swallows its own errors so it cannot mask the real exception; `OperationCanceledException` is re-thrown unwrapped.

The typed exception exists for callers: the upload UI and the MCP `add_document` tool must be able to say *"document uploaded but not indexed, retry from the dashboard"* rather than *"upload failed"*, which would be false — the file is there.

**Repair.** `ReindexDocumentAsync(documentId, ct)` refuses superseded documents (by design they must have no chunks), wipes to a clean slate — making it idempotent — and calls `IndexDocumentAsync`.

**Interrupted updates.** `UpdateDocumentAsync` reuses `AddDocumentAsync`, so it inherits the protection. But when v2 fails to index, the exception propagates *before* v1 is superseded. Three possible outcomes were considered:

| Outcome | Version chain | Cost |
|---|---|---|
| Supersede anyway | Correct | v1 leaves search and v2 is not in it yet: that knowledge becomes unfindable because a provider blipped |
| Delete v2 | Unchanged | Throws away already-extracted text and forces a re-upload |
| **Leave pending** (chosen) | Incomplete, but **derivable** | A document whose `ParentDocumentId` points at a still-active document *is* an interrupted update — detectable with no new column |

Repair completes the update: when `ReindexDocumentAsync` successfully indexes a document whose parent is still active, it supersedes the parent and strips the parent's chunks and embeddings. Clicking "Reindex" does not only fix the index — **it closes the half-finished update.** No schema change, and no window in which knowledge drops out of search because of a transient provider error.

## UI

New components in `Components/Dashboard/`:

| Component | Contents |
|---|---|
| `StatCard.razor` | Reusable KPI card: icon, value, label, accent, optional delta with arrow. Removes the markup triplicated inline in `Home.razor` today |
| `KnowHowGrowthChart.razor` | The 2/3 card: `MudToggleGroup` months/weeks + `MudChart` `StackedBar`. Takes `IReadOnlyList<GrowthBucket>`, emits `EventCallback<GrowthGranularity>` |
| `IndexHealthPanel.razor` | The 1/3 column: donut with indexed percentage, list of broken documents, per-row "Reindex", and "Review" to force full reconciliation |

`Home.razor` reduces to composition: header, quick actions, a row of five `StatCard`s, a `MudGrid` with the chart at `md="8"` and the panel at `md="4"`, then recent projects. One `GetMetricsAsync()` call in `OnInitializedAsync` plus the per-project count aggregate; changing granularity recomputes buckets only.

**Two declared limitations.** `MudChart` colours are set from C# via `ChartPalette` and do not read the `--ld-*` CSS variables, so the chart uses a fixed two-tone pair (solid accent + accent at 28%) verified against both light and dark themes rather than a theme-reactive palette. And empty states are explicit: a KB with no contributions shows a message instead of a chart of zeros, and zero index problems shows a 100% donut with no list.

## Testing

xUnit v3 + NSubstitute, following the existing style (infrastructure tests are integration-style against real SQLite files).

- **`DashboardMetricsServiceTests`** — monthly and weekly bucketing; empty buckets preserved; partial bucket flagged; new/version split; superseded rows **included** in the series; Δ over rolling windows; `LastContributionAt` falling outside the window; fast path vs probe driven by a fake vector store with a divergent count.
- **`DocumentServiceIndexingTests`** — embedding failure leaves the document present with zero chunks and throws `DocumentIndexingException`; `OperationCanceledException` passes through unwrapped; reindex repairs; reindex **closes the supersession** of a still-active parent; reindex refuses superseded documents; repeated reindex is idempotent.
- **Repositories** — new aggregates against real SQLite, plus behavioural parity on `InMemoryDocumentRepository`.
- **Vector stores** — `CountAsync` and `GetExistingChunkIdsAsync` across all four implementations, including batching beyond 500 ids.

## Global Constraints

- **Solution root:** `C:\Projects\mjm.local.docs\mjm.local.docs` (nested). All `dotnet` commands and `src/...` / `tests/...` paths are relative to that folder. This spec lives in the outer repo under `docs/superpowers/specs/`.
- **Branch:** `feature/dashboard`.
- **Target framework:** `net10.0`, `Nullable` and `ImplicitUsings` enabled.
- **No schema change, no EF migration.** Every new read is derivable from existing columns. This is a hard constraint, not a preference — it follows from the aggregate-only attribution decision.
- **Commits:** concise messages matching repo style. **NEVER** add a `Co-Authored-By` trailer (user global instruction, overrides any default).
- **Domain conventions:** `sealed`, `required` members, string GUID ids, XML doc comments on public members — as in the surrounding code.
- **Blazor conventions:** interactive pages start with `@rendermode InteractiveServer` and `@attribute [Authorize]`; `MudBlazor` is globally imported via `Components/_Imports.razor`; pages add their own `@using` for `Mjm.LocalDocs.Core.Abstractions` / `.Models` / `.Services`.
- **Styling:** reuse the `--ld-*` tokens in `wwwroot/app.css`, including their dark-mode overrides. No new colour literals outside the chart palette noted above.
