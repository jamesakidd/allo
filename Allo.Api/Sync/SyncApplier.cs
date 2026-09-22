using Allo.Api.Data;
using Allo.Shared.Catalog;
using Allo.Shared.Models;
using Allo.Shared.Sync;
using Microsoft.EntityFrameworkCore;

namespace Allo.Api.Sync;

// Applies one push. Rules:
//   - Last to reach the server wins, per field group. Client clocks decide nothing; the
//     timestamps a client sends are stored for display only.
//   - Deletes are final: a change to a tombstoned row is ignored.
//   - Who made a change comes from the login, never from the payload.
//   - A new item whose name matches an existing item is merged into it (ItemRemaps).
// Each row is saved on its own, so one bad row is rejected instead of failing the whole
// batch and leaving the phone retrying it forever.
public sealed class SyncApplier(AppDbContext db, Guid userId)
{
    private readonly SyncPushResponse _response = new();
    private readonly Dictionary<Guid, Guid> _itemRemaps = new();

    public async Task<SyncPushResponse> ApplyAsync(SyncPushRequest request)
    {
        await using (var transaction = await db.Database.BeginTransactionAsync())
        {
            // Referenced tables first, so rows in one batch can point at each other.
            await ApplyRowsAsync(SyncTable.Category, ParentsFirst(request.Rows.Categories), c => c.Id.ToString(),
                c => db.Categories.FindAsync(c.Id), CheckAsync, (to, from) =>
                {
                    to.Name = from.Name.Trim();
                    to.ParentId = from.ParentId;
                    to.SortOrder = from.SortOrder;
                });
            await ApplyRowsAsync(SyncTable.Store, request.Rows.Stores, s => s.Id.ToString(),
                s => db.Stores.FindAsync(s.Id), s => Task.FromResult(SyncValidation.Validate(s)),
                (to, from) =>
                {
                    to.Name = from.Name.Trim();
                    to.Color = from.Color;
                });
            await ApplyRowsAsync(SyncTable.ShoppingList, request.Rows.Lists, l => l.Id.ToString(),
                l => db.ShoppingLists.FindAsync(l.Id), l => Task.FromResult(SyncValidation.Validate(l)),
                (to, from) => to.Name = from.Name.Trim());
            await ApplyRowsAsync(SyncTable.Item, request.Rows.Items, i => i.Id.ToString(),
                i => db.Items.FindAsync(i.Id), CheckAsync, CopyItem, TryMergeItemAsync);
            await ApplyRowsAsync(SyncTable.StoreCategoryOrder, request.Rows.StoreCategoryOrders, SyncKeys.Of,
                o => db.StoreCategoryOrders.FindAsync(o.StoreId, o.CategoryId), CheckAsync,
                (to, from) => to.SortOrder = from.SortOrder);
            await ApplyRowsAsync(SyncTable.ListEntry, request.Rows.Entries, e => e.Id.ToString(),
                e => db.ListEntries.FindAsync(e.Id), CheckAsync, CopyEntryContent, onCreate: e =>
                {
                    e.AddedBy = userId;
                    e.CheckedBy = e.IsChecked ? userId : null;
                });
            await ApplyChecksAsync(request.Checks);
            await transaction.CommitAsync();
        }

        _response.ItemRemaps = _itemRemaps.Select(r => new ItemRemap(r.Key, r.Value)).ToList();
        await CollectCurrentAsync();
        return _response;
    }

