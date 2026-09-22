using Allo.Shared.Models;
using Allo.Shared.Sync;

namespace Allo.Shared.Lists;

public sealed record EntryGroup(Category Category, IReadOnlyList<ListEntry> Entries);

// What's still to buy, and below it what's already in the cart. Both keep their
// categories, so a checked item stays recognisable while it's out of the way.
public sealed record ListSections(IReadOnlyList<EntryGroup> ToBuy, IReadOnlyList<EntryGroup> Checked)
{
    public int CheckedCount => Checked.Sum(g => g.Entries.Count);

    public bool IsEmpty => ToBuy.Count == 0 && Checked.Count == 0;
}

// Turns the device's rows into what the list screen shows: entries grouped by category,
// in the active store's walking order.
public static class ListView
{
    public static ListSections Build(LocalStore store, Guid listId, Guid? storeId)
    {
        var categories = store.Categories.Where(c => !c.IsDeleted).ToDictionary(c => c.Id);
        var uncategorized = categories.GetValueOrDefault(Category.UncategorizedId)
            ?? new Category { Id = Category.UncategorizedId, Name = "Uncategorized" };
        var names = store.Items.ToDictionary(i => i.Id, i => i.Name);
        var order = storeId is { } id
            ? store.StoreCategoryOrders.Where(o => o.StoreId == id && !o.IsDeleted)
                .ToDictionary(o => o.CategoryId, o => o.SortOrder)
            : [];

        var entries = Entries(store, listId, storeId).ToList();
        return new ListSections(
            Group(entries.Where(e => !e.IsChecked)),
            Group(entries.Where(e => e.IsChecked)));

        IReadOnlyList<EntryGroup> Group(IEnumerable<ListEntry> source) => source
            .GroupBy(e => categories.GetValueOrDefault(e.CategoryId) ?? uncategorized)
            .Select(g => new EntryGroup(g.Key,
                [.. g.OrderBy(e => names.GetValueOrDefault(e.ItemId, ""), StringComparer.OrdinalIgnoreCase)]))
            .OrderBy(g => g.Category.Id == Category.UncategorizedId) // always last
            .ThenBy(g => SortKey(g.Category, categories, order), SortKeyComparer.Instance)
            .ToList();
    }

    // A store shows its own entries plus anything not tied to a store: things you can buy
    // anywhere shouldn't vanish because you picked a store.
    public static IEnumerable<ListEntry> Entries(LocalStore store, Guid listId, Guid? storeId) =>
        store.Entries.Where(e => !e.IsDeleted && e.ListId == listId
            && (storeId is null || e.StoreId == storeId || e.StoreId is null));

    // Categories sort by their parents' position first, so a subcategory stays with its
    // parent wherever that sits in the store's order.
    private static IReadOnlyList<int> SortKey(Category category, Dictionary<Guid, Category> categories,
        Dictionary<Guid, int> order)
    {
        var key = new List<int>();
        var current = category;
        var guard = 0;
        while (current is not null && guard++ < 20)
        {
            key.Insert(0, order.GetValueOrDefault(current.Id, current.SortOrder));
            current = current.ParentId is { } parentId ? categories.GetValueOrDefault(parentId) : null;
        }
        return key;
    }

    private sealed class SortKeyComparer : IComparer<IReadOnlyList<int>>
    {
        public static readonly SortKeyComparer Instance = new();

        public int Compare(IReadOnlyList<int>? x, IReadOnlyList<int>? y)
        {
            for (var i = 0; i < Math.Min(x!.Count, y!.Count); i++)
            {
                if (x[i] != y[i])
                {
                    return x[i].CompareTo(y[i]);
                }
            }
            return x.Count.CompareTo(y.Count);
        }
    }
}
