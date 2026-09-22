using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Allo.Tests;

public class HealthCheckTests : IDisposable
{
    private readonly string _dbDirectory =
        Path.Combine(Path.GetTempPath(), "allo-tests-" + Guid.NewGuid().ToString("N"));
    private readonly WebApplicationFactory<Program> _factory;

    public HealthCheckTests()
    {
        var dbPath = Path.Combine(_dbDirectory, "allo.db");
        _factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(b => b.UseSetting("ConnectionStrings:Default", $"Data Source={dbPath};Pooling=false"));
    }

    [Fact]
    public async Task Healthz_ReturnsHealthy_AfterAutoMigrate()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/healthz");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Healthy", await response.Content.ReadAsStringAsync());
        Assert.True(File.Exists(Path.Combine(_dbDirectory, "allo.db")));
    }

    [Fact]
    public async Task UnknownApiRoute_Returns404_NotIndexHtml()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/does-not-exist");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    public void Dispose()
    {
        _factory.Dispose();
        if (Directory.Exists(_dbDirectory))
        {
            Directory.Delete(_dbDirectory, recursive: true);
        }
    }
}
