using System.Security.Claims;
using System.Threading.RateLimiting;
using Allo.Api.Auth;
using Allo.Api.Data;
using Allo.Api.Sync;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
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

// The keys that encrypt the auth cookie must survive container updates, or every update
// logs the whole family out. Keep them in appdata next to the database.
builder.Services.AddDataProtection()
    .SetApplicationName("Allo")
    .PersistKeysToFileSystem(new DirectoryInfo(builder.Configuration["DataProtection:KeysPath"] ?? "appdata/keys"));

// Cookie auth: nobody should be logged out mid-shop, so a year with sliding renewal.
// Redirects are replaced with status codes since the client is a WASM app, not pages.
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.Cookie.Name = "AlloAuth";
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
        options.ExpireTimeSpan = TimeSpan.FromDays(365);
        options.SlidingExpiration = true;
        options.Events.OnRedirectToLogin = context =>
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return Task.CompletedTask;
        };
        options.Events.OnRedirectToAccessDenied = context =>
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return Task.CompletedTask;
        };
        // A signed cookie stays valid until it expires even if its account is gone (say,
        // the database was restored from an older backup). Re-check on every request so a
        // stale cookie gets a clean 401 instead of failing writes on a missing user.
        options.Events.OnValidatePrincipal = async context =>
        {
            var idClaim = context.Principal?.FindFirstValue(ClaimTypes.NameIdentifier);
            var db = context.HttpContext.RequestServices.GetRequiredService<AppDbContext>();
            if (!Guid.TryParse(idClaim, out var userId) || !await db.UserLogins.AnyAsync(l => l.UserId == userId))
            {
                context.RejectPrincipal();
                await context.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            }
        };
    });
builder.Services.AddAuthorization();
builder.Services.AddSingleton<IPasswordHasher<UserLogin>, PasswordHasher<UserLogin>>();

// Slows password guessing: 5 login attempts per minute per client address by default.
var loginAttemptsPerMinute = builder.Configuration.GetValue("Auth:LoginAttemptsPerMinute", 5);
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy(AuthEndpoints.LoginRateLimitPolicy, http => RateLimitPartition.GetFixedWindowLimiter(
        http.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = loginAttemptsPerMinute, Window = TimeSpan.FromMinutes(1) }));
});

var app = builder.Build();

// Auto-migrate on startup: a single-container, single-instance app with no DBA,
// so there is nobody to run migrations by hand before an update.
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.Migrate();
    await CatalogSeeder.SeedAsync(db, DateTimeOffset.UtcNow);
    await AdminBootstrap.RunAsync(db, scope.ServiceProvider.GetRequiredService<IPasswordHasher<UserLogin>>(),
        app.Configuration, app.Logger);
}

if (app.Environment.IsDevelopment())
{
    app.UseWebAssemblyDebugging();
}

// The API serves the published Blazor WASM app same-origin, in dev and in prod,
// so there is no CORS and the auth cookie just works.
// MapStaticAssets rather than UseStaticFiles: the build already writes Brotli copies of
// every asset along with their hashes, and only this serves them, with immutable caching
// on the fingerprinted ones. The runtime is ~21MB raw and a third of that compressed,
// which is the difference between a usable and an unusable first load on cell data.
app.MapStaticAssets();

app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();

app.MapHealthChecks("/healthz");
// Everything under /api requires a login unless it opts out (login, logout).
var api = app.MapGroup("/api").RequireAuthorization();
api.MapAuthEndpoints();
api.MapSyncEndpoints();

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
