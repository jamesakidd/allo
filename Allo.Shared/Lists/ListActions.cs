using Allo.Shared.Catalog;
using Allo.Shared.Models;
using Allo.Shared.Sync;

namespace Allo.Shared.Lists;

// What the item ended up as, so the screen can show the category it picked and whether it
// needs the user's attention.
public sealed record AddOutcome(ListEntry Entry, Item Item, MatchKind Match, bool MergedWithExisting);

// Every list action. They write to the device and return at once; syncing happens after.
public sealed class ListActions(LocalStore store, TimeProvider time)
{
    // Adds typed text to a list. Any #tags in the text are pulled out first. The catalog
    // decides the category and unit: an exact match silently, a partial match as a
    // suggestion the caller should show. Adding something already on the list adds one to
    // it instead of making a second row.
    public async Task<AddOutcome> AddAsync(string text, Guid listId, Guid userId, Guid? storeId = null,
        Guid? categoryId = null, Unit? unit = null, decimal quantity = 1)
    {
        var typed = Tags.Parse(text);
        var match = new CatalogMatcher(store.Items).Match(typed.Text);
        var category = categoryId ?? match.CategoryId;
        var chosenUnit = unit ?? match.Unit;
        var item = CatalogLearning.RecordAdd(match, typed.Text, category, chosenUnit, userId, time.GetUtcNow(),
            typed.Tags.Count > 0 ? typed.Tags : null);
        await store.SaveAsync(item);
        // Typed tags win; otherwise the item's usual ones come along.
        var tags = typed.Tags.Count > 0 ? typed.Tags : Tags.Normalize(item.DefaultTags);

        var existing = ListView.Entries(store, listId, storeId: null)
            .FirstOrDefault(e => e.ItemId == item.Id && !e.IsChecked && e.Unit == chosenUnit);
        if (existing is not null)
        {
            existing.Quantity += quantity;
            existing.Tags = [.. existing.Tags.Union(tags)];
            await store.SaveAsync(existing);
            return new AddOutcome(existing, item, match.Kind, MergedWithExisting: true);
        }

        var entry = new ListEntry
        {
            Id = Guid.NewGuid(),
            ListId = listId,
            ItemId = item.Id,
            CategoryId = category,
            Quantity = quantity,
            Unit = chosenUnit,
            StoreId = storeId,
            Tags = [.. tags],
            AddedBy = userId,
        };
        await store.SaveAsync(entry);
        return new AddOutcome(entry, item, match.Kind, MergedWithExisting: false);
    }

    // Changing an entry's category is also a correction to the catalog, so the next
    // occurrence of that item lands in the right place.
    public async Task SetCategoryAsync(ListEntry entry, Guid categoryId, Guid userId)
    {
        entry.CategoryId = categoryId;
        await store.SaveAsync(entry);
        if (store.Items.FirstOrDefault(i => i.Id == entry.ItemId) is { } item && item.DefaultCategoryId != categoryId)
        {
            item.DefaultCategoryId = categoryId;
            item.UpdatedBy = userId;
            await store.SaveAsync(item);
        }
    }

    public Task SetCheckedAsync(ListEntry entry, bool isChecked, Guid userId) =>
        store.SetCheckedAsync(entry.Id, isChecked, userId);

    public async Task<int> ClearCheckedAsync(Guid listId, Guid? storeId)
    {
        var checkedEntries = ListView.Entries(store, listId, storeId).Where(e => e.IsChecked).ToList();
        foreach (var entry in checkedEntries)
        {
            await store.DeleteAsync(entry);
        }
        return checkedEntries.Count;
    }

    // A new store starts with the default walking order, which it can then be reordered
    // independently of every other store.
    public async Task<Store> CreateStoreAsync(string name, Guid userId)
    {
        var newStore = new Store { Id = Guid.NewGuid(), Name = name.Trim(), UpdatedBy = userId };
        await store.SaveAsync(newStore);
        foreach (var category in store.Categories.Where(c => !c.IsDeleted).ToList())
        {
            await store.SaveAsync(new StoreCategoryOrder
            {
                StoreId = newStore.Id,
                CategoryId = category.Id,
                SortOrder = category.SortOrder,
                UpdatedBy = userId,
            });
        }
        return newStore;
    }

    public async Task<ShoppingList> CreateListAsync(string name, Guid userId)
    {
        var list = new ShoppingList { Id = Guid.NewGuid(), Name = name.Trim(), UpdatedBy = userId };
        await store.SaveAsync(list);
        return list;
    }

    public async Task<Category> CreateCategoryAsync(string name, Guid? parentId, Guid userId)
    {
        var siblings = store.Categories.Where(c => !c.IsDeleted && c.ParentId == parentId
            && c.Id != Category.UncategorizedId).ToList();
        var category = new Category
        {
            Id = Guid.NewGuid(),
            Name = name.Trim(),
            ParentId = parentId,
            SortOrder = siblings.Count == 0 ? 10 : siblings.Max(c => c.SortOrder) + 10,
            UpdatedBy = userId,
        };
        await store.SaveAsync(category);
        return category;
    }

    // Moves a category one place up or down among its siblings by swapping sort orders.
    public async Task MoveCategoryAsync(Category category, bool up, Guid userId)
    {
        var siblings = store.Categories
            .Where(c => !c.IsDeleted && c.ParentId == category.ParentId && c.Id != Category.UncategorizedId)
            .OrderBy(c => c.SortOrder).ToList();
        var index = siblings.FindIndex(c => c.Id == category.Id);
        var swapWith = up ? index - 1 : index + 1;
        if (index < 0 || swapWith < 0 || swapWith >= siblings.Count)
        {
            return;
        }
        var other = siblings[swapWith];
        (category.SortOrder, other.SortOrder) = (other.SortOrder, category.SortOrder);
        category.UpdatedBy = other.UpdatedBy = userId;
        await store.SaveAsync(category);
        await store.SaveAsync(other);
    }

    // Merges one category into another: everything pointing at it is repointed, then it's
    // deleted. Its subcategories move up to the target.
    public async Task MergeCategoriesAsync(Category from, Category into, Guid userId)
    {
        if (from.Id == into.Id || from.Id == Category.UncategorizedId)
        {
            return;
        }
        foreach (var entry in store.Entries.Where(e => !e.IsDeleted && e.CategoryId == from.Id).ToList())
        {
            entry.CategoryId = into.Id;
            entry.UpdatedBy = userId;
            await store.SaveAsync(entry);
        }
        foreach (var item in store.Items.Where(i => !i.IsDeleted && i.DefaultCategoryId == from.Id).ToList())
        {
            item.DefaultCategoryId = into.Id;
            item.UpdatedBy = userId;
            await store.SaveAsync(item);
        }
        foreach (var child in store.Categories.Where(c => !c.IsDeleted && c.ParentId == from.Id).ToList())
        {
            child.ParentId = into.Id;
            child.UpdatedBy = userId;
            await store.SaveAsync(child);
        }
        foreach (var order in store.StoreCategoryOrders.Where(o => !o.IsDeleted && o.CategoryId == from.Id).ToList())
        {
            await store.DeleteAsync(order);
        }
        from.UpdatedBy = userId;
        await store.DeleteAsync(from);
    }

    // Deleting a category leaves its entries somewhere real, never a dangling reference.
    public async Task DeleteCategoryAsync(Category category, Guid userId)
    {
        if (category.Id == Category.UncategorizedId)
        {
            return;
        }
        var target = store.Categories.First(c => c.Id == Category.UncategorizedId);
        await MergeCategoriesAsync(category, target, userId);
    }
}
