using System.Net;
using System.Net.Http.Json;
using Allo.Api.Data;
using Allo.Shared.Models;
using Allo.Shared.Sync;

namespace Allo.Tests;

// The server's rules, exercised over HTTP with hand-built pushes.
public class SyncEndpointTests : IDisposable
{
    private static readonly Guid Milk = CatalogSeeder.NameBasedId("milk");
    private readonly TestApp _app = new();

    private static ListEntry NewEntry(Guid? itemId = null, decimal quantity = 1, Unit unit = Unit.Each) => new()
    {
        Id = Guid.NewGuid(), ListId = ShoppingList.DefaultId, ItemId = itemId ?? Milk,
        CategoryId = Category.UncategorizedId, Quantity = quantity, Unit = unit,
    };

    private static async Task<SyncPushResponse> PushAsync(HttpClient http, SyncPushRequest request)
    {
        var response = await http.PostAsJsonAsync("/api/sync", request);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<SyncPushResponse>())!;
    }

    private static Task<SyncPushResponse> PushEntriesAsync(HttpClient http, params ListEntry[] entries) =>
        PushAsync(http, new SyncPushRequest { Rows = new SyncRows { Entries = [.. entries] } });

    private static async Task<SyncPullResponse> PullAsync(HttpClient http, long since = 0) =>
        (await http.GetFromJsonAsync<SyncPullResponse>($"/api/sync?since={since}"))!;

    private static async Task<ListEntry> ServerEntryAsync(HttpClient http, Guid id) =>
        (await PullAsync(http)).Rows.Entries.Single(e => e.Id == id);

    [Fact]
    public async Task Sync_RequiresLogin()
    {
        var client = _app.CreateClient();

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/sync")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await client.PostAsJsonAsync("/api/sync", new SyncPushRequest())).StatusCode);
    }

    [Fact]
    public async Task FirstPull_HasEverything_AndTheNextPullHasNothing()
    {
        var sam = (await _app.AddPhoneAsync(TestApp.AdminUsername)).Http;

        var first = await PullAsync(sam);
        var second = await PullAsync(sam, first.Cursor);

        Assert.Equal(13, first.Rows.Categories.Count);
        Assert.Equal(CatalogSeeder.Load().Count, first.Rows.Items.Count);
        Assert.Single(first.Rows.Lists);
        Assert.Contains(first.Users, u => u.DisplayName == "Sam");
        Assert.True(second.Rows.IsEmpty);
        Assert.Equal(first.Cursor, second.Cursor);
    }

    [Fact]
    public async Task Push_StampsWhoFromTheLogin_NotThePayload()
    {
        var alex = await _app.AddPhoneAsync("alex");
        var entry = NewEntry();
        entry.AddedBy = Guid.NewGuid();
        entry.UpdatedBy = Guid.NewGuid();

        await PushEntriesAsync(alex.Http, entry);

        var stored = await ServerEntryAsync(alex.Http, entry.Id);
        Assert.Equal(alex.UserId, stored.AddedBy);
        Assert.Equal(alex.UserId, stored.UpdatedBy);
    }

    [Fact]
    public async Task ContentAndChecked_AreResolvedIndependently()
    {
        var sam = await _app.AddPhoneAsync(TestApp.AdminUsername);
        var alex = await _app.AddPhoneAsync("alex");
        var entry = NewEntry();
        await PushEntriesAsync(sam.Http, entry);

        // Sam changes milk 1 → 2; Alex checks milk off. Neither knows about the other.
        var samEdit = NewEntry();
        samEdit.Id = entry.Id;
        samEdit.Quantity = 2;
        await PushEntriesAsync(sam.Http, samEdit);
        await PushAsync(alex.Http, new SyncPushRequest { Checks = [new EntryCheck(entry.Id, true, DateTimeOffset.UtcNow)] });

        var stored = await ServerEntryAsync(sam.Http, entry.Id);
        Assert.Equal(2, stored.Quantity);
        Assert.True(stored.IsChecked);
        Assert.Equal(alex.UserId, stored.CheckedBy);
        Assert.Equal(sam.UserId, stored.UpdatedBy);
    }

    [Fact]
    public async Task LastToArrive_Wins_WhateverThePhoneClocksSay()
    {
        var sam = await _app.AddPhoneAsync(TestApp.AdminUsername);
        var entry = NewEntry();
        await PushEntriesAsync(sam.Http, entry);

        var fastClock = NewEntry(quantity: 2);
        fastClock.Id = entry.Id;
        fastClock.UpdatedAt = DateTimeOffset.UtcNow.AddHours(1);
        var slowClock = NewEntry(quantity: 3);
        slowClock.Id = entry.Id;
        slowClock.UpdatedAt = DateTimeOffset.UtcNow.AddHours(-1);
        await PushEntriesAsync(sam.Http, fastClock);
        await PushEntriesAsync(sam.Http, slowClock);

        Assert.Equal(3, (await ServerEntryAsync(sam.Http, entry.Id)).Quantity);
    }

    [Fact]
    public async Task Deletes_AreFinal()
    {
        var sam = await _app.AddPhoneAsync(TestApp.AdminUsername);
        var entry = NewEntry();
        await PushEntriesAsync(sam.Http, entry);
        var tombstone = NewEntry();
        tombstone.Id = entry.Id;
        tombstone.IsDeleted = true;
        await PushEntriesAsync(sam.Http, tombstone);

        var staleEdit = NewEntry(quantity: 5);
        staleEdit.Id = entry.Id;
        await PushAsync(sam.Http, new SyncPushRequest
        {
            Rows = new SyncRows { Entries = [staleEdit] },
            Checks = [new EntryCheck(entry.Id, true, DateTimeOffset.UtcNow)],
        });

        var stored = await ServerEntryAsync(sam.Http, entry.Id);
        Assert.True(stored.IsDeleted);
        Assert.Equal(1, stored.Quantity);
        Assert.False(stored.IsChecked);
    }

    [Fact]
    public async Task Pull_IncludesTombstones_SinceTheCursor()
    {
        var sam = await _app.AddPhoneAsync(TestApp.AdminUsername);
        var entry = NewEntry();
        await PushEntriesAsync(sam.Http, entry);
        var cursor = (await PullAsync(sam.Http)).Cursor;
        entry.IsDeleted = true;
        await PushEntriesAsync(sam.Http, entry);

        var changes = await PullAsync(sam.Http, cursor);

        Assert.True(Assert.Single(changes.Rows.Entries).IsDeleted);
    }

    [Theory]
    [InlineData(0, Unit.Each)]
    [InlineData(1.5, Unit.Each)]
    [InlineData(-1, Unit.Kilogram)]
    public async Task InvalidQuantity_IsRejected_AndNotStored(decimal quantity, Unit unit)
    {
        var sam = await _app.AddPhoneAsync(TestApp.AdminUsername);
        var entry = NewEntry(quantity: quantity, unit: unit);

        var response = await PushEntriesAsync(sam.Http, entry);

        Assert.Equal(entry.Id.ToString(), Assert.Single(response.Rejections).Key);
        Assert.Empty(response.Current.Entries);
        Assert.DoesNotContain((await PullAsync(sam.Http)).Rows.Entries, e => e.Id == entry.Id);
    }

    [Fact]
    public async Task OneBadRow_DoesNotBlockTheRest()
    {
        var sam = await _app.AddPhoneAsync(TestApp.AdminUsername);
        var good = NewEntry(quantity: 1.5m, unit: Unit.Kilogram);
        var unknownItem = NewEntry(itemId: Guid.NewGuid());

        var response = await PushEntriesAsync(sam.Http, unknownItem, good);

        Assert.Equal("Unknown item.", Assert.Single(response.Rejections).Reason);
        Assert.Equal(1.5m, (await ServerEntryAsync(sam.Http, good.Id)).Quantity);
    }

    [Fact]
    public async Task RejectedUpdate_ReturnsTheServerVersion()
    {
        var sam = await _app.AddPhoneAsync(TestApp.AdminUsername);
        var entry = NewEntry(quantity: 2);
        await PushEntriesAsync(sam.Http, entry);
        entry.Quantity = 0;

        var response = await PushEntriesAsync(sam.Http, entry);

        Assert.Single(response.Rejections);
        Assert.Equal(2, Assert.Single(response.Current.Entries).Quantity);
    }

    [Fact]
    public async Task NewItem_WithAnExistingName_IsMergedIntoIt()
    {
        var sam = await _app.AddPhoneAsync(TestApp.AdminUsername);
        var duplicate = new Item
        {
            Id = Guid.NewGuid(), Name = "  Milk ", DefaultCategoryId = Category.UncategorizedId,
        };
        var entry = NewEntry(itemId: duplicate.Id);

        var response = await PushAsync(sam.Http, new SyncPushRequest
        {
            Rows = new SyncRows { Items = [duplicate], Entries = [entry] },
        });

        Assert.Equal(new ItemRemap(duplicate.Id, Milk), Assert.Single(response.ItemRemaps));
        Assert.Equal(Milk, (await ServerEntryAsync(sam.Http, entry.Id)).ItemId);
        Assert.DoesNotContain((await PullAsync(sam.Http)).Rows.Items, i => i.Id == duplicate.Id);
    }

    [Fact]
    public async Task NewCategory_AndItsChild_CanArriveInEitherOrder()
    {
        var sam = await _app.AddPhoneAsync(TestApp.AdminUsername);
        var parent = new Category { Id = Guid.NewGuid(), Name = "Deli", SortOrder = 35 };
        var child = new Category { Id = Guid.NewGuid(), Name = "Deli Meats", ParentId = parent.Id };

        var response = await PushAsync(sam.Http, new SyncPushRequest
        {
            Rows = new SyncRows { Categories = [child, parent] },
        });

        Assert.Empty(response.Rejections);
        Assert.Contains((await PullAsync(sam.Http)).Rows.Categories, c => c.Id == child.Id && c.ParentId == parent.Id);
    }

    public void Dispose() => _app.Dispose();
}
