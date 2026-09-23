using System.Net;
using System.Security.Claims;
using System.Threading.RateLimiting;
using Allo.Api.Auth;
using Allo.Api.Data;
using Allo.Api.Sync;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
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
            var login = Guid.TryParse(idClaim, out var userId)
                ? await db.UserLogins.AsNoTracking().SingleOrDefaultAsync(l => l.UserId == userId)
                : null;
            if (login is null)
            {
                context.RejectPrincipal();
                await context.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
                return;
            }
            // Read here because this already runs on every request; TemporaryPasswordGate
            // turns it into a 403 for everything but changing the password.
            context.HttpContext.Items[TemporaryPasswordGate.MustChangePasswordItem] = login.MustChangePassword;
        };
    });
builder.Services.AddAuthorization();
builder.Services.AddSingleton<IPasswordHasher<UserLogin>, PasswordHasher<UserLogin>>();

// Behind Nginx Proxy Manager every request arrives from the proxy's address, so without
// this the rate limiter below sees one client for the whole family and the cookie's
// SameAsRequest secure flag sees plain http and never sets Secure.
// Only the addresses in ForwardedHeaders:KnownProxies are trusted, and the framework's
// default trusted networks are cleared: honouring X-Forwarded-For from anyone would let a
// caller claim any address they like and walk straight around the login rate limit. An
// empty list leaves the middleware out of the pipeline entirely, which is what a direct
// run on the LAN wants.
// Blank entries mean "no proxy". A container sets an unfilled variable to an empty string
// rather than leaving it out, so treating "" as an address stops the app from starting.
var knownProxies = (builder.Configuration.GetSection("ForwardedHeaders:KnownProxies").Get<string[]>() ?? [])
    .Select(proxy => proxy?.Trim())
    .Where(proxy => !string.IsNullOrEmpty(proxy))
    .Select(proxy => IPAddress.TryParse(proxy, out var address)
        ? address
        // Fail here, naming the value: the alternative is a FormatException from deep
        // inside the options factory the first time a request arrives.
        : throw new InvalidOperationException(
            $"ForwardedHeaders:KnownProxies contains \"{proxy}\", which is not an IP address."))
    .ToArray();
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.KnownIPNetworks.Clear();
    options.KnownProxies.Clear();
    foreach (var proxy in knownProxies)
    {
        options.KnownProxies.Add(proxy);
    }
});

// Slows password guessing: 5 login attempts per minute per client address by default.
// Parsed rather than bound, because a container hands over a cleared variable as an empty
// string and binding that to an int refuses to start the app. A missing or unusable value
// falls back to the default: a running app with the standard limit beats no app at all.
var loginAttemptsPerMinute =
    int.TryParse(builder.Configuration["Auth:LoginAttemptsPerMinute"], out var configuredAttempts)
    && configuredAttempts > 0
        ? configuredAttempts
        : 5;
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy(AuthEndpoints.LoginRateLimitPolicy, http => RateLimitPartition.GetFixedWindowLimiter(
        http.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = loginAttemptsPerMinute, Window = TimeSpan.FromMinutes(1) }));
});

var app = builder.Build();

// First in the pipeline: everything downstream that reads the client address or the scheme
// needs the real ones, not the proxy's.
if (knownProxies.Length > 0)
{
    app.UseForwardedHeaders();
}

// Security headers live here rather than on the reverse proxy so they travel with the
// container and survive someone rebuilding the proxy entry.
// The CSP is what a Blazor WASM app needs and no more: 'wasm-unsafe-eval' to instantiate
// the runtime, blob: workers for the dotnet threads, and inline styles because MudBlazor
// positions popovers with style attributes. Scripts are same-origin files only — there is
// no inline script left in index.html — so no 'unsafe-inline' for script-src.
app.Use(async (context, next) =>
{
    var headers = context.Response.Headers;
    headers["Content-Security-Policy"] =
        "default-src 'self'; " +
        "script-src 'self' 'wasm-unsafe-eval'; " +
        "style-src 'self' 'unsafe-inline'; " +
        "img-src 'self' data:; " +
        "font-src 'self'; " +
        "connect-src 'self'; " +
        "worker-src 'self' blob:; " +
        "manifest-src 'self'; " +
        "base-uri 'self'; " +
        "form-action 'self'; " +
        "frame-ancestors 'none'";
    headers["X-Content-Type-Options"] = "nosniff";
    headers["Referrer-Policy"] = "no-referrer";
    await next();
});

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
api.AddEndpointFilter(TemporaryPasswordGate.Filter);
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
