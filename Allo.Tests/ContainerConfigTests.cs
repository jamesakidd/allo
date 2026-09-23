using System.Net;
using System.Net.Http.Json;
using Allo.Shared.Auth;

namespace Allo.Tests;

// Unraid, and containers generally, pass a variable the user left blank as an empty
// string rather than leaving it out. Every setting has to read that as "not set", because
// the alternative is a container that will not start and says why only in a stack trace.
public class ContainerConfigTests
{
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-a-number")]
    [InlineData("0")]
    public async Task AnUnusableRateLimit_FallsBackToTheDefault(string value)
    {
        using var app = new TestApp(settings: ("Auth:LoginAttemptsPerMinute", value));
        var client = app.CreateClient();

        // Starts, and still rate limits: five failures allowed, the sixth refused.
        for (var i = 0; i < 5; i++)
        {
            Assert.Equal(HttpStatusCode.Unauthorized,
                (await client.PostAsJsonAsync("/api/auth/login", new LoginRequest("admin", "wrong"))).StatusCode);
        }
        Assert.Equal(HttpStatusCode.TooManyRequests,
            (await client.PostAsJsonAsync("/api/auth/login", new LoginRequest("admin", "wrong"))).StatusCode);
    }

    [Fact]
    public async Task AllTheBlanksAtOnce_StillStarts()
    {
        // What the shipped Unraid template looks like with nothing filled in.
        using var app = new TestApp(settings:
        [
            ("ForwardedHeaders:KnownProxies:0", ""),
            ("Auth:LoginAttemptsPerMinute", ""),
            ("Admin:Username", ""),
            ("Admin:InitialPassword", ""),
            ("Admin:DisplayName", ""),
        ]);

        Assert.Equal(HttpStatusCode.OK, (await app.CreateClient().GetAsync("/healthz")).StatusCode);
    }
}
