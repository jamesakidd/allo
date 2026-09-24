using Allo.Api.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Allo.Tests;

// A migrated SQLite database in a throwaway file, deleted on dispose. Pass a migration
// name to stop there instead, to test what an existing install goes through on upgrade.
public sealed class TestDatabase : IDisposable
{
    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), "allo-tests-" + Guid.NewGuid().ToString("N"));

    public TestDatabase(string? migrateTo = null)
    {
        Directory.CreateDirectory(_directory);
        using var db = CreateContext();
        if (migrateTo is null)
        {
            db.Database.Migrate();
        }
        else
        {
            db.GetService<IMigrator>().Migrate(migrateTo);
        }
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
