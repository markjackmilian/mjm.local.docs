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
    /// OpenAI-specific configuration.
    /// </summary>
    public OpenAIEmbeddingsOptions OpenAI { get; init; } = new();
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
    OpenAI
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
/// Configuration options for storage provider.
/// </summary>
public sealed class StorageOptions
{
    /// <summary>
    /// The storage provider to use.
    /// </summary>
    public StorageProvider Provider { get; init; } = StorageProvider.InMemory;

    /// <summary>
    /// Use sqlite-vec extension for efficient vector search when using SQLite storage.
    /// Only applicable when Provider is Sqlite.
    /// </summary>
    public bool UseSqliteVec { get; init; } = true;
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
    /// SQLite persistent storage.
    /// </summary>
    Sqlite
}
