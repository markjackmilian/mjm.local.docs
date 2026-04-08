# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Commands

```bash
# Restore and build
dotnet restore
dotnet build

# Run the application (Web UI: http://localhost:5024, MCP: http://localhost:5024/mcp)
dotnet run --project src/Mjm.LocalDocs.Server/Mjm.LocalDocs.Server.csproj

# Run all tests
dotnet test

# Run a single test project
dotnet test tests/Mjm.LocalDocs.Tests/Mjm.LocalDocs.Tests.csproj

# Run a specific test class or method
dotnet test --filter "FullyQualifiedName~ClassName"
```

Default login: `admin` / `admin`

## Architecture

Three-layer solution under `/src`, one test project under `/tests`:

- **Mjm.LocalDocs.Core** — Domain models, interfaces (repositories, services, storage), and service implementations with no infrastructure dependencies. All abstractions live here.
- **Mjm.LocalDocs.Infrastructure** — Implements Core interfaces: EF Core persistence (SQLite/SQL Server), document readers (PDF via PdfPig, Word via NPOI, Markdown, plain text), embedding providers (Fake, OpenAI/AzureOpenAI/Ollama via Semantic Kernel), vector stores (InMemory, SQLite with sqlite-vec, SQLite+HNSW, SQL Server with native VECTOR type), and file storage (Database, FileSystem, Azure Blob).
- **Mjm.LocalDocs.Server** — Blazor Server UI + MCP server endpoint. `Program.cs` wires everything together. MCP tools in `McpTools/` expose search/document operations to AI agents. Middleware handles MCP authentication (Basic Auth).

### Key Data Flow

1. User uploads a document via the Blazor UI
2. `DocumentService` (Core) delegates to `IDocumentReader` → `IDocumentProcessor` to chunk content
3. `IEmbeddingService` generates vectors for each chunk
4. Chunks + vectors stored via `IVectorStore` and `IDocumentRepository`
5. AI agents call MCP tools (e.g., `search_docs`) → `IVectorStore.SearchAsync` → returns ranked `SearchResult` objects

### Configuration

All runtime behavior is controlled via `appsettings.json` under the `LocalDocs` key:
- **Storage**: `InMemory` | `Sqlite` | `SqliteHnsw` | `SqlServer`
- **Embeddings**: `Fake` (dev) | `OpenAI` | `AzureOpenAI` | `Ollama`
- **FileStorage**: `Database` | `FileSystem` | `AzureBlob`
- **MCP auth**: disabled by default in `appsettings.Development.json`

See `STARTUP.md` for full configuration examples for all deployment scenarios.

### Testing

Tests use xUnit v3 + NSubstitute. Coverage includes document readers, all vector store backends, file storage adapters, and services. Each vector store implementation has its own test class. Infrastructure tests are integration-style (real SQLite files); use `IEmbeddingService` fakes to avoid external dependencies.
