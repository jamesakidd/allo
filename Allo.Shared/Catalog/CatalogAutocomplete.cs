using Allo.Shared.Models;

namespace Allo.Shared.Catalog;

public static class CatalogAutocomplete
{
    // How long a recent add keeps boosting an item, so something bought last week ranks
    // above a staple from months ago with only a slightly higher count.
    private const double RecencyBoost = 5;
    private const double RecencyHalfLifeDays = 14;

    // Items whose name or alias starts with the typed text, or has a word that does.
    // Staples first (use count plus a fading recency boost), then match quality, then name.
    public static IReadOnlyList<Item> Search(IEnumerable<Item> catalog, string text, DateTimeOffset now, int limit = 8)
    {
        var prefix = CatalogText.Normalize(text);
        if (prefix.Length == 0)
        {
            return [];
        }

        return catalog
            .Where(i => !i.IsDeleted)
            .Select(i => (Item: i, Quality: MatchQuality(i, prefix)))
            .Where(m => m.Quality >= 0)
            .OrderByDescending(m => UsageScore(m.Item, now))
            .ThenBy(m => m.Quality)
            .ThenBy(m => m.Item.Name.Length)
            .ThenBy(m => m.Item.Name, StringComparer.OrdinalIgnoreCase)
            .Take(limit)
            .Select(m => m.Item)
            .ToList();
    }

    public static double UsageScore(Item item, DateTimeOffset now)
    {
        if (item.LastUsedAt is not { } lastUsed)
        {
            return item.UseCount;
        }
        var days = Math.Max(0, (now - lastUsed).TotalDays);
        return item.UseCount + RecencyBoost * Math.Pow(0.5, days / RecencyHalfLifeDays);
    }

    // 0: name starts with it, 1: an alias does, 2: a later word does, -1: no match.
    private static int MatchQuality(Item item, string prefix)
    {
        var name = CatalogText.Normalize(item.Name);
        if (name.StartsWith(prefix, StringComparison.Ordinal))
        {
            return 0;
        }
        var aliases = item.Aliases.Select(CatalogText.Normalize).ToList();
        if (aliases.Any(a => a.StartsWith(prefix, StringComparison.Ordinal)))
        {
            return 1;
        }
        return aliases.Prepend(name).Any(t => HasWordStartingWith(t, prefix)) ? 2 : -1;
    }

    private static bool HasWordStartingWith(string text, string prefix)
    {
        for (var i = text.IndexOf(' '); i >= 0; i = text.IndexOf(' ', i + 1))
        {
            if (string.CompareOrdinal(text, i + 1, prefix, 0, prefix.Length) == 0)
            {
                return true;
            }
        }
        return false;
    }
}
