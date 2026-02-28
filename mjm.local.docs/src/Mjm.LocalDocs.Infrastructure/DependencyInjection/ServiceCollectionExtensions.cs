using System.ClientModel;
using Azure.AI.OpenAI;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Mjm.LocalDocs.Core.Abstractions;
using Mjm.LocalDocs.Core.Configuration;
using Mjm.LocalDocs.Infrastructure.Documents;
using Mjm.LocalDocs.Infrastructure.Embeddings;
using Mjm.LocalDocs.Infrastructure.FileStorage;
using Mjm.LocalDocs.Infrastructure.Persistence;
using Mjm.LocalDocs.Infrastructure.Persistence.Repositories;

using Mjm.LocalDocs.Infrastructure.VectorStore;
using Mjm.LocalDocs.Infrastructure.VectorStore.Hnsw;
using OpenAI;

namespace Mjm.LocalDocs.Infrastructure.DependencyInjection;

/// <summary>
/// Extension methods for configuring Infrastructure services.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Adds infrastructure services configured from appsettings.json.
    /// Reads the "LocalDocs" section for embedding and storage provider configuration.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">The application configuration.</param>
    /// <param name="connectionString">Optional SQLite connection string. Required when using SQLite storage.</param>
    /// <returns>The service collection for chaining.</returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when OpenAI provider is configured but API key is missing,
    /// or when SQLite storage is configured but connection string is missing.
    /// </exception>
    public static IServiceCollection AddLocalDocsInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration,
        string? connectionString = null)
    {
        var options = new LocalDocsOptions();
        configuration.GetSection(LocalDocsOptions.SectionName).Bind(options);

        // Register options for dependency injection
        services.TryAddSingleton<IOptions<LocalDocsOptions>>(
            new OptionsWrapper<LocalDocsOptions>(options));

        // Configure storage (repositories and vector store)
        ConfigureStorage(services, options.Storage, connectionString, options.Embeddings.Dimension);

        // Configure file storage for document content
        ConfigureFileStorage(services, options.FileStorage);

        // Configure embeddings
        ConfigureEmbeddings(services, options.Embeddings);

        // Processing services
        services.AddSingleton<IDocumentProcessor>(
            new SimpleDocumentProcessor(options.Chunking.MaxChunkSize, options.Chunking.OverlapSize));

        // Document readers
        AddDocumentReaders(services);

        return services;
    }

    private static void AddDocumentReaders(IServiceCollection services)
    {
        // Register individual readers
        services.AddSingleton<PlainTextDocumentReader>();
        services.AddSingleton<MarkdownDocumentReader>();
        services.AddSingleton<PdfDocumentReader>();
        services.AddSingleton<WordDocumentReader>();

        // Register composite reader that aggregates all readers
        services.AddSingleton<CompositeDocumentReader>(sp => new CompositeDocumentReader(
        [
            sp.GetRequiredService<PlainTextDocumentReader>(),
            sp.GetRequiredService<MarkdownDocumentReader>(),
            sp.GetRequiredService<PdfDocumentReader>(),
            sp.GetRequiredService<WordDocumentReader>()
        ]));

        // Register the interface so controllers can inject IDocumentReader
        services.AddSingleton<IDocumentReader>(sp => sp.GetRequiredService<CompositeDocumentReader>());
    }

    private static void ConfigureStorage(
        IServiceCollection services,
        StorageOptions storageOptions,
        string? connectionString,
        int embeddingDimension)
    {
        switch (storageOptions.Provider)
        {
            case StorageProvider.Sqlite:
                if (string.IsNullOrEmpty(connectionString))
                {
                    throw new InvalidOperationException(
                        "SQLite storage requires a connection string. " +
                        "Configure 'ConnectionStrings:LocalDocs' in appsettings.json.");
                }

                // Use pooled factory for SQLite to support both scoped DbContext and singleton IDbContextFactory
                services.AddPooledDbContextFactory<LocalDocsDbContext>(options =>
                    options.UseSqlite(connectionString));
                services.AddScoped(sp => 
                    sp.GetRequiredService<IDbContextFactory<LocalDocsDbContext>>().CreateDbContext());

                services.AddScoped<IProjectRepository, EfCoreProjectRepository>();
                services.AddScoped<IDocumentRepository, EfCoreDocumentRepository>();
                services.AddScoped<IApiTokenRepository, EfCoreApiTokenRepository>();
                services.AddSingleton<IVectorStore>(sp =>
                    new SqliteVectorStore(connectionString, embeddingDimension));
                break;

            case StorageProvider.SqliteHnsw:
                if (string.IsNullOrEmpty(connectionString))
                {
                    throw new InvalidOperationException(
                        "SQLite+HNSW storage requires a connection string. " +
                        "Configure 'ConnectionStrings:LocalDocs' in appsettings.json.");
                }

                // Use pooled factory for SQLite to support both scoped DbContext and singleton IDbContextFactory
                services.AddPooledDbContextFactory<LocalDocsDbContext>(options =>
                    options.UseSqlite(connectionString));
                services.AddScoped(sp => 
                    sp.GetRequiredService<IDbContextFactory<LocalDocsDbContext>>().CreateDbContext());

                services.AddScoped<IProjectRepository, EfCoreProjectRepository>();
                services.AddScoped<IDocumentRepository, EfCoreDocumentRepository>();
                services.AddScoped<IApiTokenRepository, EfCoreApiTokenRepository>();

                // Use HNSW for vector search instead of brute-force SQLite
                services.AddSingleton<IVectorStore>(sp =>
                    new HnswVectorStore(new HnswVectorStore.Options
                    {
                        IndexPath = storageOptions.Hnsw.IndexPath,
                        MaxConnections = storageOptions.Hnsw.MaxConnections,
                        EfConstruction = storageOptions.Hnsw.EfConstruction,
                        EfSearch = storageOptions.Hnsw.EfSearch,
                        AutoSaveDelayMs = storageOptions.Hnsw.AutoSaveDelayMs
                    }));
                break;

            case StorageProvider.SqlServer:
                if (string.IsNullOrEmpty(connectionString))
                {
                    throw new InvalidOperationException(
                        "SQL Server storage requires a connection string. " +
                        "Configure 'ConnectionStrings:LocalDocs' in appsettings.json.");
                }

                // Use pooled factory for SQL Server to support both scoped DbContext and singleton IDbContextFactory
                services.AddPooledDbContextFactory<LocalDocsDbContext>(options =>
                    options.UseSqlServer(connectionString));
                services.AddScoped(sp => 
                    sp.GetRequiredService<IDbContextFactory<LocalDocsDbContext>>().CreateDbContext());

                services.AddScoped<IProjectRepository, EfCoreProjectRepository>();
                services.AddScoped<IDocumentRepository, EfCoreDocumentRepository>();
                services.AddScoped<IApiTokenRepository, EfCoreApiTokenRepository>();
                
                // Use raw SQL vector store with separate chunk_embeddings table
                // Pass SqlServerOptions to support custom schema, table name, and index settings
                services.AddSingleton<IVectorStore>(sp =>
                    new SqlServerVectorStore(connectionString, embeddingDimension, storageOptions.SqlServer));
                break;

            case StorageProvider.InMemory:
            default:
                services.AddSingleton<IProjectRepository, InMemoryProjectRepository>();
                services.AddSingleton<IDocumentRepository, InMemoryDocumentRepository>();
                services.AddSingleton<IApiTokenRepository, InMemoryApiTokenRepository>();
                services.AddSingleton<IVectorStore, InMemoryVectorStore>();
                break;
        }
    }

    private static void ConfigureFileStorage(
        IServiceCollection services,
        FileStorageOptions fileStorageOptions)
    {
        switch (fileStorageOptions.Provider)
        {
            case FileStorageProvider.FileSystem:
                services.AddSingleton<IDocumentFileStorage>(
                    new FileSystemDocumentFileStorage(fileStorageOptions.FileSystem));
                break;

            case FileStorageProvider.AzureBlob:
                services.AddSingleton<IDocumentFileStorage>(
                    new AzureBlobDocumentFileStorage(fileStorageOptions.AzureBlob));
                break;

            case FileStorageProvider.Database:
            default:
                // For database storage, we need DbContextFactory
                // DbContextFactory is already registered by ConfigureStorage when using Sqlite/SqlServer
                // Only register if not already registered (for InMemory case, this will be a no-op)
                services.TryAddSingleton<IDocumentFileStorage, DatabaseDocumentFileStorage>();
                break;
        }
    }

    private static void ConfigureEmbeddings(
        IServiceCollection services,
        EmbeddingsOptions embeddingsOptions)
    {
        switch (embeddingsOptions.Provider)
        {
            case EmbeddingProvider.OpenAI:
                ConfigureOpenAIEmbeddings(services, embeddingsOptions);
                break;

            case EmbeddingProvider.AzureOpenAI:
                ConfigureAzureOpenAIEmbeddings(services, embeddingsOptions);
                break;

            case EmbeddingProvider.Ollama:
                ConfigureOllamaEmbeddings(services, embeddingsOptions);
                break;

            case EmbeddingProvider.Fake:
            default:
                services.AddSingleton<IEmbeddingService>(
                    new FakeEmbeddingService(embeddingsOptions.Dimension));
                break;
        }
    }

    private static void ConfigureOpenAIEmbeddings(
        IServiceCollection services,
        EmbeddingsOptions embeddingsOptions)
    {
        var apiKey = embeddingsOptions.OpenAI.ApiKey
            ?? Environment.GetEnvironmentVariable("OPENAI_API_KEY");

        if (string.IsNullOrEmpty(apiKey))
        {
            throw new InvalidOperationException(
                "OpenAI embedding provider requires an API key. " +
                "Configure 'LocalDocs:Embeddings:OpenAI:ApiKey' in appsettings.json " +
                "or set the OPENAI_API_KEY environment variable.");
        }

        var openAiClient = new OpenAIClient(apiKey);
        var embeddingGenerator = openAiClient.GetEmbeddingClient(embeddingsOptions.OpenAI.Model)
            .AsIEmbeddingGenerator();

        services.AddSingleton<IEmbeddingService>(
            new SemanticKernelEmbeddingService(embeddingGenerator, embeddingsOptions.Dimension));
    }

    private static void ConfigureAzureOpenAIEmbeddings(
        IServiceCollection services,
        EmbeddingsOptions embeddingsOptions)
    {
        var endpoint = embeddingsOptions.AzureOpenAI.Endpoint
            ?? Environment.GetEnvironmentVariable("AZURE_OPENAI_ENDPOINT");

        var apiKey = embeddingsOptions.AzureOpenAI.ApiKey
            ?? Environment.GetEnvironmentVariable("AZURE_OPENAI_API_KEY");

        if (string.IsNullOrEmpty(endpoint))
        {
            throw new InvalidOperationException(
                "Azure OpenAI embedding provider requires an endpoint. " +
                "Configure 'LocalDocs:Embeddings:AzureOpenAI:Endpoint' in appsettings.json " +
                "or set the AZURE_OPENAI_ENDPOINT environment variable.");
        }

        if (string.IsNullOrEmpty(apiKey))
        {
            throw new InvalidOperationException(
                "Azure OpenAI embedding provider requires an API key. " +
                "Configure 'LocalDocs:Embeddings:AzureOpenAI:ApiKey' in appsettings.json " +
                "or set the AZURE_OPENAI_API_KEY environment variable.");
        }

        var azureClient = new AzureOpenAIClient(
            new Uri(endpoint),
            new ApiKeyCredential(apiKey));

        var embeddingGenerator = azureClient
            .GetEmbeddingClient(embeddingsOptions.AzureOpenAI.DeploymentName)
            .AsIEmbeddingGenerator();

        services.AddSingleton<IEmbeddingService>(
            new SemanticKernelEmbeddingService(embeddingGenerator, embeddingsOptions.Dimension));
    }

    private static void ConfigureOllamaEmbeddings(
        IServiceCollection services,
        EmbeddingsOptions embeddingsOptions)
    {
        var endpoint = new Uri(embeddingsOptions.Ollama.Endpoint);
        var model = embeddingsOptions.Ollama.Model;

        var embeddingGenerator = new OllamaEmbeddingGenerator(endpoint, model);

        services.AddSingleton<IEmbeddingService>(
            new SemanticKernelEmbeddingService(embeddingGenerator, embeddingsOptions.Dimension));
    }

    /// <summary>
    /// Adds infrastructure services with SQLite persistence.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="connectionString">SQLite connection string (e.g., "Data Source=localdocs.db").</param>
    /// <param name="embeddingGenerator">The embedding generator to use.</param>
    /// <param name="embeddingDimension">Dimension of embedding vectors (default 1536).</param>
    public static IServiceCollection AddLocalDocsSqliteInfrastructure(
        this IServiceCollection services,
        string connectionString,
        IEmbeddingGenerator<string, Embedding<float>> embeddingGenerator,
        int embeddingDimension = 1536)
    {
        // DbContext
        services.AddDbContext<LocalDocsDbContext>(options =>
            options.UseSqlite(connectionString));

        // Repositories
        services.AddScoped<IProjectRepository, EfCoreProjectRepository>();
        services.AddScoped<IDocumentRepository, EfCoreDocumentRepository>();
        
        // Vector store (singleton with its own connection)
        services.AddSingleton<IVectorStore>(sp =>
            new SqliteVectorStore(connectionString, embeddingDimension));

        // Processing services
        services.AddSingleton<IDocumentProcessor>(new SimpleDocumentProcessor());
        services.AddSingleton<IEmbeddingService>(
            new SemanticKernelEmbeddingService(embeddingGenerator, embeddingDimension));

        // Document readers
        AddDocumentReaders(services);

        return services;
    }

    /// <summary>
    /// Adds infrastructure services with SQLite persistence and fake embeddings.
    /// Useful for development/testing with persistence but without external API calls.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="connectionString">SQLite connection string (e.g., "Data Source=localdocs.db").</param>
    /// <param name="embeddingDimension">Dimension of embedding vectors (default 1536).</param>
    public static IServiceCollection AddLocalDocsSqliteFakeInfrastructure(
        this IServiceCollection services,
        string connectionString,
        int embeddingDimension = 1536)
    {
        // DbContext
        services.AddDbContext<LocalDocsDbContext>(options =>
            options.UseSqlite(connectionString));

        // Repositories
        services.AddScoped<IProjectRepository, EfCoreProjectRepository>();
        services.AddScoped<IDocumentRepository, EfCoreDocumentRepository>();
        
        // Vector store (singleton with its own connection)
        services.AddSingleton<IVectorStore>(sp =>
            new SqliteVectorStore(connectionString, embeddingDimension));

        // Processing services
        services.AddSingleton<IDocumentProcessor>(new SimpleDocumentProcessor());
        services.AddSingleton<IEmbeddingService>(new FakeEmbeddingService(embeddingDimension));

        // Document readers
        AddDocumentReaders(services);

        return services;
    }

    /// <summary>
    /// Adds infrastructure services with in-memory storage (for dev/test).
    /// </summary>
    public static IServiceCollection AddLocalDocsInMemoryInfrastructure(
        this IServiceCollection services,
        IEmbeddingGenerator<string, Embedding<float>> embeddingGenerator,
        int embeddingDimension = 1536)
    {
        // Repositories
        services.AddSingleton<IProjectRepository, InMemoryProjectRepository>();
        services.AddSingleton<IDocumentRepository, InMemoryDocumentRepository>();
        services.AddSingleton<IVectorStore, InMemoryVectorStore>();

        // Processing services
        services.AddSingleton<IDocumentProcessor>(new SimpleDocumentProcessor());
        services.AddSingleton<IEmbeddingService>(
            new SemanticKernelEmbeddingService(embeddingGenerator, embeddingDimension));

        // Document readers
        AddDocumentReaders(services);

        return services;
    }

    /// <summary>
    /// Adds infrastructure services with fake embedding for development/testing.
    /// Uses in-memory storage and deterministic fake embeddings.
    /// NOT suitable for production use.
    /// </summary>
    public static IServiceCollection AddLocalDocsFakeInfrastructure(
        this IServiceCollection services,
        int embeddingDimension = 1536)
    {
        // Repositories
        services.AddSingleton<IProjectRepository, InMemoryProjectRepository>();
        services.AddSingleton<IDocumentRepository, InMemoryDocumentRepository>();
        services.AddSingleton<IVectorStore, InMemoryVectorStore>();

        // Processing services
        services.AddSingleton<IDocumentProcessor>(new SimpleDocumentProcessor());
        services.AddSingleton<IEmbeddingService>(new FakeEmbeddingService(embeddingDimension));

        // Document readers
        AddDocumentReaders(services);

        return services;
    }

    /// <summary>
    /// Adds infrastructure services with custom implementations.
    /// </summary>
    public static IServiceCollection AddLocalDocsInfrastructure<TProjectRepository, TDocumentRepository, TVectorStore>(
        this IServiceCollection services,
        IEmbeddingGenerator<string, Embedding<float>> embeddingGenerator,
        int embeddingDimension = 1536)
        where TProjectRepository : class, IProjectRepository
        where TDocumentRepository : class, IDocumentRepository
        where TVectorStore : class, IVectorStore
    {
        // Repositories
        services.AddSingleton<IProjectRepository, TProjectRepository>();
        services.AddSingleton<IDocumentRepository, TDocumentRepository>();
        services.AddSingleton<IVectorStore, TVectorStore>();

        // Processing services
        services.AddSingleton<IDocumentProcessor>(new SimpleDocumentProcessor());
        services.AddSingleton<IEmbeddingService>(
            new SemanticKernelEmbeddingService(embeddingGenerator, embeddingDimension));

        // Document readers
        AddDocumentReaders(services);

        return services;
    }
}
