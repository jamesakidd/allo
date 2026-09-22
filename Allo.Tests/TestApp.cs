using Microsoft.AspNetCore.Mvc.Testing;

namespace Allo.Tests;

// The real app on a throwaway SQLite file and key directory, with a bootstrap admin
// ("admin" / AdminPassword) that must change its password.
public sealed class TestApp : IDisposable
{
    public const string AdminUsername = "admin";
    public const string AdminPassword = "initial-pass";

    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), "allo-tests-" + Guid.NewGuid().ToString("N"));

    public WebApplicationFactory<Program> Factory { get; }

    public string DatabasePath => Path.Combine(_directory, "allo.db");

    public TestApp()
    {
        Factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b => b
            .UseSetting("ConnectionStrings:Default", $"Data Source={DatabasePath};Pooling=false")
            .UseSetting("DataProtection:KeysPath", Path.Combine(_directory, "keys"))
            .UseSetting("Admin:Username", AdminUsername)
            .UseSetting("Admin:InitialPassword", AdminPassword)
            .UseSetting("Admin:DisplayName", "Sam"));
    }

    // Keeps cookies between requests, like a browser.
    public HttpClient CreateClient() => Factory.CreateClient();

    public void Dispose()
    {
        Factory.Dispose();
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }
}
