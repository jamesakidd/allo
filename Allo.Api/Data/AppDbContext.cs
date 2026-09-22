using Microsoft.EntityFrameworkCore;

namespace Allo.Api.Data;

// Entities are added in the Data Model phase; Phase 0 only proves the SQLite +
// migration + auto-migrate pipeline works end to end.
public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
}
