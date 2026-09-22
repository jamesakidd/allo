using Allo.Shared.Auth;
using Allo.Shared.Models;

namespace Allo.Shared.Sync;

public enum SyncTable
{
    Category,
    Store,
    StoreCategoryOrder,
    Item,
    ShoppingList,
    ListEntry,
}

// Rows for every synced table. Pulls carry full rows; pushes carry the content field
// group of each row (for ListEntry, the checked group travels separately as EntryChecks).
public sealed class SyncRows
{
    public List<Category> Categories { get; set; } = [];
    public List<Store> Stores { get; set; } = [];
    public List<StoreCategoryOrder> StoreCategoryOrders { get; set; } = [];
    public List<Item> Items { get; set; } = [];
    public List<ShoppingList> Lists { get; set; } = [];
    public List<ListEntry> Entries { get; set; } = [];

    public bool IsEmpty => Categories.Count + Stores.Count + StoreCategoryOrders.Count + Items.Count
        + Lists.Count + Entries.Count == 0;
}

// GET /api/sync?since=N: everything with Sequence > N, tombstones included.
public sealed class SyncPullResponse
{
    // The highest sequence covered; the client's next `since`.
    public long Cursor { get; set; }
    public SyncRows Rows { get; set; } = new();
    // The whole family every time (it's tiny), so "checked by Sam" works offline.
    public List<FamilyMember> Users { get; set; } = [];
}

public sealed record EntryCheck(Guid EntryId, bool IsChecked, DateTimeOffset CheckedAt);

// POST /api/sync
public sealed class SyncPushRequest
{
    public SyncRows Rows { get; set; } = new();
    public List<EntryCheck> Checks { get; set; } = [];

    public bool IsEmpty => Rows.IsEmpty && Checks.Count == 0;
}

// A new item that matched an existing item's name was merged into it: the client should
// replace From with To everywhere.
public sealed record ItemRemap(Guid From, Guid To);

public sealed record SyncRejection(SyncTable Table, string Key, string Reason);

public sealed class SyncPushResponse
{
    public List<ItemRemap> ItemRemaps { get; set; } = [];
    public List<SyncRejection> Rejections { get; set; } = [];
    // The server's current version of every rejected row that exists there, so the client
    // can drop its rejected change. A rejected row missing here doesn't exist on the server.
    public SyncRows Current { get; set; } = new();
}

public static class SyncKeys
{
    public static string Of(StoreCategoryOrder order) => $"{order.StoreId}:{order.CategoryId}";
}
