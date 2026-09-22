using Allo.Shared.Catalog;
using Allo.Shared.Models;

namespace Allo.Tests;

public class CatalogAutocompleteTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 22, 12, 0, 0, TimeSpan.Zero);

    private static Item NewItem(string name, int useCount = 0, int? daysAgo = null, params string[] aliases) => new()
    {
        Id = Guid.NewGuid(), Name = name, NormalizedName = name, UseCount = useCount,
        LastUsedAt = daysAgo is { } d ? Now.AddDays(-d) : null, Aliases = [.. aliases],
    };

    [Fact]
    public void Staples_RankAboveUnusedMatches()
    {
        var mint = NewItem("mint");
        var milk = NewItem("milk", useCount: 12, daysAgo: 3);

        var results = CatalogAutocomplete.Search([mint, milk], "mi", Now);

        Assert.Equal([milk, mint], results);
    }

    [Fact]
    public void RecentUse_BeatsAnOldSlightlyHigherCount()
    {
        var old = NewItem("mustard", useCount: 3, daysAgo: 200);
        var recent = NewItem("mushrooms", useCount: 2, daysAgo: 1);

        var results = CatalogAutocomplete.Search([old, recent], "mu", Now);

        Assert.Equal([recent, old], results);
    }

    [Fact]
    public void AmongUnused_NamePrefix_ThenAlias_ThenLaterWord()
    {
        var laterWord = NewItem("almond milk");
        var alias = NewItem("whipping cream", aliases: "heavy cream");
        var name = NewItem("milk");

        var nameResults = CatalogAutocomplete.Search([laterWord, name], "mil", Now);
        var aliasResults = CatalogAutocomplete.Search([laterWord, alias], "heav", Now);

        Assert.Equal([name, laterWord], nameResults);
        Assert.Equal([alias], aliasResults);
    }

    [Fact]
    public void EmptyText_DeletedItems_AndLimit()
    {
        var items = Enumerable.Range(0, 20).Select(i => NewItem($"item {i}")).ToList();
        items[0].IsDeleted = true;

        Assert.Empty(CatalogAutocomplete.Search(items, " ", Now));
        var results = CatalogAutocomplete.Search(items, "item", Now, limit: 5);
        Assert.Equal(5, results.Count);
        Assert.DoesNotContain(items[0], results);
    }
}
