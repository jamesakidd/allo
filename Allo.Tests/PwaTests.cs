namespace Allo.Tests;

// The app shell is plain files that no compiler checks. These guard the two mistakes that
// would ship a broken or dangerous build without any other test noticing.
public class PwaTests
{
    private static string RepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Allo.slnx")))
        {
            directory = directory.Parent;
        }
        Assert.NotNull(directory);
        return directory!.FullName;
    }

    private static string Wwwroot(string relative) =>
        File.ReadAllText(Path.Combine(RepoRoot(), "Allo.Client", "wwwroot", relative));

    // Allo.Api hosts the client, and that publish path copies index.html verbatim instead
    // of substituting asset placeholders. A placeholder here boots nothing in production
    // while working perfectly in dev, so it has to be caught at build time.
    [Fact]
    public void IndexHtml_HasNoUnsubstitutedAssetPlaceholders()
    {
        var html = Wwwroot("index.html");

        Assert.DoesNotContain("#[", html);
        Assert.Contains("_framework/blazor.webassembly.js", html);
    }

    // A cached API response would hand someone another account's list, or a stale sync
    // cursor that quietly drops their changes.
    [Fact]
    public void ServiceWorker_NeverCachesTheApi()
    {
        var worker = Wwwroot("service-worker.published.js");

        Assert.Contains("pathname.startsWith('/api/')", worker);
        Assert.Contains("self.skipWaiting()", worker);
    }
}
