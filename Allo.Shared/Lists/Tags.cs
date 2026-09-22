using Allo.Shared.Models;
using Allo.Shared.Sync;

namespace Allo.Shared.Lists;

// What was typed: the item name, with any #tags pulled out.
public sealed record TypedEntry(string Text, IReadOnlyList<string> Tags);

public static class Tags
{
    // Offered before anything has been tagged, so the first tag isn't a blank page.
    public static readonly IReadOnlyList<string> Suggested =
        ["if on sale", "urgent", "next trip", "bulk", "check price", "optional", "exact brand"];

    // Lowercase for matching and storage, like item names. "#If On Sale" and "if on sale"
    // are the same tag.
    public static string Normalize(string tag) =>
        string.Join(' ', tag.Trim().TrimStart('#').ToLowerInvariant()
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    public static List<string> Normalize(IEnumerable<string> tags) =>
        tags.Select(Normalize).Where(t => t.Length > 0).Distinct().ToList();

    public static bool SameSet(IEnumerable<string> a, IEnumerable<string> b) =>
        Normalize(a).OrderBy(t => t).SequenceEqual(Normalize(b).OrderBy(t => t));

    // "ribeye #sale" → ribeye, tagged sale. A #tag can be anywhere in the text; a lone
    // "#" is just text. Multi-word tags are typed with the tag picker, not here.
    public static TypedEntry Parse(string text)
    {
        var words = text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        var name = new List<string>();
        var tags = new List<string>();
        foreach (var word in words)
        {
            var tag = word.Length > 1 && word[0] == '#' ? Normalize(word) : null;
            if (tag is { Length: > 0 })
            {
                tags.Add(tag);
            }
            else
            {
                name.Add(word);
            }
        }
        return new TypedEntry(string.Join(' ', name), Normalize(tags));
    }

    // Tags in use on a list, most used first, then the suggested ones that aren't.
    public static IReadOnlyList<string> Known(LocalStore store, Guid? listId = null)
    {
        var used = store.Entries
            .Where(e => !e.IsDeleted && (listId is null || e.ListId == listId))
            .SelectMany(e => e.Tags)
            .GroupBy(Normalize)
            .OrderByDescending(g => g.Count())
            .ThenBy(g => g.Key, StringComparer.Ordinal)
            .Select(g => g.Key)
            .ToList();
        return [.. used, .. Suggested.Where(t => !used.Contains(t))];
    }

    // Tags in use, for the filter row. Nothing tagged means no filter row at all.
    public static IReadOnlyList<string> InUse(LocalStore store, Guid listId, Guid? storeId) =>
        ListView.Entries(store, listId, storeId)
            .SelectMany(e => e.Tags)
            .Select(Normalize)
            .Distinct()
            .OrderBy(t => t, StringComparer.Ordinal)
            .ToList();

    public static bool Has(ListEntry entry, string tag) =>
        entry.Tags.Any(t => Normalize(t) == Normalize(tag));
}
