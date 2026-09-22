using Allo.Api.Data;
using Allo.Shared.Catalog;
using Allo.Shared.Models;

namespace Allo.Tests;

public class CatalogMatcherTests
{
    private static readonly List<Item> Catalog = CatalogSeeder.Load().Select(CatalogSeeder.ToItem).ToList();
    private static readonly CatalogMatcher Matcher = new(Catalog);

    [Theory]
    [InlineData("milk", "milk")]
    [InlineData("  Milk ", "milk")]
    [InlineData("grape", "grapes")]
    [InlineData("banana", "bananas")]
    [InlineData("cookie", "cookies")]
    [InlineData("strawberry", "strawberries")]
    [InlineData("tomato", "tomatoes")]
    [InlineData("pop", "soft drinks")]
    [InlineData("KD", "macaroni and cheese")]
    [InlineData("peppers", "bell peppers")]
    [InlineData("pepper", "pepper")]
    [InlineData("hamburger buns", "hamburger buns")]
    public void Pass1_Exact(string typed, string expected)
    {
        var match = Matcher.Match(typed);

        Assert.Equal(MatchKind.Exact, match.Kind);
        Assert.Equal(expected, match.Item!.Name);
    }

    [Theory]
    [InlineData("rosemary sourdough bread", "sourdough bread", "Bakery")]
    [InlineData("bread flour", "flour", "Pantry")]
    [InlineData("peanut butter cookies", "cookies", "Snacks")]
    [InlineData("organic strawberries", "strawberries", "Produce")]
    [InlineData("chocolate almond milk", "almond milk", "Dairy")]
    [InlineData("homo milk", "milk", "Dairy")]
    [InlineData("free range eggs", "eggs", "Dairy")]
    public void Pass2_SuggestsFromContainedItem(string typed, string from, string category)
    {
        var match = Matcher.Match(typed);

        Assert.Equal(MatchKind.Suggested, match.Kind);
        Assert.Equal(from, match.Item!.Name);
        Assert.Equal(SeedData.CategoryIdsByName[category], match.CategoryId);
    }

    [Fact]
    public void Pass2_CarriesTheDefaultUnit()
    {
        var match = Matcher.Match("extra lean ground beef");

        Assert.Equal(MatchKind.Suggested, match.Kind);
        Assert.Equal(Unit.Pound, match.Unit);
    }

    [Theory]
    [InlineData("xyzzy")]
    [InlineData("")]
    [InlineData("   ")]
    public void Pass3_NoMatch_IsUncategorizedEach(string typed)
    {
        var match = Matcher.Match(typed);

        Assert.Equal(MatchKind.None, match.Kind);
        Assert.Null(match.Item);
        Assert.Equal(Category.UncategorizedId, match.CategoryId);
        Assert.Equal(Unit.Each, match.Unit);
    }

    [Fact]
    public void DeletedItems_AreIgnored()
    {
        var item = new Item { Id = Guid.NewGuid(), Name = "quark", IsDeleted = true };

        Assert.Equal(MatchKind.None, new CatalogMatcher([item]).Match("quark").Kind);
    }

    [Fact]
    public void SameName_PrefersTheMoreUsedItem()
    {
        var a = new Item { Id = Guid.NewGuid(), Name = "oat milk", UseCount = 1 };
        var b = new Item { Id = Guid.NewGuid(), Name = "Oat Milk", UseCount = 5 };

        Assert.Equal(b, new CatalogMatcher([a, b]).Match("oat milk").Item);
    }
}
