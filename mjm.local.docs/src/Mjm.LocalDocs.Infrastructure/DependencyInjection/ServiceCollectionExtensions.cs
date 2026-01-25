using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Mjm.LocalDocs.Core.Abstractions;
using Mjm.LocalDocs.Core.Configuration;
using Mjm.LocalDocs.Infrastructure.Documents;
using Mjm.LocalDocs.Infrastructure.Embeddings;
using Mjm.LocalDocs.Infrastructure.Persistence;
using Mjm.LocalDocs.Infrastructure.Persistence.Repositories;
using Mjm.LocalDocs.Infrastructure.VectorStore;
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

        // Configure storage
        ConfigureStorage(services, options.Storage, connectionString, options.Embeddings.Dimension);

        // Configure embeddings
        ConfigureEmbeddings(services, options.Embeddings);

        // Processing services
        services.AddSingleton<IDocumentProcessor>(new SimpleDocumentProcessor());

        // Document readers
        AddDocumentReaders(services);

        return services;
    }

    private static void AddDocumentReaders(IServiceCollection services)
    {
        // Register individual readers
        services.AddSingleton<PlainTextDocumentReader>();
        services.AddSingleton<PdfDocumentReader>();
        services.AddSingleton<WordDocumentReader>();

        // Register composite reader that aggregates all readers
        services.AddSingleton<CompositeDocumentReader>(sp => new CompositeDocumentReader(
        [
            sp.GetRequiredService<PlainTextDocumentReader>(),
            sp.GetRequiredService<PdfDocumentReader>(),
            sp.GetRequiredService<WordDocumentReader>()
        ]));
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

                services.AddDbContext<LocalDocsDbContext>(options =>
                    options.UseSqlite(connectionString));

                services.AddScoped<IProjectRepository, SqliteProjectRepository>();
                services.AddScoped<IDocumentRepository, SqliteDocumentRepository>();
                
                if (storageOptions.UseSqliteVec)
                {
                    services.AddSingleton<IVectorStore>(sp =>
                        new SqliteVecVectorStore(connectionString, embeddingDimension));
                }
                else
                {
                    services.AddSingleton<IVectorStore>(sp =>
                        new SqliteVectorStore(connectionString, embeddingDimension));
                }
                break;

            case StorageProvider.InMemory:
            default:
                services.AddSingleton<IProjectRepository, InMemoryProjectRepository>();
                services.AddSingleton<IDocumentRepository, InMemoryDocumentRepository>();
                services.AddSingleton<IVectorStore, InMemoryVectorStore>();
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
                break;

            case EmbeddingProvider.Fake:
            default:
                services.AddSingleton<IEmbeddingService>(
                    new FakeEmbeddingService(embeddingsOptions.Dimension));
                break;
        }
    }

    /// <summary>
    /// Adds infrastructure services with SQLite persistence.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="connectionString">SQLite connection string (e.g., "Data Source=localdocs.db").</param>
    /// <param name="embeddingGenerator">The embedding generator to use.</param>
    /// <param name="embeddingDimension">Dimension of embedding vectors (default 1536).</param>
    /// <param name="useSqliteVec">Use sqlite-vec extension for efficient vector search (default true).</param>
    public static IServiceCollection AddLocalDocsSqliteInfrastructure(
        this IServiceCollection services,
        string connectionString,
        IEmbeddingGenerator<string, Embedding<float>> embeddingGenerator,
        int embeddingDimension = 1536,
        bool useSqliteVec = true)
    {
        // DbContext
        services.AddDbContext<LocalDocsDbContext>(options =>
            options.UseSqlite(connectionString));

        // Repositories
        services.AddScoped<IProjectRepository, SqliteProjectRepository>();
        services.AddScoped<IDocumentRepository, SqliteDocumentRepository>();
        
        // Vector store (singleton with its own connection)
        if (useSqliteVec)
        {
            services.AddSingleton<IVectorStore>(sp =>
                new SqliteVecVectorStore(connectionString, embeddingDimension));
        }
        else
        {
            services.AddSingleton<IVectorStore>(sp =>
                new SqliteVectorStore(connectionString, embeddingDimension));
        }

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
    /// <param name="useSqliteVec">Use sqlite-vec extension for efficient vector search (default true).</param>
    public static IServiceCollection AddLocalDocsSqliteFakeInfrastructure(
        this IServiceCollection services,
        string connectionString,
        int embeddingDimension = 1536,
        bool useSqliteVec = true)
    {
        // DbContext
        services.AddDbContext<LocalDocsDbContext>(options =>
            options.UseSqlite(connectionString));

        // Repositories
        services.AddScoped<IProjectRepository, SqliteProjectRepository>();
        services.AddScoped<IDocumentRepository, SqliteDocumentRepository>();
        
        // Vector store (singleton with its own connection)
        if (useSqliteVec)
        {
            services.AddSingleton<IVectorStore>(sp =>
                new SqliteVecVectorStore(connectionString, embeddingDimension));
        }
        else
        {
            services.AddSingleton<IVectorStore>(sp =>
                new SqliteVectorStore(connectionString, embeddingDimension));
        }

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
