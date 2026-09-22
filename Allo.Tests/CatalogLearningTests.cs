using Allo.Shared.Catalog;
using Allo.Shared.Models;

namespace Allo.Tests;

public class CatalogLearningTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 22, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid User = Guid.NewGuid();
    private static readonly Guid Dairy = Guid.NewGuid();
    private static readonly Guid Beverages = Guid.NewGuid();

    private static Item Milk() => new()
    {
        Id = Guid.NewGuid(), Name = "milk", NormalizedName = "milk",
        DefaultCategoryId = Dairy, DefaultUnit = Unit.Each, UseCount = 4,
    };

    private static CatalogMatch Exact(Item item) =>
        new(MatchKind.Exact, item, item.DefaultCategoryId, item.DefaultUnit);

    [Fact]
    public void ExactMatch_UpdatesUsage_OnTheSameItem()
    {
        var milk = Milk();

        var result = CatalogLearning.RecordAdd(Exact(milk), "milk", Dairy, Unit.Each, User, Now);

        Assert.Same(milk, result);
        Assert.Equal(5, milk.UseCount);
        Assert.Equal(Now, milk.LastUsedAt);
        Assert.Equal(User, milk.UpdatedBy);
    }

    [Fact]
    public void ExactMatch_CategoryCorrection_AppliesImmediately()
    {
        var milk = Milk();

        CatalogLearning.RecordAdd(Exact(milk), "milk", Beverages, Unit.Each, User, Now);

        Assert.Equal(Beverages, milk.DefaultCategoryId);
    }

    [Fact]
    public void Unit_BecomesDefault_OnlyAfterTwoAddsInARow()
    {
        var milk = Milk();

        CatalogLearning.RecordAdd(Exact(milk), "milk", Dairy, Unit.Litre, User, Now);
        Assert.Equal(Unit.Each, milk.DefaultUnit);
        Assert.Equal(Unit.Litre, milk.PendingUnit);

        CatalogLearning.RecordAdd(Exact(milk), "milk", Dairy, Unit.Litre, User, Now);
        Assert.Equal(Unit.Litre, milk.DefaultUnit);
        Assert.Null(milk.PendingUnit);
    }

    [Fact]
    public void Unit_PendingResets_WhenTheDefaultIsUsedInBetween()
    {
        var milk = Milk();

        CatalogLearning.RecordAdd(Exact(milk), "milk", Dairy, Unit.Litre, User, Now);
        CatalogLearning.RecordAdd(Exact(milk), "milk", Dairy, Unit.Each, User, Now);
        CatalogLearning.RecordAdd(Exact(milk), "milk", Dairy, Unit.Litre, User, Now);

        Assert.Equal(Unit.Each, milk.DefaultUnit);
        Assert.Equal(Unit.Litre, milk.PendingUnit);
    }

    [Fact]
    public void SuggestedOrNone_CreatesANewItem_WithWhatTheUserChose()
    {
        var bread = new Item { Id = Guid.NewGuid(), Name = "bread", UseCount = 9 };
        var suggestion = new CatalogMatch(MatchKind.Suggested, bread, Guid.NewGuid(), Unit.Each);

        var result = CatalogLearning.RecordAdd(suggestion, "  Rosemary   Sourdough ", Dairy, Unit.Each, User, Now);

        Assert.NotSame(bread, result);
        Assert.Equal(9, bread.UseCount);
        Assert.Equal("Rosemary Sourdough", result.Name);
        Assert.Equal("rosemary sourdough", result.NormalizedName);
        Assert.Equal(Dairy, result.DefaultCategoryId);
        Assert.Equal(1, result.UseCount);
    }

    [Fact]
    public void NewItem_RequiresAName()
    {
        var none = new CatalogMatch(MatchKind.None, null, Category.UncategorizedId, Unit.Each);

        Assert.Throws<ArgumentException>(() =>
            CatalogLearning.RecordAdd(none, "   ", Dairy, Unit.Each, User, Now));
    }
}
