using System.Net;

namespace Allo.Tests;

public class HealthCheckTests : IDisposable
{
    private readonly TestApp _app = new();

    [Fact]
    public async Task Healthz_ReturnsHealthy_AfterAutoMigrate_WithoutLogin()
    {
        var client = _app.CreateClient();

        var response = await client.GetAsync("/healthz");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Healthy", await response.Content.ReadAsStringAsync());
        Assert.True(File.Exists(_app.DatabasePath));
    }

    [Fact]
    public async Task UnknownApiRoute_Returns404_NotIndexHtml()
    {
        var client = _app.CreateClient();

        var response = await client.GetAsync("/api/does-not-exist");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    public void Dispose() => _app.Dispose();
}
