using System.Net.Http.Json;
using Allo.Shared.Models;
using Allo.Shared.Sync;

namespace Allo.Tests;

// The colour ends up inside a style attribute on the list row, so only a plain hex
// triple is allowed in.
public class StoreColourTests : IDisposable
{
    private readonly TestApp _app = new();

    [Theory]
    [InlineData(null)]
    [InlineData("#4fc3f7")]
    [InlineData("#4FC3F7")]
    public void Validate_Accepts_NullOrHex(string? colour) =>
        Assert.Null(SyncValidation.Validate(new Store { Name = "Costco", Color = colour }));

    [Theory]
    [InlineData("red")]
    [InlineData("#4fc3f")]
    [InlineData("#4fc3f7ff")]
    [InlineData("#4fc3f7; background: url(x)")]
    [InlineData("rgb(0,0,0)")]
    public void Validate_Rejects_AnythingElse(string colour) =>
        Assert.NotNull(SyncValidation.Validate(new Store { Name = "Costco", Color = colour }));

    [Fact]
    public async Task Server_RejectsABadColour_AndKeepsAGoodOne()
    {
        var sam = await _app.AddPhoneAsync(TestApp.AdminUsername);
        var good = new Store { Id = Guid.NewGuid(), Name = "Costco", Color = "#4fc3f7" };
        var bad = new Store { Id = Guid.NewGuid(), Name = "Sobeys", Color = "red; content: 'x'" };

        var response = await sam.Http.PostAsJsonAsync("/api/sync",
            new SyncPushRequest { Rows = new SyncRows { Stores = [good, bad] } });
        var pushed = (await response.Content.ReadFromJsonAsync<SyncPushResponse>())!;
        var pulled = (await sam.Http.GetFromJsonAsync<SyncPullResponse>("/api/sync?since=0"))!;

        Assert.Equal(bad.Id.ToString(), Assert.Single(pushed.Rejections).Key);
        Assert.Equal("#4fc3f7", Assert.Single(pulled.Rows.Stores).Color);
    }

    [Fact]
    public async Task Colour_SyncsToAnotherPhone()
    {
        var sam = await _app.AddPhoneAsync(TestApp.AdminUsername);
        var alex = await _app.AddPhoneAsync("alex");
        await sam.SyncAsync();
        await sam.Store.SaveAsync(new Store { Id = Guid.NewGuid(), Name = "Costco", Color = "#ffb74d" });
        await sam.SyncAsync();

        await alex.SyncAsync();

        Assert.Equal("#ffb74d", Assert.Single(alex.Store.Stores).Color);
    }

    public void Dispose() => _app.Dispose();
}
