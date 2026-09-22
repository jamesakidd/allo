using Allo.Shared.Models;

namespace Allo.Shared.Catalog;

public static class CatalogLearning
{
    // Updates the catalog for a confirmed add and returns the item the new entry should
    // point at: the matched item (updated in place) or a newly created one. The caller
    // persists it. categoryId and unit are what the user finally chose, not the suggestion.
    public static Item RecordAdd(CatalogMatch match, string typedName, Guid categoryId, Unit unit,
        Guid userId, DateTimeOffset now)
    {
        var item = match.Kind == MatchKind.Exact && match.Item is not null
            ? match.Item
            : NewItem(typedName, categoryId, unit);

        // Categories are user-owned: an explicit change is a correction, applied at once.
        item.DefaultCategoryId = categoryId;
        ApplyUnit(item, unit);

        item.UseCount++;
        item.LastUsedAt = now;
        item.UpdatedAt = now;
        item.UpdatedBy = userId;
        return item;
    }

    // The same non-default unit twice in a row becomes the default.
    private static void ApplyUnit(Item item, Unit unit)
    {
        if (unit == item.DefaultUnit)
        {
            item.PendingUnit = null;
        }
        else if (item.PendingUnit == unit)
        {
            item.DefaultUnit = unit;
            item.PendingUnit = null;
        }
        else
        {
            item.PendingUnit = unit;
        }
    }

    private static Item NewItem(string typedName, Guid categoryId, Unit unit)
    {
        var name = string.Join(' ', typedName.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        if (name.Length == 0)
        {
            throw new ArgumentException("Item name is required.", nameof(typedName));
        }
        return new Item
        {
            Id = Guid.NewGuid(),
            Name = name,
            NormalizedName = CatalogText.Normalize(name),
            DefaultCategoryId = categoryId,
            DefaultUnit = unit,
        };
    }
}
