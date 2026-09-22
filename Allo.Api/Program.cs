using Allo.Api.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// SQLite file lives in appdata. Override per deployment with ConnectionStrings__Default.
var connectionString = builder.Configuration.GetConnectionString("Default")
    ?? throw new InvalidOperationException("ConnectionStrings:Default is not configured.");
EnsureDatabaseDirectory(connectionString);

builder.Services.AddDbContext<AppDbContext>(options => options.UseSqlite(connectionString));

builder.Services.AddHealthChecks()
    .AddDbContextCheck<AppDbContext>();

var app = builder.Build();

// Auto-migrate on startup: a single-container, single-instance app with no DBA,
// so there is nobody to run migrations by hand before an update.
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.Migrate();
    await CatalogSeeder.SeedAsync(db, DateTimeOffset.UtcNow);
}

if (app.Environment.IsDevelopment())
{
    app.UseWebAssemblyDebugging();
}

// The API serves the published Blazor WASM app same-origin, in dev and in prod,
// so there is no CORS and the auth cookie just works.
app.UseBlazorFrameworkFiles();
app.UseStaticFiles();

app.MapHealthChecks("/healthz");

// Unmatched /api routes must 404 rather than fall through to index.html.
app.MapFallback("/api/{**path}", () => Results.NotFound());
app.MapFallbackToFile("index.html");

app.Run();

static void EnsureDatabaseDirectory(string connectionString)
{
    var dataSource = new SqliteConnectionStringBuilder(connectionString).DataSource;
    var directory = Path.GetDirectoryName(Path.GetFullPath(dataSource));
    if (!string.IsNullOrEmpty(directory))
    {
        Directory.CreateDirectory(directory);
    }
}
