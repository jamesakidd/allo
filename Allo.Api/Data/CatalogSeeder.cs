using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Allo.Shared.Catalog;
using Allo.Shared.Models;
using Microsoft.EntityFrameworkCore;

namespace Allo.Api.Data;

// Loads Seed/catalog.json into Items at startup. Only inserts what's missing: an item
// that already exists (by id or by name, deleted or not) is never touched, so user edits
// and deletions stick and new seed items still reach existing installs.
public static class CatalogSeeder
{
    // Namespace for name-based (v5) item ids. Never change it: ids must stay stable.
    private static readonly Guid SeedNamespace = new("5f0e6a4c-2b7d-4c1e-9a53-1d8e7c2b9f40");

    public sealed record SeedItem(string Name, string Category, string? Unit, List<string>? Aliases);

    public static async Task SeedAsync(AppDbContext db, DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        var existingIds = await db.Items.Select(i => i.Id).ToHashSetAsync(cancellationToken);
        var existingNames = await db.Items.Select(i => i.NormalizedName).ToHashSetAsync(cancellationToken);
        var deletedCategories = await db.Categories.Where(c => c.IsDeleted).Select(c => c.Id)
            .ToHashSetAsync(cancellationToken);

        foreach (var seed in Load())
        {
            var item = ToItem(seed);
            if (existingIds.Contains(item.Id) || !existingNames.Add(item.NormalizedName))
            {
                continue;
            }
            if (deletedCategories.Contains(item.DefaultCategoryId))
            {
                item.DefaultCategoryId = Category.UncategorizedId;
            }
            item.UpdatedAt = now;
            db.Items.Add(item);
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    public static IReadOnlyList<SeedItem> Load()
    {
        using var stream = typeof(CatalogSeeder).Assembly.GetManifestResourceStream("Allo.Api.Data.Seed.catalog.json")
            ?? throw new InvalidOperationException("Embedded catalog.json not found.");
        return JsonSerializer.Deserialize<List<SeedItem>>(stream, JsonSerializerOptions.Web)
            ?? throw new InvalidOperationException("catalog.json is empty.");
    }

    public static Item ToItem(SeedItem seed)
    {
        var normalized = CatalogText.Normalize(seed.Name);
        return new Item
        {
            Id = NameBasedId(normalized),
            Name = seed.Name.Trim(),
            NormalizedName = normalized,
            DefaultCategoryId = SeedData.CategoryIdsByName.TryGetValue(seed.Category, out var categoryId)
                ? categoryId
                : throw new InvalidOperationException($"Seed item '{seed.Name}' has unknown category '{seed.Category}'."),
            DefaultUnit = seed.Unit is null ? Unit.Each : Units.FromCode(seed.Unit),
            Aliases = (seed.Aliases ?? []).Select(CatalogText.Normalize).ToList(),
        };
    }

    public static Guid NameBasedId(string name) => NameBasedId(SeedNamespace, name);

    // RFC 4122 version 5 (SHA-1, name-based) UUID.
    public static Guid NameBasedId(Guid namespaceId, string name)
    {
        var namespaceBytes = namespaceId.ToByteArray(bigEndian: true);
        var nameBytes = Encoding.UTF8.GetBytes(name);
        var hash = SHA1.HashData([.. namespaceBytes, .. nameBytes]);

        var bytes = hash[..16];
        bytes[6] = (byte)((bytes[6] & 0x0F) | 0x50);
        bytes[8] = (byte)((bytes[8] & 0x3F) | 0x80);
        return new Guid(bytes, bigEndian: true);
    }
}
