using System.Net;
using System.Net.Http.Json;
using Allo.Shared.Auth;

namespace Allo.Tests;

// The app is reachable from the internet, so the boundary is the API, never the UI.
public class SecurityTests
{
    [Fact]
    public async Task ATemporaryPassword_CannotTouchAnythingButItsOwnPassword()
    {
        using var app = new TestApp();
        var client = app.CreateClient();
        await client.PostAsJsonAsync("/api/auth/login",
            new LoginRequest(TestApp.AdminUsername, TestApp.AdminPassword));

        // Reading who you are and changing the password have to keep working.
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/auth/me")).StatusCode);

        // Everything else is refused until the password is real. The UI routes to the
        // Account page, but a temporary password read out over the phone must not keep
        // full API access just because nobody visited that page.
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/sync")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await client.PostAsJsonAsync("/api/users", new AddFamilyMemberRequest("intruder", "Intruder", "temp-pass-1"))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/users")).StatusCode);
    }

    [Fact]
    public async Task OnceTheTemporaryPasswordIsChanged_EverythingOpensUp()
    {
        using var app = new TestApp();
        var client = app.CreateClient();
        await client.PostAsJsonAsync("/api/auth/login",
            new LoginRequest(TestApp.AdminUsername, TestApp.AdminPassword));

        var changed = await client.PostAsJsonAsync("/api/auth/password",
            new ChangePasswordRequest(TestApp.AdminPassword, "a-real-password"));
        changed.EnsureSuccessStatusCode();

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/sync")).StatusCode);
    }

    [Fact]
    public async Task EveryResponseCarriesTheSecurityHeaders()
    {
        using var app = new TestApp();
        var client = app.CreateClient();

        var response = await client.GetAsync("/healthz");

        var csp = Assert.Single(response.Headers.GetValues("Content-Security-Policy"));
        // No 'unsafe-inline' for scripts: index.html has no inline script, and letting one
        // back in is exactly how an injected string becomes an executed one.
        Assert.DoesNotContain("script-src 'self' 'unsafe-inline'", csp);
        Assert.Contains("'wasm-unsafe-eval'", csp);
        Assert.Contains("frame-ancestors 'none'", csp);
        Assert.Equal("nosniff", Assert.Single(response.Headers.GetValues("X-Content-Type-Options")));
    }
}
