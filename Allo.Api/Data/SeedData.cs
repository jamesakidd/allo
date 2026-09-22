using Allo.Shared.Models;
using Microsoft.EntityFrameworkCore;

namespace Allo.Api.Data;

// Seeded rows have fixed ids so every database and client agrees on them. They carry
// Sequence 1 so a client starting from cursor 0 pulls them on its first sync.
public static class SeedData
{
    private const long SeedSequence = 1;
    private static readonly DateTimeOffset SeedTime = new(2026, 9, 22, 0, 0, 0, TimeSpan.Zero);

    private static readonly (Guid Id, string Name)[] TopLevelCategories =
    [
        (new("00000000-0000-0000-0000-00000000c001"), "Produce"),
        (new("00000000-0000-0000-0000-00000000c002"), "Bakery"),
        (new("00000000-0000-0000-0000-00000000c003"), "Dairy"),
        (new("00000000-0000-0000-0000-00000000c004"), "Meat & Seafood"),
        (new("00000000-0000-0000-0000-00000000c005"), "Frozen"),
        (new("00000000-0000-0000-0000-00000000c006"), "Pantry"),
        (new("00000000-0000-0000-0000-00000000c007"), "Beverages"),
        (new("00000000-0000-0000-0000-00000000c008"), "Snacks"),
        (new("00000000-0000-0000-0000-00000000c009"), "Household"),
        (new("00000000-0000-0000-0000-00000000c00a"), "Personal Care"),
        (new("00000000-0000-0000-0000-00000000c00b"), "Baby"),
        (new("00000000-0000-0000-0000-00000000c00c"), "Pet"),
    ];

    public static void Apply(ModelBuilder modelBuilder)
    {
        var categories = TopLevelCategories
            .Select((c, i) => new Category { Id = c.Id, Name = c.Name, SortOrder = (i + 1) * 10 })
            .Append(new Category { Id = Category.UncategorizedId, Name = "Uncategorized", SortOrder = int.MaxValue })
            .ToArray();
        foreach (var category in categories)
        {
            category.Sequence = SeedSequence;
            category.UpdatedAt = SeedTime;
        }
        modelBuilder.Entity<Category>().HasData(categories);

        modelBuilder.Entity<ShoppingList>().HasData(new ShoppingList
        {
            Id = ShoppingList.DefaultId,
            Name = "Groceries",
            Sequence = SeedSequence,
            UpdatedAt = SeedTime,
        });

        modelBuilder.Entity<SyncCounter>().HasData(new SyncCounter { Id = 1, Value = SeedSequence });
    }
}
