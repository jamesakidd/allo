using Allo.Shared.Models;
using Microsoft.EntityFrameworkCore;

namespace Allo.Tests;

public class DataModelTests : IDisposable
{
    private readonly TestDatabase _database = new();

    [Fact]
    public async Task Seed_HasCategoriesInOrder_WithUncategorizedLast()
    {
        await using var db = _database.CreateContext();

        var names = await db.Categories.OrderBy(c => c.SortOrder).Select(c => c.Name).ToListAsync();

        Assert.Equal(13, names.Count);
        Assert.Equal("Produce", names[0]);
        Assert.Equal("Uncategorized", names[^1]);
        Assert.True(await db.Categories.AnyAsync(c => c.Id == Category.UncategorizedId));
    }

    [Fact]
    public async Task Seed_HasDefaultList_AndRowsVisibleToCursorZero()
    {
        await using var db = _database.CreateContext();

        Assert.True(await db.ShoppingLists.AnyAsync(l => l.Id == ShoppingList.DefaultId));
        Assert.False(await db.Categories.AnyAsync(c => c.Sequence <= 0));
    }

    [Fact]
    public async Task SaveChanges_StampsIncreasingSequence_OnAddAndModify()
    {
        await using var db = _database.CreateContext();
        var user = new User { Id = Guid.NewGuid(), DisplayName = "Sam" };
        var store = NewStore("Costco");
        db.AddRange(user, store, NewStore("No Frills"));
        await db.SaveChangesAsync();
        var afterAdd = store.Sequence;

        store.Name = "Costco Kanata";
        store.UpdatedBy = user.Id;
        await db.SaveChangesAsync();

        Assert.True(afterAdd > 1, "new rows must sort after the seed");
        Assert.True(store.Sequence > afterAdd);
        var sequences = await db.Stores.Select(s => s.Sequence).ToListAsync();
        Assert.Equal(sequences.Count, sequences.Distinct().Count());
    }

    [Fact]
    public async Task SaveChanges_RejectsHardDelete()
    {
        await using var db = _database.CreateContext();
        var store = NewStore("Metro");
        db.Add(store);
        await db.SaveChangesAsync();

        db.Remove(store);

        await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task ListEntry_RoundTripsUnitAsCode_AndDecimalQuantity_AndTags()
    {
        var user = new User { Id = Guid.NewGuid(), DisplayName = "Sam" };
        var item = new Item
        {
            Id = Guid.NewGuid(), Name = "Ground Beef", NormalizedName = "ground beef",
            DefaultCategoryId = Category.UncategorizedId, DefaultUnit = Unit.Pound,
            Aliases = ["hamburger"],
        };
        var entry = new ListEntry
        {
            Id = Guid.NewGuid(), ListId = ShoppingList.DefaultId, ItemId = item.Id,
            CategoryId = Category.UncategorizedId, Quantity = 1.5m, Unit = Unit.Kilogram,
            Tags = ["if on sale"], AddedBy = user.Id,
        };
        await using (var db = _database.CreateContext())
        {
            db.AddRange(user, item, entry);
            await db.SaveChangesAsync();
        }

        await using var read = _database.CreateContext();
        var stored = await read.ListEntries.SingleAsync(e => e.Id == entry.Id);
        var rawUnit = await read.Database
            .SqlQuery<string>($"SELECT Unit AS Value FROM ListEntries WHERE Id = {entry.Id}")
            .ToListAsync();

        Assert.Equal(1.5m, stored.Quantity);
        Assert.Equal(Unit.Kilogram, stored.Unit);
        Assert.Equal(["if on sale"], stored.Tags);
        Assert.Equal("kg", rawUnit.Single());
    }

    private static Store NewStore(string name) => new() { Id = Guid.NewGuid(), Name = name };

    public void Dispose() => _database.Dispose();
}