    private async Task ApplyRowsAsync<T>(SyncTable table, IEnumerable<T> rows, Func<T, string> key,
        Func<T, ValueTask<T?>> find, Func<T, Task<string?>> check, Action<T, T> copyContent,
        Func<T, Task<bool>>? tryMerge = null, Action<T>? onCreate = null) where T : SyncEntity
    {
        foreach (var row in rows)
        {
            if (await check(row) is { } reason)
            {
                Reject(table, key(row), reason);
                continue;
            }

            var existing = await find(row);
            if (existing is null)
            {
                if (tryMerge is not null && await tryMerge(row))
                {
                    continue;
                }
                copyContent(row, row); // normalizes the incoming values the same way an update would
                row.UpdatedBy = userId;
                onCreate?.Invoke(row);
                db.Add(row);
            }
            else if (!existing.IsDeleted)
            {
                copyContent(existing, row);
                existing.IsDeleted = row.IsDeleted;
                existing.UpdatedAt = row.UpdatedAt;
                existing.UpdatedBy = userId;
            }

            await SaveOrRejectAsync(table, key(row));
        }
    }

    // The checked group: IsChecked, CheckedAt, CheckedBy. Independent of content, so one
    // person checking milk off doesn't undo another changing its quantity.
    private async Task ApplyChecksAsync(IEnumerable<EntryCheck> checks)
    {
        foreach (var check in checks)
        {
            var entry = await db.ListEntries.FindAsync(check.EntryId);
            if (entry is null)
            {
                Reject(SyncTable.ListEntry, check.EntryId.ToString(), "Unknown entry.");
                continue;
            }
            if (entry.IsDeleted)
            {
                continue;
            }
            entry.IsChecked = check.IsChecked;
            entry.CheckedAt = check.CheckedAt;
            entry.CheckedBy = userId;
            await SaveOrRejectAsync(SyncTable.ListEntry, check.EntryId.ToString());
        }
    }

