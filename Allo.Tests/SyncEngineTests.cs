using Allo.Api.Data;
using Allo.Shared.Models;
using Allo.Shared.Sync;

namespace Allo.Tests;

// Two phones against the real server, going in and out of airplane mode. This is the
// "test properly in airplane mode" item, automated.
public class SyncEngineTests : IDisposable
{
    private readonly TestApp _app = new();

    private async Task<(Phone Sam, Phone Alex)> TwoSyncedPhonesAsync()
    {
        var sam = await _app.AddPhoneAsync(TestApp.AdminUsername);
        var alex = await _app.AddPhoneAsync("alex");
        await sam.SyncAsync();
        await alex.SyncAsync();
        return (sam, alex);
    }

    [Fact]
    public async Task FirstSync_LoadsTheCatalog()
    {
        var sam = await _app.AddPhoneAsync(TestApp.AdminUsername);

        await sam.SyncAsync();

        Assert.Equal(SyncState.Idle, sam.Engine.State);
        Assert.Equal(CatalogSeeder.Load().Count, sam.Store.Items.Count);
        Assert.NotNull(sam.Store.LastSyncedAt);
        Assert.Contains(sam.Store.Users, u => u.DisplayName == "Sam");
    }

    [Fact]
    public async Task LocalChanges_ShowImmediately_AndReachTheOtherPhone()
    {
        var (sam, alex) = await TwoSyncedPhonesAsync();

        var entry = await sam.AddEntryAsync("milk");
        Assert.Equal(1, sam.Store.PendingCount);
        await sam.SyncAsync();
        await alex.SyncAsync();

        Assert.Equal(0, sam.Store.PendingCount);
        Assert.Contains(alex.Store.Entries, e => e.Id == entry.Id);
    }

    [Fact]
    public async Task AirplaneMode_EditAndCheck_BothSurvive()
    {
        var (sam, alex) = await TwoSyncedPhonesAsync();
        var milk = await sam.AddEntryAsync("milk");
        await sam.SyncAsync();
        await alex.SyncAsync();

        // Sam loses signal and changes milk to 2. Alex, online, checks milk off.
        sam.Network.Offline = true;
        var samMilk = sam.Entry(milk.Id);
        samMilk.Quantity = 2;
        await sam.Store.SaveAsync(samMilk);
        await sam.SyncAsync();
        Assert.Equal(SyncState.Offline, sam.Engine.State);
        Assert.Equal(1, sam.Store.PendingCount);

        await alex.Store.SetCheckedAsync(milk.Id, true, alex.UserId);
        await alex.SyncAsync();

        sam.Network.Offline = false;
        await sam.SyncAsync();
        await alex.SyncAsync();

        foreach (var phone in new[] { sam, alex })
        {
            Assert.Equal(2, phone.Entry(milk.Id).Quantity);
            Assert.True(phone.Entry(milk.Id).IsChecked);
            Assert.Equal(0, phone.Store.PendingCount);
        }
    }

    [Fact]
    public async Task AirplaneMode_DeleteWins_OverAnotherPhonesEdit()
    {
        var (sam, alex) = await TwoSyncedPhonesAsync();
        var bread = await sam.AddEntryAsync("bread");
        await sam.SyncAsync();
        await alex.SyncAsync();

        sam.Network.Offline = true;
        await sam.Store.DeleteAsync(sam.Entry(bread.Id));
        await sam.SyncAsync();
        sam.Network.Offline = false;
        await sam.SyncAsync();

        // Alex's phone hadn't heard yet and edits it: the delete still stands.
        var alexBread = alex.Entry(bread.Id);
        alexBread.Quantity = 3;
        await alex.Store.SaveAsync(alexBread);
        await alex.SyncAsync();

        Assert.True(alex.Entry(bread.Id).IsDeleted);
        Assert.Equal(0, alex.Store.PendingCount);
    }

    [Fact]
    public async Task Queue_SurvivesClosingTheApp()
    {
        var sam = await _app.AddPhoneAsync(TestApp.AdminUsername);
        await sam.SyncAsync();
        sam.Network.Offline = true;
        var eggs = await sam.AddEntryAsync("eggs");

        await sam.ReloadAsync();

        Assert.Equal(1, sam.Store.PendingCount);
        Assert.Contains(sam.Store.Entries, e => e.Id == eggs.Id);
        sam.Network.Offline = false;
        await sam.SyncAsync();
        var alex = await _app.AddPhoneAsync("alex");
        await alex.SyncAsync();
        Assert.Contains(alex.Store.Entries, e => e.Id == eggs.Id);
    }

