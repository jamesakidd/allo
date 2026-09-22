using Allo.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace Allo.Tests;

// A migrated SQLite database in a throwaway file, deleted on dispose.
public sealed class TestDatabase : IDisposable
{
    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), "allo-tests-" + Guid.NewGuid().ToString("N"));

    public TestDatabase()
    {
        Directory.CreateDirectory(_directory);
        using var db = CreateContext();
        db.Database.Migrate();
    }

    public AppDbContext CreateContext() => new(new DbContextOptionsBuilder<AppDbContext>()
        .UseSqlite($"Data Source={Path.Combine(_directory, "allo.db")};Pooling=false")
        .Options);

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }
}
