namespace Mjm.LocalDocs.Core.Configuration;

/// <summary>
/// Root configuration options for LocalDocs.
/// </summary>
public sealed class LocalDocsOptions
{
    /// <summary>
    /// Configuration section name in appsettings.json.
    /// </summary>
    public const string SectionName = "LocalDocs";

    /// <summary>
    /// Embeddings configuration.
    /// </summary>
    public EmbeddingsOptions Embeddings { get; init; } = new();

    /// <summary>
    /// Storage configuration.
    /// </summary>
    public StorageOptions Storage { get; init; } = new();

    /// <summary>
    /// Document chunking configuration.
    /// </summary>
    public ChunkingOptions Chunking { get; init; } = new();

    /// <summary>
    /// File storage configuration for document content.
    /// </summary>
    public FileStorageOptions FileStorage { get; init; } = new();

    /// <summary>
    /// Chat completion configuration.
    /// </summary>
    public ChatOptions Chat { get; init; } = new();
}

/// <summary>
/// Configuration options for document chunking.
/// </summary>
public sealed class ChunkingOptions
{
    /// <summary>
    /// Maximum characters per chunk.
    /// </summary>
    public int MaxChunkSize { get; init; } = 3000;

    /// <summary>
    /// Overlap between chunks for context continuity.
    /// </summary>
    public int OverlapSize { get; init; } = 300;
}

/// <summary>
/// Configuration options for the embedding provider.
/// </summary>
public sealed class EmbeddingsOptions
{
    /// <summary>
    /// The embedding provider to use.
    /// </summary>
    public EmbeddingProvider Provider { get; init; } = EmbeddingProvider.Fake;

    /// <summary>
    /// Dimension of embedding vectors.
    /// </summary>
    public int Dimension { get; init; } = 1536;

    /// <summary>
    /// Maximum number of texts sent to the embedding provider per request.
    /// Large documents are split into sub-batches of this size.
    /// </summary>
    public int MaxBatchSize { get; init; } = 64;

    /// <summary>
    /// OpenAI-specific configuration.
    /// </summary>
    public OpenAIEmbeddingsOptions OpenAI { get; init; } = new();

    /// <summary>
    /// Azure OpenAI-specific configuration.
    /// </summary>
    public AzureOpenAIEmbeddingsOptions AzureOpenAI { get; init; } = new();

    /// <summary>
    /// Ollama-specific configuration.
    /// </summary>
    public OllamaEmbeddingsOptions Ollama { get; init; } = new();
}

/// <summary>
/// Supported embedding providers.
/// </summary>
public enum EmbeddingProvider
{
    /// <summary>
    /// Fake embeddings for development/testing (no external API calls).
    /// </summary>
    Fake,

    /// <summary>
    /// OpenAI embeddings API.
    /// </summary>
    OpenAI,

    /// <summary>
    /// Azure OpenAI Service embeddings.
    /// </summary>
    AzureOpenAI,

    /// <summary>
    /// Ollama local embeddings (e.g., nomic-embed-text, mxbai-embed-large).
    /// </summary>
    Ollama
}

/// <summary>
/// OpenAI-specific embedding configuration.
/// </summary>
public sealed class OpenAIEmbeddingsOptions
{
    /// <summary>
    /// OpenAI API key. Can also be set via environment variable OPENAI_API_KEY.
    /// </summary>
    public string? ApiKey { get; init; }

    /// <summary>
    /// The model to use for embeddings.
    /// </summary>
    public string Model { get; init; } = "text-embedding-3-small";
}

/// <summary>
/// Azure OpenAI Service embedding configuration.
/// </summary>
public sealed class AzureOpenAIEmbeddingsOptions
{
    /// <summary>
    /// Azure OpenAI endpoint URL (e.g., https://your-resource.openai.azure.com/).
    /// Can also be set via environment variable AZURE_OPENAI_ENDPOINT.
    /// </summary>
    public string? Endpoint { get; init; }

    /// <summary>
    /// Azure OpenAI API key.
    /// Can also be set via environment variable AZURE_OPENAI_API_KEY.
    /// </summary>
    public string? ApiKey { get; init; }

    /// <summary>
    /// The deployment name for the embedding model.
    /// </summary>
    public string DeploymentName { get; init; } = "text-embedding-3-small";
}

/// <summary>
/// Ollama local embedding configuration.
/// </summary>
public sealed class OllamaEmbeddingsOptions
{
    /// <summary>
    /// Ollama server endpoint URL.
    /// </summary>
    public string Endpoint { get; init; } = "http://localhost:11434";

    /// <summary>
    /// The embedding model to use (e.g., nomic-embed-text, mxbai-embed-large, all-minilm).
    /// </summary>
    public string Model { get; init; } = "nomic-embed-text";
}

/// <summary>
/// Configuration options for storage provider.
/// </summary>
public sealed class StorageOptions
{
    /// <summary>
    /// The storage provider to use.
    /// </summary>
    public StorageProvider Provider { get; init; } = StorageProvider.InMemory;