    [Fact]
    public async Task ExpiredLogin_KeepsTheQueue_UntilLoggedInAgain()
    {
        var sam = await _app.AddPhoneAsync(TestApp.AdminUsername);
        await sam.SyncAsync();
        await sam.Http.PostAsync("/api/auth/logout", null);
        var eggs = await sam.AddEntryAsync("eggs");

        await sam.SyncAsync();
        Assert.Equal(SyncState.NeedsLogin, sam.Engine.State);
        Assert.Equal(1, sam.Store.PendingCount);

        (await sam.Http.PostAsync("/api/auth/login", System.Net.Http.Json.JsonContent.Create(
            new Allo.Shared.Auth.LoginRequest(sam.Username, sam.Password)))).EnsureSuccessStatusCode();
        await sam.SyncAsync();

        Assert.Equal(SyncState.Idle, sam.Engine.State);
        Assert.Equal(0, sam.Store.PendingCount);
        Assert.Equal(sam.Store.Cursor, sam.Store.Entries.Single(e => e.Id == eggs.Id).Sequence);
    }

    [Fact]
    public async Task EditMadeWhilePushing_IsNotLost()
    {
        var (sam, _) = await TwoSyncedPhonesAsync();
        var milk = await sam.AddEntryAsync("milk");
        await sam.SyncAsync();

        var entry = sam.Entry(milk.Id);
        entry.Quantity = 2;
        await sam.Store.SaveAsync(entry);
        // While that push is on the wire, the user taps again.
        sam.BeforePushResponse = async () =>
        {
            sam.BeforePushResponse = null;
            var again = sam.Entry(milk.Id);
            again.Quantity = 3;
            await sam.Store.SaveAsync(again);
        };
        await sam.SyncAsync();

        Assert.Equal(3, sam.Entry(milk.Id).Quantity);
        Assert.Equal(1, sam.Store.PendingCount);
        await sam.SyncAsync();
        Assert.Equal(0, sam.Store.PendingCount);
        var alex = await _app.AddPhoneAsync("bea");
        await alex.SyncAsync();
        Assert.Equal(3, alex.Entry(milk.Id).Quantity);
    }

    [Fact]
    public async Task SameNewItem_OnTwoOfflinePhones_EndsUpAsOneItem()
    {
        var (sam, alex) = await TwoSyncedPhonesAsync();
        sam.Network.Offline = alex.Network.Offline = true;
        var samItem = await NewItemWithEntryAsync(sam, "Lingonberry Jam");
        var alexItem = await NewItemWithEntryAsync(alex, "lingonberry jam");

        sam.Network.Offline = alex.Network.Offline = false;
        await sam.SyncAsync();
        await alex.SyncAsync();
        await sam.SyncAsync();

        foreach (var phone in new[] { sam, alex })
        {
            var jam = Assert.Single(phone.Store.Items, i => i.NormalizedName == "lingonberry jam" && !i.IsDeleted);
            Assert.Equal(samItem.Item.Id, jam.Id);
            Assert.All(phone.Store.Entries.Where(e => e.Id == samItem.Entry.Id || e.Id == alexItem.Entry.Id),
                e => Assert.Equal(jam.Id, e.ItemId));
        }
        Assert.Equal(0, alex.Store.PendingCount);
    }

    private static async Task<(Item Item, ListEntry Entry)> NewItemWithEntryAsync(Phone phone, string name)
    {
        var item = new Item
        {
            Id = Guid.NewGuid(), Name = name, NormalizedName = name.ToLowerInvariant(),
            DefaultCategoryId = Category.UncategorizedId,
        };
        await phone.Store.SaveAsync(item);
        var entry = new ListEntry
        {
            Id = Guid.NewGuid(), ListId = ShoppingList.DefaultId, ItemId = item.Id,
            CategoryId = Category.UncategorizedId, Quantity = 1, AddedBy = phone.UserId,
        };
        await phone.Store.SaveAsync(entry);
        return (item, entry);
    }

    public void Dispose() => _app.Dispose();
}
