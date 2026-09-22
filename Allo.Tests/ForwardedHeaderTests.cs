using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Allo.Tests;

// Behind the reverse proxy, X-Forwarded-For is what the login rate limiter partitions on.
// Trusting it from the wrong sender hands an attacker an unlimited number of identities,
// so what is trusted is worth pinning down.
public class ForwardedHeaderTests
{
    private static ForwardedHeadersOptions Options(params (string Key, string Value)[] settings)
    {
        using var app = new TestApp(settings: settings);
        return app.Factory.Services.GetRequiredService<IOptions<ForwardedHeadersOptions>>().Value;
    }

    [Fact]
    public void WithNoConfiguredProxy_NothingIsTrusted()
    {
        var options = Options();

        // The framework trusts loopback by default. Cleared, because the middleware is only
        // in the pipeline when a proxy is configured and a half-trusted default is worse
        // than an explicit one.
        Assert.Empty(options.KnownProxies);
        Assert.Empty(options.KnownIPNetworks);
    }

    [Fact]
    public void ConfiguredProxies_AreTheOnlyOnesTrusted()
    {
        var options = Options(
            ("ForwardedHeaders:KnownProxies:0", "192.168.1.77"),
            ("ForwardedHeaders:KnownProxies:1", "192.168.1.78"));

        Assert.Equal(
            ["192.168.1.77", "192.168.1.78"],
            options.KnownProxies.Select(p => p.ToString()));
        Assert.Empty(options.KnownIPNetworks);
    }

    [Fact]
    public void BothTheClientAddressAndTheSchemeAreForwarded()
    {
        // Without XForwardedProto the cookie's SameAsRequest policy sees plain http from
        // the proxy and never marks the auth cookie Secure.
        var options = Options(("ForwardedHeaders:KnownProxies:0", "192.168.1.77"));

        Assert.Equal(ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto,
            options.ForwardedHeaders);
    }
}
