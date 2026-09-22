using Allo.Api.Data;
using Allo.Shared.Lists;
using Allo.Shared.Models;
using Allo.Shared.Sync;

namespace Allo.Tests;

public class TagTests
{
    private static readonly Guid User = Guid.NewGuid();
    private static readonly Guid Dairy = SeedData.CategoryIdsByName["Dairy"];

    private readonly LocalStore _store = new(new InMemoryStorage(), TimeProvider.System);
    private readonly ListActions _actions;

    public TagTests()
    {
        _actions = new ListActions(_store, TimeProvider.System);
        _store.ApplyPullAsync(new SyncPullResponse
        {
            Cursor = 1,
            Rows = new SyncRows
            {
                Categories =
                [
                    new Category { Id = Dairy, Name = "Dairy", SortOrder = 30, Sequence = 1 },
                    new Category { Id = SeedData.CategoryIdsByName["Produce"], Name = "Produce", SortOrder = 10, Sequence = 1 },
                    new Category { Id = Category.UncategorizedId, Name = "Uncategorized", SortOrder = int.MaxValue, Sequence = 1 },
                ],
                Lists = [new ShoppingList { Id = ShoppingList.DefaultId, Name = "Groceries", Sequence = 1 }],
                Items = [.. CatalogSeeder.Load().Select(CatalogSeeder.ToItem)],
            },
        }).GetAwaiter().GetResult();
    }

    private Task<AddOutcome> AddAsync(string text) => _actions.AddAsync(text, ShoppingList.DefaultId, User);

    [Theory]
    [InlineData("ribeye #sale", "ribeye", new[] { "sale" })]
    [InlineData("#urgent milk", "milk", new[] { "urgent" })]
    [InlineData("ground beef #sale #bulk", "ground beef", new[] { "sale", "bulk" })]
    [InlineData("milk", "milk", new string[0])]
    [InlineData("aisle # 7 milk", "aisle # 7 milk", new string[0])]
    [InlineData("milk #SALE", "milk", new[] { "sale" })]
    [InlineData("milk #sale #sale", "milk", new[] { "sale" })]
    public void Parse_PullsTagsOutOfTypedText(string typed, string name, string[] tags)
    {
        var parsed = Tags.Parse(typed);

        Assert.Equal(name, parsed.Text);
        Assert.Equal(tags, parsed.Tags);
    }

    [Fact]
    public async Task Add_AppliesTypedTags_ToTheEntry()
    {
        var outcome = await AddAsync("milk #urgent");

        Assert.Equal(["urgent"], outcome.Entry.Tags);
        Assert.Equal("milk", _store.Items.Single(i => i.Id == outcome.Item.Id).Name);
    }

    [Fact]
    public async Task Add_MergingIntoAnExistingRow_KeepsBothSetsOfTags()
    {
        await AddAsync("milk #urgent");

        var second = await AddAsync("milk #sale");

        Assert.True(second.MergedWithExisting);
        Assert.Equal(["urgent", "sale"], second.Entry.Tags);
    }

    [Fact]
    public async Task SameTagsTwiceInARow_BecomeTheItemsDefaults()
    {
        await AddAsync("bananas #bulk");
        var item = _store.Items.Single(i => i.NormalizedName == "bananas");
        Assert.Empty(item.DefaultTags);
        Assert.Equal(["bulk"], item.PendingTags);

        await AddAsync("bananas #bulk");

        Assert.Equal(["bulk"], item.DefaultTags);
        Assert.Empty(item.PendingTags);
    }

    [Fact]
    public async Task DefaultTags_ComeAlongOnTheNextAdd_AndDontClearThemselves()
    {
        await AddAsync("bananas #bulk");
        await AddAsync("bananas #bulk");
        var item = _store.Items.Single(i => i.NormalizedName == "bananas");

        var outcome = await AddAsync("bananas");

        Assert.Equal(["bulk"], outcome.Entry.Tags);
        Assert.Equal(["bulk"], item.DefaultTags);
    }

    [Fact]
    public async Task ADifferentTagEachTime_NeverBecomesADefault()
    {
        await AddAsync("eggs #urgent");
        await AddAsync("eggs #bulk");
        await AddAsync("eggs #next trip");

        Assert.Empty(_store.Items.Single(i => i.NormalizedName == "eggs").DefaultTags);
    }

    [Fact]
    public async Task Filtering_ShowsOnlyThatTag_InBothSections()
    {
        var urgent = await AddAsync("milk #urgent");
        var plain = await AddAsync("bread");
        var checkedUrgent = await AddAsync("bananas #urgent");
        await _actions.SetCheckedAsync(checkedUrgent.Entry, true, User);

        var filtered = ListView.Build(_store, ShoppingList.DefaultId, null, "urgent");

        Assert.Equal([urgent.Entry.Id], filtered.ToBuy.SelectMany(g => g.Entries).Select(e => e.Id));
        Assert.Equal([checkedUrgent.Entry.Id], filtered.Checked.SelectMany(g => g.Entries).Select(e => e.Id));
        Assert.DoesNotContain(filtered.ToBuy.SelectMany(g => g.Entries), e => e.Id == plain.Entry.Id);
    }

    [Fact]
    public async Task Tags_NeverChangeTheOrder()
    {
        await AddAsync("bananas #urgent");
        await AddAsync("milk");

        var groups = ListView.Build(_store, ShoppingList.DefaultId, null).ToBuy;

        // Produce before Dairy, as the category order says: the tag changes nothing.
        Assert.Equal(["Produce", "Dairy"], groups.Select(g => g.Category.Name));
    }

    [Fact]
    public async Task KnownTags_AreUsedOnesFirst_ThenSuggestions()
    {
        await AddAsync("milk #urgent");
        await AddAsync("bread #urgent");
        await AddAsync("eggs #bulk");

        var known = Tags.Known(_store);

        Assert.Equal("urgent", known[0]);
        Assert.Equal("bulk", known[1]);
        Assert.Contains("if on sale", known);
        Assert.Equal(known.Count, known.Distinct().Count());
    }

    [Fact]
    public async Task InUse_IsEmpty_UntilSomethingIsTagged()
    {
        await AddAsync("milk");
        Assert.Empty(Tags.InUse(_store, ShoppingList.DefaultId, null));

        await AddAsync("bread #urgent");

        Assert.Equal(["urgent"], Tags.InUse(_store, ShoppingList.DefaultId, null));
    }
}
