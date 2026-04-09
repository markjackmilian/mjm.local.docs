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
    /// and optionally IDocumentFileStorage to be registered.
    /// </summary>
    public static IServiceCollection AddLocalDocsCoreServices(this IServiceCollection services)
    {
        services.AddScoped<DocumentService>(sp =>
        {
            var repository = sp.GetRequiredService<IDocumentRepository>();
            var vectorStore = sp.GetRequiredService<IVectorStore>();
            var processor = sp.GetRequiredService<IDocumentProcessor>();
            var embeddingService = sp.GetRequiredService<IEmbeddingService>();
            
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
                fileStorage,
                fileStorageProvider);
        });

        // Chat service for project RAG chat
        services.AddScoped<ProjectChatService>();

        // API Token service for MCP authentication
        services.AddScoped<ApiTokenService>();

        return services;
    }
}
