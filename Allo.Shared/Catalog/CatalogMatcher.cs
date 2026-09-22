using Allo.Shared.Models;

namespace Allo.Shared.Catalog;

public enum MatchKind
{
    // The text is a catalog item (by name or alias): use it as-is.
    Exact,
    // The text contains a catalog item ("sourdough bread" contains "bread"). Category and
    // unit are pre-filled from it but must stay a one-tap-changeable suggestion.
    Suggested,
    // Nothing matched: Uncategorized, with the category picker pre-focused.
    None,
}

// Item: the exact match, or the item a suggestion was derived from; null for None.
public sealed record CatalogMatch(MatchKind Kind, Item? Item, Guid CategoryId, Unit Unit);

// Three-pass categorization of typed text against the catalog. Built once per catalog
// snapshot and reused per keystroke; rebuild when the catalog changes.
public sealed class CatalogMatcher
{
    private sealed record Key(Item Item, string[] Tokens, HashSet<string>[] TokenVariants, bool IsAlias);

    private readonly Dictionary<string, List<Key>> _byText = new();
    private readonly Dictionary<string, List<Key>> _byPhraseVariant = new();
    private readonly List<Key> _keys = [];

    public CatalogMatcher(IEnumerable<Item> catalog)
    {
        foreach (var item in catalog.Where(i => !i.IsDeleted))
        {
            AddKey(item, item.Name, isAlias: false);
            foreach (var alias in item.Aliases)
            {
                AddKey(item, alias, isAlias: true);
            }
        }
    }

    public CatalogMatch Match(string text)
    {
        var tokens = CatalogText.Tokenize(text);
        if (tokens.Length == 0)
        {
            return NoMatch;
        }

        // Pass 1: exact name or alias, falling back to singular/plural forms.
        if (_byText.TryGetValue(string.Join(' ', tokens), out var exact))
        {
            return Found(MatchKind.Exact, Best(exact));
        }
        var byVariant = CatalogText.PhraseVariants(tokens)
            .SelectMany(v => _byPhraseVariant.GetValueOrDefault(v) ?? [])
            .ToList();
        if (byVariant.Count > 0)
        {
            return Found(MatchKind.Exact, Best(byVariant));
        }

        // Pass 2: the longest catalog phrase contained in the text. A phrase ending on the
        // last word wins first, because that's the head noun in English: "bread flour" is
        // flour, "peanut butter cookies" is cookies.
        var inputVariants = tokens.Select(t => CatalogText.Variants(t).ToHashSet()).ToArray();
        var suggestion = _keys
            .Select(k => (Key: k, End: LastMatchEnd(k, inputVariants)))
            .Where(m => m.End >= 0)
            .OrderByDescending(m => m.End == tokens.Length - 1)
            .ThenByDescending(m => m.Key.Tokens.Length)
            .ThenBy(m => m.Key.IsAlias)
            .ThenByDescending(m => m.Key.Item.UseCount)
            .Select(m => m.Key)
            .FirstOrDefault();

        // Pass 3: nothing.
        return suggestion is null ? NoMatch : Found(MatchKind.Suggested, suggestion);
    }

    private static CatalogMatch NoMatch => new(MatchKind.None, null, Category.UncategorizedId, Unit.Each);

    private static CatalogMatch Found(MatchKind kind, Key key) =>
        new(kind, key.Item, key.Item.DefaultCategoryId, key.Item.DefaultUnit);

    // A name beats an alias; between items, the one used more often.
    private static Key Best(IEnumerable<Key> keys) => keys
        .OrderBy(k => k.IsAlias)
        .ThenByDescending(k => k.Item.UseCount)
        .ThenByDescending(k => k.Item.LastUsedAt)
        .First();

    // Index of the input token where the key's last contiguous occurrence ends, or -1.
    private static int LastMatchEnd(Key key, HashSet<string>[] input)
    {
        var m = key.Tokens.Length;
        for (var start = input.Length - m; start >= 0; start--)
        {
            var all = true;
            for (var j = 0; j < m && all; j++)
            {
                all = key.TokenVariants[j].Overlaps(input[start + j]);
            }
            if (all)
            {
                return start + m - 1;
            }
        }
        return -1;
    }

    private void AddKey(Item item, string text, bool isAlias)
    {
        var tokens = CatalogText.Tokenize(text);
        if (tokens.Length == 0)
        {
            return;
        }
        var key = new Key(item, tokens, tokens.Select(t => CatalogText.Variants(t).ToHashSet()).ToArray(), isAlias);
        _keys.Add(key);
        Index(_byText, string.Join(' ', tokens), key);
        foreach (var variant in CatalogText.PhraseVariants(tokens).Distinct())
        {
            Index(_byPhraseVariant, variant, key);
        }
    }

    private static void Index(Dictionary<string, List<Key>> index, string text, Key key)
    {
        if (!index.TryGetValue(text, out var list))
        {
            index[text] = list = [];
        }
        list.Add(key);
    }
}
