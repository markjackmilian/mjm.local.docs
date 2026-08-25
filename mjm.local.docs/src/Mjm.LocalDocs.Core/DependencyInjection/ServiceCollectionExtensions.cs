using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Mjm.LocalDocs.Core.Abstractions;
using Mjm.LocalDocs.Core.Configuration;
using Mjm.LocalDocs.Core.Services;

namespace Mjm.LocalDocs.Core.DependencyInjection;

/// <summary>
/// Extension methods for configuring Core services.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Adds core document services to the service collection.
    /// Requires IDocumentRepository, IVectorStore, IDocumentProcessor, IEmbeddingService,
    /// IDocumentLockRegistry, and optionally IDocumentFileStorage to be registered.
    /// </summary>
    public static IServiceCollection AddLocalDocsCoreServices(this IServiceCollection services)
    {
        services.AddScoped<DocumentService>(sp =>
        {
            var repository = sp.GetRequiredService<IDocumentRepository>();
            var vectorStore = sp.GetRequiredService<IVectorStore>();
            var processor = sp.GetRequiredService<IDocumentProcessor>();
            var embeddingService = sp.GetRequiredService<IEmbeddingService>();
            var locks = sp.GetRequiredService<IDocumentLockRegistry>();

            // IDocumentFileStorage is optional - null if not registered
            var fileStorage = sp.GetService<IDocumentFileStorage>();

            // Get FileStorageProvider from options
            var options = sp.GetService<IOptions<LocalDocsOptions>>()?.Value;
            var fileStorageProvider = options?.FileStorage.Provider ?? FileStorageProvider.Database;

            return new DocumentService(
                repository,
                vectorStore,
                processor,
                embeddingService,
                locks,
                fileStorage,
                fileStorageProvider);
        });

        services.AddScoped<ProjectService>(sp => new ProjectService(
            sp.GetRequiredService<IProjectRepository>(),
            sp.GetRequiredService<IDocumentRepository>(),
            sp.GetRequiredService<IVectorStore>(),
            sp.GetService<IDocumentFileStorage>()));

        // Chat service for static RAG path (AgenticProjectChatService in Infrastructure wraps this)
        services.AddScoped<ProjectChatService>();

        // API Token service for MCP authentication
        services.AddScoped<ApiTokenService>();

        // Dashboard read-side metrics (growth series + index health)
        services.AddScoped<DashboardMetricsService>();

        return services;
    }
}
