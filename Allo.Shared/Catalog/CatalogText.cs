namespace Allo.Shared.Catalog;

public static class CatalogText
{
    // Lowercase, trimmed, single-spaced. Used for every name, alias and tag comparison;
    // the original casing is kept separately for display.
    public static string Normalize(string text) =>
        string.Join(' ', Tokenize(text));

    public static string[] Tokenize(string text) =>
        text.Trim().ToLowerInvariant().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);

    // Possible singular forms of a token, including itself. Deliberately loose: both sides
    // of a comparison are expanded and match if the sets overlap, so "grapes" meets
    // "grape", "berries" meets "berry" and "cookies" meets "cookie". A wrong variant
    // only matters if it happens to equal another real word.
    public static IEnumerable<string> Variants(string token)
    {
        yield return token;
        if (token.Length < 3 || !token.EndsWith('s') || token.EndsWith("ss") || token.EndsWith("us"))
        {
            yield break;
        }
        yield return token[..^1];
        if (token.EndsWith("es"))
        {
            yield return token[..^2];
        }
        if (token.EndsWith("ies"))
        {
            yield return token[..^3] + "y";
        }
    }

    // Phrase variants vary only the last token, since that's where the plural goes
    // ("cherry tomatoes", not "cherries tomato").
    public static IEnumerable<string> PhraseVariants(string[] tokens)
    {
        if (tokens.Length == 0)
        {
            yield break;
        }
        var head = string.Join(' ', tokens[..^1]);
        foreach (var last in Variants(tokens[^1]))
        {
            yield return head.Length == 0 ? last : head + " " + last;
        }
    }
}
