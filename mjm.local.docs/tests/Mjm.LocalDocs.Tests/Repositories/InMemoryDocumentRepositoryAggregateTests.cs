using Mjm.LocalDocs.Core.Abstractions;
using Mjm.LocalDocs.Infrastructure.VectorStore;

namespace Mjm.LocalDocs.Tests.Repositories;

/// <summary>
/// Runs the shared aggregate suite against <see cref="InMemoryDocumentRepository"/>,
/// proving behavioural parity with the EF Core implementation.
/// </summary>
public sealed class InMemoryDocumentRepositoryAggregateTests : DocumentRepositoryAggregateTests
{
    private readonly InMemoryDocumentRepository _repository = new();

    protected override IDocumentRepository Sut => _repository;
}
