using Allo.Api.Data;
using Allo.Shared.Catalog;
using Allo.Shared.Lists;
using Allo.Shared.Models;
using Allo.Shared.Sync;

namespace Allo.Tests;

// The list screen's logic, on a device store seeded like a freshly synced phone.
public class ListTests
{
    private static readonly Guid User = Guid.NewGuid();
    private static readonly Guid Produce = SeedData.CategoryIdsByName["Produce"];
    private static readonly Guid Dairy = SeedData.CategoryIdsByName["Dairy"];
    private static readonly Guid Bakery = SeedData.CategoryIdsByName["Bakery"];

    private readonly LocalStore _store = new(new InMemoryStorage(), TimeProvider.System);
    private readonly ListActions _actions;

    public ListTests()
    {
        _actions = new ListActions(_store, TimeProvider.System);
        Seed().GetAwaiter().GetResult();
    }

    private async Task Seed()
    {
        await _store.ApplyPullAsync(new SyncPullResponse
        {
            Cursor = 1,
            Rows = new SyncRows
            {
                Categories = [.. SeedCategories()],
                Lists = [new ShoppingList { Id = ShoppingList.DefaultId, Name = "Groceries", Sequence = 1 }],
                Items = [.. CatalogSeeder.Load().Select(CatalogSeeder.ToItem)],
            },
        });
    }

    private static IEnumerable<Category> SeedCategories()
    {
        var names = new[]
        {
            "Produce", "Bakery", "Dairy", "Meat & Seafood", "Frozen", "Pantry",
            "Beverages", "Snacks", "Household", "Personal Care", "Baby", "Pet",
        };
        var categories = names.Select((name, i) => new Category
        {
            Id = SeedData.CategoryIdsByName[name], Name = name, SortOrder = (i + 1) * 10, Sequence = 1,
        }).ToList();
        categories.Add(new Category
        {
            Id = Category.UncategorizedId, Name = "Uncategorized", SortOrder = int.MaxValue, Sequence = 1,
        });
        return categories;
    }

    private IReadOnlyList<EntryGroup> Groups(Guid? storeId = null) =>
        ListView.Build(_store, ShoppingList.DefaultId, storeId);

    [Fact]
    public async Task Add_UsesTheCatalogCategoryAndUnit()
    {
        var outcome = await _actions.AddAsync("bananas", ShoppingList.DefaultId, User);

        Assert.Equal(MatchKind.Exact, outcome.Match);
        Assert.Equal(Produce, outcome.Entry.CategoryId);
        Assert.Equal(Unit.Kilogram, outcome.Entry.Unit);
        Assert.Equal(1, outcome.Entry.Quantity);
    }

    [Fact]
    public async Task Add_UnknownText_LandsInUncategorized_AsANewItem()
    {
        var outcome = await _actions.AddAsync("Zamboni wax", ShoppingList.DefaultId, User);

        Assert.Equal(MatchKind.None, outcome.Match);
        Assert.Equal(Category.UncategorizedId, outcome.Entry.CategoryId);
        Assert.Equal("Zamboni wax", outcome.Item.Name);
        Assert.Contains(_store.Items, i => i.Id == outcome.Item.Id);
    }

    [Fact]
    public async Task Add_PartialMatch_IsASuggestion_TheUserCanOverride()
    {
        var suggested = await _actions.AddAsync("rosemary sourdough bread", ShoppingList.DefaultId, User);
        var corrected = await _actions.AddAsync("seedy rye loaf", ShoppingList.DefaultId, User, categoryId: Dairy);

        Assert.Equal(MatchKind.Suggested, suggested.Match);
        Assert.Equal(Bakery, suggested.Entry.CategoryId);
        Assert.Equal(Dairy, corrected.Entry.CategoryId);
        // The correction is remembered for next time.
        Assert.Equal(Dairy, _store.Items.Single(i => i.NormalizedName == "seedy rye loaf").DefaultCategoryId);
    }