    /// <summary>
    /// HNSW-specific configuration (used when Provider is Hnsw or SqliteHnsw).
    /// </summary>
    public HnswOptions Hnsw { get; init; } = new();

    /// <summary>
    /// SQL Server-specific configuration (used when Provider is SqlServer).
    /// </summary>
    public SqlServerOptions SqlServer { get; init; } = new();
}

/// <summary>
/// Configuration options for HNSW vector index.
/// </summary>
public sealed class HnswOptions
{
    /// <summary>
    /// Path to the HNSW index file.
    /// </summary>
    public string IndexPath { get; init; } = "hnsw_index.bin";

    /// <summary>
    /// Maximum connections per node (M parameter).
    /// Higher values improve recall but increase memory. Recommended: 12-48.
    /// </summary>
    public int MaxConnections { get; init; } = 16;

    /// <summary>
    /// Size of dynamic candidate list during construction.
    /// Higher values improve quality but slow construction. Recommended: 100-500.
    /// </summary>
    public int EfConstruction { get; init; } = 200;

    /// <summary>
    /// Size of dynamic candidate list during search.
    /// Higher values improve recall but slow search. Recommended: 50-500.
    /// </summary>
    public int EfSearch { get; init; } = 50;

    /// <summary>
    /// Auto-save delay in milliseconds. Set to 0 to disable.
    /// </summary>
    public int AutoSaveDelayMs { get; init; } = 5000;
}

/// <summary>
/// Configuration options for SQL Server vector store.
/// </summary>
public sealed class SqlServerOptions
{
    /// <summary>
    /// Schema name for embeddings table (default: dbo).
    /// </summary>
    public string Schema { get; init; } = "dbo";

    /// <summary>
    /// Table name for embeddings (default: chunk_embeddings).
    /// </summary>
    public string TableName { get; init; } = "chunk_embeddings";

    /// <summary>
    /// Use vector index for approximate nearest neighbor search.
    /// If false, uses exact k-NN search with VECTOR_DISTANCE.
    /// </summary>
    public bool UseVectorIndex { get; init; } = true;

    /// <summary>
    /// Distance metric for vector similarity.
    /// Supported values: cosine, euclidean, dotproduct.
    /// </summary>
    public string DistanceMetric { get; init; } = "cosine";
}

/// <summary>
/// Supported storage providers.
/// </summary>
public enum StorageProvider
{
    /// <summary>
    /// In-memory storage (data lost on restart).
    /// </summary>
    InMemory,

    /// <summary>
    /// SQLite persistent storage with brute-force vector search.
    /// </summary>
    Sqlite,

    /// <summary>
    /// SQLite for metadata + HNSW for fast approximate vector search.
    /// Recommended for datasets with 10,000+ documents.
    /// </summary>
    SqliteHnsw,

    /// <summary>
    /// SQL Server or Azure SQL Database with native VECTOR type and indexed search.
    /// Requires SQL Server 2025+, Azure SQL Database, or Azure SQL Managed Instance.
    /// </summary>
    SqlServer
}

/// <summary>
/// Configuration options for document file storage.
/// </summary>
public sealed class FileStorageOptions
{
    /// <summary>
    /// The file storage provider to use for storing document content.
    /// </summary>
    public FileStorageProvider Provider { get; init; } = FileStorageProvider.Database;

    /// <summary>
    /// FileSystem-specific configuration (used when Provider is FileSystem).
    /// </summary>
    public FileSystemStorageOptions FileSystem { get; init; } = new();

    /// <summary>
    /// Azure Blob Storage-specific configuration (used when Provider is AzureBlob).
    /// </summary>
    public AzureBlobStorageOptions AzureBlob { get; init; } = new();
}

/// <summary>
/// Supported file storage providers.
/// </summary>
public enum FileStorageProvider
{
    /// <summary>
    /// Store file content directly in the database (default, legacy behavior).
    /// </summary>
    Database,

    /// <summary>
    /// Store file content on the local file system.
    /// </summary>
    FileSystem,

    /// <summary>
    /// Store file content in Azure Blob Storage.
    /// </summary>
    AzureBlob
}

/// <summary>
/// Configuration options for file system storage.
/// </summary>
public sealed class FileSystemStorageOptions
{
    /// <summary>
    /// Base path where document files will be stored.
    /// Files are organized as: {BasePath}/{ProjectId}/{DocumentId}.{Extension}
    /// </summary>
    public string BasePath { get; init; } = "DocumentFiles";

    /// <summary>
    /// Create the directory structure if it doesn't exist.
    /// </summary>
    public bool CreateDirectoryIfNotExists { get; init; } = true;
}

