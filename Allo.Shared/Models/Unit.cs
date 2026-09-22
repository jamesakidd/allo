using System.Text.Json.Serialization;

namespace Allo.Shared.Models;

// Stored and sent as the lowercase code, never the integer. Add new members at the end.
[JsonConverter(typeof(JsonStringEnumConverter<Unit>))]
public enum Unit
{
    [JsonStringEnumMemberName("ea")] Each,
    [JsonStringEnumMemberName("g")] Gram,
    [JsonStringEnumMemberName("kg")] Kilogram,
    [JsonStringEnumMemberName("lb")] Pound,
    [JsonStringEnumMemberName("ml")] Millilitre,
    [JsonStringEnumMemberName("l")] Litre,
    [JsonStringEnumMemberName("gal")] Gallon,
    [JsonStringEnumMemberName("bunch")] Bunch,
    [JsonStringEnumMemberName("dozen")] Dozen,
}

public static class Units
{
    private static readonly Dictionary<Unit, string> Codes = new()
    {
        [Unit.Each] = "ea",
        [Unit.Gram] = "g",
        [Unit.Kilogram] = "kg",
        [Unit.Pound] = "lb",
        [Unit.Millilitre] = "ml",
        [Unit.Litre] = "l",
        [Unit.Gallon] = "gal",
        [Unit.Bunch] = "bunch",
        [Unit.Dozen] = "dozen",
    };

    private static readonly Dictionary<string, Unit> ByCode =
        Codes.ToDictionary(kv => kv.Value, kv => kv.Key, StringComparer.OrdinalIgnoreCase);

    public static IReadOnlyCollection<Unit> All => Codes.Keys;

    public static string ToCode(this Unit unit) => Codes[unit];

    public static Unit FromCode(string code) =>
        ByCode.TryGetValue(code.Trim(), out var unit)
            ? unit
            : throw new ArgumentException($"Unknown unit code '{code}'.", nameof(code));

    // Count units take whole-number quantities; measure units allow decimals.
    public static bool IsCountUnit(this Unit unit) =>
        unit is Unit.Each or Unit.Bunch or Unit.Dozen;
}