    [Fact]
    public async Task Add_SameItemTwice_AddsToTheExistingRow()
    {
        var first = await _actions.AddAsync("milk", ShoppingList.DefaultId, User);
        var second = await _actions.AddAsync("Milk", ShoppingList.DefaultId, User);

        Assert.True(second.MergedWithExisting);
        Assert.Equal(first.Entry.Id, second.Entry.Id);
        Assert.Equal(2, second.Entry.Quantity);
        Assert.Single(ListView.Entries(_store, ShoppingList.DefaultId, null));
    }

    [Fact]
    public async Task Add_AfterChecking_StartsAFreshRow()
    {
        var first = await _actions.AddAsync("milk", ShoppingList.DefaultId, User);
        await _actions.SetCheckedAsync(first.Entry, true, User);

        var second = await _actions.AddAsync("milk", ShoppingList.DefaultId, User);

        Assert.False(second.MergedWithExisting);
        Assert.Equal(2, ListView.Entries(_store, ShoppingList.DefaultId, null).Count());
    }

    [Fact]
    public async Task Groups_FollowCategoryOrder_WithUncategorizedLast()
    {
        await _actions.AddAsync("milk", ShoppingList.DefaultId, User);
        await _actions.AddAsync("Zamboni wax", ShoppingList.DefaultId, User);
        await _actions.AddAsync("bananas", ShoppingList.DefaultId, User);
        await _actions.AddAsync("bagels", ShoppingList.DefaultId, User);

        var groups = Groups();

        Assert.Equal(["Produce", "Bakery", "Dairy", "Uncategorized"], groups.Select(g => g.Category.Name));
    }

    [Fact]
    public async Task Groups_FollowTheStoresOwnOrder()
    {
        await _actions.AddAsync("milk", ShoppingList.DefaultId, User);
        await _actions.AddAsync("bananas", ShoppingList.DefaultId, User);
        var costco = await _actions.CreateStoreAsync("Costco", User);
        // At Costco, dairy comes before produce.
        var dairyOrder = _store.StoreCategoryOrders.Single(o => o.StoreId == costco.Id && o.CategoryId == Dairy);
        dairyOrder.SortOrder = 1;
        await _store.SaveAsync(dairyOrder);

        Assert.Equal(["Produce", "Dairy"], Groups().Select(g => g.Category.Name));
        Assert.Equal(["Dairy", "Produce"], Groups(costco.Id).Select(g => g.Category.Name));
    }

    [Fact]
    public async Task Subcategories_StayWithTheirParent()
    {
        var cheese = await _actions.CreateCategoryAsync("Cheese", Dairy, User);
        await _actions.AddAsync("bananas", ShoppingList.DefaultId, User);
        var milk = await _actions.AddAsync("milk", ShoppingList.DefaultId, User);
        var brie = await _actions.AddAsync("brie", ShoppingList.DefaultId, User);
        await _actions.SetCategoryAsync(brie.Entry, cheese.Id, User);
        await _actions.AddAsync("bagels", ShoppingList.DefaultId, User);

        Assert.Equal(["Produce", "Bakery", "Dairy", "Cheese"], Groups().Select(g => g.Category.Name));
        Assert.Equal(milk.Entry.Id, Groups().Single(g => g.Category.Name == "Dairy").Entries.Single().Id);
    }

    [Fact]
    public async Task CheckedEntries_SinkToTheBottomOfTheirCategory()
    {
        var milk = await _actions.AddAsync("milk", ShoppingList.DefaultId, User);
        await _actions.AddAsync("butter", ShoppingList.DefaultId, User);
        await _actions.AddAsync("yogurt", ShoppingList.DefaultId, User);

        await _actions.SetCheckedAsync(milk.Entry, true, User);

        var dairy = Groups().Single(g => g.Category.Name == "Dairy");
        Assert.Equal(milk.Entry.Id, dairy.Entries[^1].Id);
        Assert.Equal(2, dairy.UncheckedCount);
    }

    [Fact]
    public async Task StoreView_ShowsThatStoresEntries_PlusUnassignedOnes()
    {
        var costco = await _actions.CreateStoreAsync("Costco", User);
        var metro = await _actions.CreateStoreAsync("Metro", User);
        var anywhere = await _actions.AddAsync("milk", ShoppingList.DefaultId, User);
        var atCostco = await _actions.AddAsync("bananas", ShoppingList.DefaultId, User, storeId: costco.Id);
        var atMetro = await _actions.AddAsync("bagels", ShoppingList.DefaultId, User, storeId: metro.Id);

        var ids = Groups(costco.Id).SelectMany(g => g.Entries).Select(e => e.Id).ToList();

        Assert.Contains(anywhere.Entry.Id, ids);
        Assert.Contains(atCostco.Entry.Id, ids);
        Assert.DoesNotContain(atMetro.Entry.Id, ids);
    }

