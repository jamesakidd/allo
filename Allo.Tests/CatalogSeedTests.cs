using Allo.Api.Data;
using Allo.Shared.Catalog;
using Allo.Shared.Models;
using Microsoft.EntityFrameworkCore;

namespace Allo.Tests;

public class CatalogSeedTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 9, 22, 12, 0, 0, TimeSpan.Zero);
    private readonly TestDatabase _database = new();

    [Fact]
    public void SeedFile_IsWithinSizeTarget_AndEveryRowIsValid()
    {
        var seed = CatalogSeeder.Load();

        Assert.InRange(seed.Count, 400, 600);
        foreach (var row in seed)
        {
            CatalogSeeder.ToItem(row); // throws on an unknown category or unit
        }
    }

    [Fact]
    public void SeedFile_NamesAndAliasesAreUnique()
    {
        var keys = CatalogSeeder.Load().Select(CatalogSeeder.ToItem)
            .SelectMany(i => i.Aliases.Prepend(i.NormalizedName));

        var duplicates = keys.GroupBy(k => k).Where(g => g.Count() > 1).Select(g => g.Key).ToList();

        Assert.Empty(duplicates);
    }

    [Fact]
    public void SeedFile_EveryNameAndAlias_MatchesItsOwnItem()
    {
        var items = CatalogSeeder.Load().Select(CatalogSeeder.ToItem).ToList();
        var matcher = new CatalogMatcher(items);

        var wrong = items
            .SelectMany(i => i.Aliases.Prepend(i.Name).Select(text => (Text: text, Item: i)))
            .Where(p => matcher.Match(p.Text) is not { Kind: MatchKind.Exact } m || m.Item != p.Item)
            .Select(p => p.Text)
            .ToList();

        Assert.Empty(wrong);
    }

    [Theory]
    [InlineData("bananas", Unit.Kilogram)]
    [InlineData("eggs", Unit.Dozen)]
    [InlineData("ground beef", Unit.Pound)]
    [InlineData("sliced ham", Unit.Gram)]
    [InlineData("kale", Unit.Bunch)]
    [InlineData("milk", Unit.Each)]
    public void SeedFile_DefaultUnits(string name, Unit unit)
    {
        var row = CatalogSeeder.Load().Single(r => r.Name == name);

        Assert.Equal(unit, CatalogSeeder.ToItem(row).DefaultUnit);
    }

    [Fact]
    public void NameBasedId_IsStandardUuidV5()
    {
        var dnsNamespace = new Guid("6ba7b810-9dad-11d1-80b4-00c04fd430c8");

        // Python: uuid.uuid5(uuid.NAMESPACE_DNS, "python.org")
        Assert.Equal(new Guid("886313e1-3b8a-5372-9b90-0c9aee199e5d"),
            CatalogSeeder.NameBasedId(dnsNamespace, "python.org"));
    }

    [Fact]
    public async Task Seeder_InsertsAll_ThenNothingOnRerun()
    {
        await using (var db = _database.CreateContext())
        {
            await CatalogSeeder.SeedAsync(db, Now);
        }
        await using var check = _database.CreateContext();
        var count = await check.Items.CountAsync();
        var maxSequence = await check.Items.MaxAsync(i => i.Sequence);

        await CatalogSeeder.SeedAsync(check, Now);

        Assert.Equal(CatalogSeeder.Load().Count, count);
        Assert.Equal(count, await check.Items.CountAsync());
        Assert.Equal(maxSequence, await check.Items.MaxAsync(i => i.Sequence));
    }

    [Fact]
    public async Task Seeder_NeverOverwritesEdits_OrResurrectsDeletes()
    {
        var milkId = CatalogSeeder.NameBasedId("milk");
        var breadId = CatalogSeeder.NameBasedId("bread");
        await using (var db = _database.CreateContext())
        {
            await CatalogSeeder.SeedAsync(db, Now);
            var milk = await db.Items.SingleAsync(i => i.Id == milkId);
            milk.DefaultCategoryId = SeedData.CategoryIdsByName["Beverages"];
            var bread = await db.Items.SingleAsync(i => i.Id == breadId);
            bread.IsDeleted = true;
            await db.SaveChangesAsync();
        }

        await using var check = _database.CreateContext();
        await CatalogSeeder.SeedAsync(check, Now);

        Assert.Equal(SeedData.CategoryIdsByName["Beverages"],
            (await check.Items.SingleAsync(i => i.Id == milkId)).DefaultCategoryId);
        Assert.True((await check.Items.SingleAsync(i => i.Id == breadId)).IsDeleted);
    }

    [Fact]
    public async Task Seeder_SkipsNames_TheUserAlreadyCreated()
    {
        await using (var db = _database.CreateContext())
        {
            db.Items.Add(new Item
            {
                Id = Guid.NewGuid(), Name = "Milk", NormalizedName = "milk",
                DefaultCategoryId = Category.UncategorizedId,
            });
            await db.SaveChangesAsync();
        }

        await using var check = _database.CreateContext();
        await CatalogSeeder.SeedAsync(check, Now);

        Assert.Equal(1, await check.Items.CountAsync(i => i.NormalizedName == "milk"));
    }

    public void Dispose() => _database.Dispose();
}
