using System.Text.Json.Serialization;

namespace Allo.Shared.Models;

// Stored and sent as the lowercase code, never the integer, so adding a level later never
// renumbers existing rows. Normal is first so an unset value means Normal.
[JsonConverter(typeof(JsonStringEnumConverter<Priority>))]
public enum Priority
{
    [JsonStringEnumMemberName("normal")] Normal,
    [JsonStringEnumMemberName("high")] High,
    [JsonStringEnumMemberName("low")] Low,
}

public static class Priorities
{
    private static readonly Dictionary<Priority, string> Codes = new()
    {
        [Priority.Normal] = "normal",
        [Priority.High] = "high",
        [Priority.Low] = "low",
    };

    // What the screen sorts by, kept separate from the enum's declaration order: the
    // stored value is a string, so declaration order has to stay free to change.
    private static readonly Dictionary<Priority, int> Ranks = new()
    {
        [Priority.High] = 0,
        [Priority.Normal] = 1,
        [Priority.Low] = 2,
    };

    private static readonly Dictionary<string, Priority> ByCode =
        Codes.ToDictionary(kv => kv.Value, kv => kv.Key, StringComparer.OrdinalIgnoreCase);

    // Highest first, which is the order the screen groups in.
    public static IReadOnlyList<Priority> All { get; } =
        [.. Ranks.OrderBy(r => r.Value).Select(r => r.Key)];

    public static string ToCode(this Priority priority) => Codes[priority];

    public static int Rank(this Priority priority) => Ranks[priority];

    public static string Label(this Priority priority) => priority switch
    {
        Priority.High => "High",
        Priority.Low => "Low",
        _ => "Normal",
    };

    public static Priority FromCode(string code) =>
        ByCode.TryGetValue(code.Trim(), out var priority)
            ? priority
            : throw new ArgumentException($"Unknown priority code '{code}'.", nameof(code));
}