    [Fact]
    public async Task ClearChecked_TombstonesThem_AndLeavesTheRest()
    {
        var milk = await _actions.AddAsync("milk", ShoppingList.DefaultId, User);
        var bread = await _actions.AddAsync("bread", ShoppingList.DefaultId, User);
        await _actions.SetCheckedAsync(milk.Entry, true, User);

        var cleared = await _actions.ClearCheckedAsync(ShoppingList.DefaultId, null);

        Assert.Equal(1, cleared);
        Assert.True(_store.Entries.Single(e => e.Id == milk.Entry.Id).IsDeleted);
        Assert.Equal(bread.Entry.Id, Assert.Single(ListView.Entries(_store, ShoppingList.DefaultId, null)).Id);
    }

    [Fact]
    public async Task SecondList_KeepsItsOwnEntries()
    {
        var watch = await _actions.CreateListAsync("Watch list", User);
        await _actions.AddAsync("milk", ShoppingList.DefaultId, User);
        var watched = await _actions.AddAsync("ribeye", watch.Id, User);

        Assert.Equal(watched.Entry.Id, Assert.Single(ListView.Entries(_store, watch.Id, null)).Id);
        Assert.Single(ListView.Entries(_store, ShoppingList.DefaultId, null));
    }

    [Fact]
    public async Task MergeCategories_MovesEverything_AndDeletesTheOldOne()
    {
        var cheese = await _actions.CreateCategoryAsync("Cheese", null, User);
        var brie = await _actions.AddAsync("brie", ShoppingList.DefaultId, User);
        await _actions.SetCategoryAsync(brie.Entry, cheese.Id, User);

        await _actions.MergeCategoriesAsync(cheese, _store.Categories.Single(c => c.Id == Dairy), User);

        Assert.Equal(Dairy, _store.Entries.Single(e => e.Id == brie.Entry.Id).CategoryId);
        Assert.Equal(Dairy, _store.Items.Single(i => i.NormalizedName == "brie").DefaultCategoryId);
        Assert.True(_store.Categories.Single(c => c.Id == cheese.Id).IsDeleted);
    }

    [Fact]
    public async Task DeleteCategory_SendsItsEntriesToUncategorized()
    {
        var cheese = await _actions.CreateCategoryAsync("Cheese", null, User);
        var brie = await _actions.AddAsync("brie", ShoppingList.DefaultId, User);
        await _actions.SetCategoryAsync(brie.Entry, cheese.Id, User);

        await _actions.DeleteCategoryAsync(cheese, User);

        Assert.Equal(Category.UncategorizedId, _store.Entries.Single(e => e.Id == brie.Entry.Id).CategoryId);
        Assert.Equal("Uncategorized", Assert.Single(Groups()).Category.Name);
    }

    [Fact]
    public async Task MoveCategory_SwapsWithItsNeighbour_AndStopsAtTheEnds()
    {
        var produce = _store.Categories.Single(c => c.Id == Produce);
        var bakery = _store.Categories.Single(c => c.Id == Bakery);

        await _actions.MoveCategoryAsync(bakery, up: true, User);
        Assert.True(bakery.SortOrder < produce.SortOrder);

        await _actions.MoveCategoryAsync(bakery, up: true, User);
        Assert.True(bakery.SortOrder < produce.SortOrder);
    }

    [Fact]
    public async Task NewStore_StartsFromTheDefaultOrder()
    {
        var costco = await _actions.CreateStoreAsync("Costco", User);

        var orders = _store.StoreCategoryOrders.Where(o => o.StoreId == costco.Id).ToList();

        Assert.Equal(_store.Categories.Count, orders.Count);
        Assert.Equal(_store.Categories.Single(c => c.Id == Produce).SortOrder,
            orders.Single(o => o.CategoryId == Produce).SortOrder);
    }
}
