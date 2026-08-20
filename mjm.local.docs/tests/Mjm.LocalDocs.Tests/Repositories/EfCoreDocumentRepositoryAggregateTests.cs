using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Mjm.LocalDocs.Core.Abstractions;
using Mjm.LocalDocs.Infrastructure.Persistence;
using Mjm.LocalDocs.Infrastructure.Persistence.Entities;
using Mjm.LocalDocs.Infrastructure.Persistence.Repositories;

namespace Mjm.LocalDocs.Tests.Repositories;

/// <summary>
/// Runs the shared aggregate suite against <see cref="EfCoreDocumentRepository"/> on a
/// temporary SQLite database file that is cleaned up after each test.
/// </summary>
public sealed class EfCoreDocumentRepositoryAggregateTests
    : DocumentRepositoryAggregateTests, IDisposable
{
    private readonly string _testDbPath;
    private readonly LocalDocsDbContext _context;
    private readonly EfCoreDocumentRepository _repository;

    protected override IDocumentRepository Sut => _repository;

    public EfCoreDocumentRepositoryAggregateTests()
    {
        _testDbPath = Path.Combine(Path.GetTempPath(), $"efcore_docrepo_test_{Guid.NewGuid()}.db");

        var options = new DbContextOptionsBuilder<LocalDocsDbContext>()
            .UseSqlite($"Data Source={_testDbPath}")
            .Options;

        _context = new LocalDocsDbContext(options);
        _context.Database.EnsureCreated();
        _repository = new EfCoreDocumentRepository(_context);
    }

    /// <summary>
    /// Documents.ProjectId is a real foreign key here, and the shared seed helper invents
    /// project ids. Create the parent row rather than switching foreign keys off, so the
    /// fixture exercises the same referential integrity production does.
    /// </summary>
    protected override async Task EnsureProjectAsync(string projectId)
    {
        if (await _context.Projects.AnyAsync(p => p.Id == projectId))
            return;

        _context.Projects.Add(new ProjectEntity
        {
            Id = projectId,
            Name = projectId,
            CreatedAt = DateTimeOffset.UtcNow
        });

        await _context.SaveChangesAsync();
    }

    public void Dispose()
    {
        _context.Dispose();

        // Clear connection pool to release the file lock on Windows.
        SqliteConnection.ClearAllPools();
        Thread.Sleep(50);

        try
        {
            if (File.Exists(_testDbPath))
            {
                File.Delete(_testDbPath);
            }
        }
        catch (IOException)
        {
            // Best effort: a lingering temp file is harmless.
        }
    }
}