    private async Task SaveOrRejectAsync(SyncTable table, string key)
    {
        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            db.ChangeTracker.Clear();
            Reject(table, key, "The server could not save this change.");
        }
    }

    // A new item with the same name as an existing one is the same item: keep the
    // existing one and point this push's entries at it.
    private async Task<bool> TryMergeItemAsync(Item item)
    {
        var match = db.Items.Local.FirstOrDefault(i => !i.IsDeleted && i.NormalizedName == item.NormalizedName)
            ?? await db.Items.FirstOrDefaultAsync(i => !i.IsDeleted && i.NormalizedName == item.NormalizedName);
        if (match is null)
        {
            return false;
        }
        _itemRemaps[item.Id] = match.Id;
        return true;
    }

    private async Task<string?> CheckAsync(Category category)
    {
        if (SyncValidation.Validate(category) is { } error)
        {
            return error;
        }
        if (category.ParentId is not { } parentId)
        {
            return null;
        }
        return parentId == category.Id ? "A category can't be its own parent."
            : await db.Categories.FindAsync(parentId) is null ? "Unknown parent category."
            : null;
    }

    private async Task<string?> CheckAsync(Item item)
    {
        if (SyncValidation.Validate(item) is { } error)
        {
            return error;
        }
        item.Name = item.Name.Trim();
        item.NormalizedName = CatalogText.Normalize(item.Name);
        return await db.Categories.FindAsync(item.DefaultCategoryId) is null ? "Unknown category." : null;
    }

    private async Task<string?> CheckAsync(StoreCategoryOrder order) =>
        await db.Stores.FindAsync(order.StoreId) is null ? "Unknown store."
        : await db.Categories.FindAsync(order.CategoryId) is null ? "Unknown category."
        : null;

    private async Task<string?> CheckAsync(ListEntry entry)
    {
        if (SyncValidation.Validate(entry) is { } error)
        {
            return error;
        }
        entry.ItemId = _itemRemaps.GetValueOrDefault(entry.ItemId, entry.ItemId);
        return await db.ShoppingLists.FindAsync(entry.ListId) is null ? "Unknown list."
            : await db.Items.FindAsync(entry.ItemId) is null ? "Unknown item."
            : await db.Categories.FindAsync(entry.CategoryId) is null ? "Unknown category."
            : entry.StoreId is { } storeId && await db.Stores.FindAsync(storeId) is null ? "Unknown store."
            : null;
    }

    private static void CopyItem(Item to, Item from)
    {
        to.Name = from.Name;
        to.NormalizedName = from.NormalizedName;
        to.DefaultCategoryId = from.DefaultCategoryId;
        to.DefaultUnit = from.DefaultUnit;
        to.PendingUnit = from.PendingUnit;
        to.Aliases = [.. from.Aliases.Select(CatalogText.Normalize)];
        to.DefaultTags = [.. from.DefaultTags];
        to.Notes = from.Notes;
        to.LastUsedAt = from.LastUsedAt;
        to.UseCount = from.UseCount;
    }

    private static void CopyEntryContent(ListEntry to, ListEntry from)
    {
        to.ListId = from.ListId;
        to.ItemId = from.ItemId;
        to.CategoryId = from.CategoryId;
        to.Quantity = from.Quantity;
        to.Unit = from.Unit;
        to.Note = string.IsNullOrWhiteSpace(from.Note) ? null : from.Note.Trim();
        to.StoreId = from.StoreId;
        to.Tags = [.. from.Tags.Select(t => t.Trim())];
    }

    // Parents before children within the batch, so a new subcategory and its new parent
    // can arrive together in any order.
    private static IEnumerable<Category> ParentsFirst(List<Category> categories)
    {
        var pending = categories.ToList();
        var emitted = new HashSet<Guid>();
        while (pending.Count > 0)
        {
            var ready = pending.Where(c => c.ParentId is not { } p || emitted.Contains(p)
                || pending.All(other => other.Id != p)).ToList();
            if (ready.Count == 0)
            {
                ready = pending; // a cycle: let validation reject what it must
            }
            foreach (var category in ready)
            {
                emitted.Add(category.Id);
                pending.Remove(category);
                yield return category;
            }
        }
    }

    private void Reject(SyncTable table, string key, string reason) =>
        _response.Rejections.Add(new SyncRejection(table, key, reason));

    private async Task CollectCurrentAsync()
    {
        foreach (var rejection in _response.Rejections)
        {
            var current = _response.Current;
            switch (rejection.Table)
            {
                case SyncTable.Category when Guid.TryParse(rejection.Key, out var id):
                    AddIfFound(current.Categories, await db.Categories.AsNoTracking().FirstOrDefaultAsync(r => r.Id == id));
                    break;
                case SyncTable.Store when Guid.TryParse(rejection.Key, out var id):
                    AddIfFound(current.Stores, await db.Stores.AsNoTracking().FirstOrDefaultAsync(r => r.Id == id));
                    break;
                case SyncTable.ShoppingList when Guid.TryParse(rejection.Key, out var id):
                    AddIfFound(current.Lists, await db.ShoppingLists.AsNoTracking().FirstOrDefaultAsync(r => r.Id == id));
                    break;
                case SyncTable.Item when Guid.TryParse(rejection.Key, out var id):
                    AddIfFound(current.Items, await db.Items.AsNoTracking().FirstOrDefaultAsync(r => r.Id == id));
                    break;
                case SyncTable.ListEntry when Guid.TryParse(rejection.Key, out var id):
                    AddIfFound(current.Entries, await db.ListEntries.AsNoTracking().FirstOrDefaultAsync(r => r.Id == id));
                    break;
                case SyncTable.StoreCategoryOrder:
                    var parts = rejection.Key.Split(':');
                    if (parts.Length == 2 && Guid.TryParse(parts[0], out var storeId) && Guid.TryParse(parts[1], out var categoryId))
                    {
                        AddIfFound(current.StoreCategoryOrders, await db.StoreCategoryOrders.AsNoTracking()
                            .FirstOrDefaultAsync(r => r.StoreId == storeId && r.CategoryId == categoryId));
                    }
                    break;
            }
        }
    }

    private static void AddIfFound<T>(List<T> list, T? row) where T : class
    {
        if (row is not null && !list.Contains(row))
        {
            list.Add(row);
        }
    }
}