/// <summary>
/// Configuration options for Azure Blob Storage.
/// </summary>
public sealed class AzureBlobStorageOptions
{
    /// <summary>
    /// Azure Storage connection string.
    /// Can also be set via environment variable AZURE_STORAGE_CONNECTION_STRING.
    /// </summary>
    public string? ConnectionString { get; init; }

    /// <summary>
    /// Name of the blob container to store documents.
    /// </summary>
    public string ContainerName { get; init; } = "documents";

    /// <summary>
    /// Create the container if it doesn't exist.
    /// </summary>
    public bool CreateContainerIfNotExists { get; init; } = true;
}

/// <summary>
/// Configuration options for the chat completion provider.
/// </summary>
public sealed class ChatOptions
{
    /// <summary>
    /// Whether the chat feature is enabled.
    /// When false, the Chat button is hidden from project detail and the chat page redirects away.
    /// </summary>
    public bool Enabled { get; init; } = false;

    /// <summary>
    /// The chat completion provider to use.
    /// </summary>
    public ChatProvider Provider { get; init; } = ChatProvider.Fake;

    /// <summary>
    /// Maximum number of document chunks to include as context in each request.
    /// </summary>
    public int MaxContextChunks { get; init; } = 5;

    /// <summary>
    /// Default mode for the UI toggle. Users can override per-session in the chat page.
    /// When true, the LLM autonomously calls search tools (requires OpenAI, AzureOpenAI, or Ollama).
    /// </summary>
    public bool AgenticMode { get; init; } = false;

    /// <summary>
    /// OpenAI-specific configuration.
    /// </summary>
    public OpenAIChatOptions OpenAI { get; init; } = new();

    /// <summary>
    /// Azure OpenAI-specific configuration.
    /// </summary>
    public AzureOpenAIChatOptions AzureOpenAI { get; init; } = new();

    /// <summary>
    /// Anthropic-specific configuration.
    /// </summary>
    public AnthropicChatOptions Anthropic { get; init; } = new();

    /// <summary>
    /// Ollama-specific configuration.
    /// </summary>
    public OllamaChatOptions Ollama { get; init; } = new();
}

/// <summary>
/// Supported chat completion providers.
/// </summary>
public enum ChatProvider
{
    /// <summary>
    /// Fake/simulated responses for development and testing.
    /// </summary>
    Fake,

    /// <summary>
    /// OpenAI chat completions API.
    /// </summary>
    OpenAI,

    /// <summary>
    /// Azure OpenAI Service chat completions.
    /// </summary>
    AzureOpenAI,

    /// <summary>
    /// Anthropic Claude API.
    /// </summary>
    Anthropic,

    /// <summary>
    /// Ollama local LLM (e.g., llama3, mistral, phi3).
    /// </summary>
    Ollama
}

/// <summary>
/// OpenAI chat completion configuration.
/// </summary>
public sealed class OpenAIChatOptions
{
    /// <summary>
    /// OpenAI API key. Can also be set via environment variable OPENAI_API_KEY.
    /// </summary>
    public string? ApiKey { get; init; }

    /// <summary>
    /// The chat model to use (e.g., gpt-4o, gpt-4o-mini).
    /// </summary>
    public string Model { get; init; } = "gpt-4o-mini";
}

/// <summary>
/// Azure OpenAI Service chat completion configuration.
/// </summary>
public sealed class AzureOpenAIChatOptions
{
    /// <summary>
    /// Azure OpenAI endpoint URL.
    /// Can also be set via environment variable AZURE_OPENAI_ENDPOINT.
    /// </summary>
    public string? Endpoint { get; init; }

    /// <summary>
    /// Azure OpenAI API key.
    /// Can also be set via environment variable AZURE_OPENAI_API_KEY.
    /// </summary>
    public string? ApiKey { get; init; }

    /// <summary>
    /// The deployment name for the chat model.
    /// </summary>
    public string DeploymentName { get; init; } = "gpt-4o";
}

/// <summary>
/// Anthropic chat completion configuration.
/// </summary>
public sealed class AnthropicChatOptions
{
    /// <summary>
    /// Anthropic API key. Can also be set via environment variable ANTHROPIC_API_KEY.
    /// </summary>
    public string? ApiKey { get; init; }

    /// <summary>
    /// The Claude model to use (e.g., claude-sonnet-4-6, claude-haiku-4-5-20251001).
    /// </summary>
    public string Model { get; init; } = "claude-sonnet-4-6";

    /// <summary>
    /// Maximum tokens to generate in the response.
    /// </summary>
    public int MaxTokens { get; init; } = 4096;
}

/// <summary>
/// Ollama local LLM chat completion configuration.
/// </summary>
public sealed class OllamaChatOptions
{
    /// <summary>
    /// Ollama server endpoint URL.
    /// </summary>
    public string Endpoint { get; init; } = "http://localhost:11434";

    /// <summary>
    /// The model to use (e.g., llama3, mistral, phi3, qwen2).
    /// </summary>
    public string Model { get; init; } = "llama3";
}
