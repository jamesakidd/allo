using System.Text.Json;
using Allo.Shared.Models;

namespace Allo.Tests;

public class UnitTests
{
    [Theory]
    [InlineData(Unit.Each, "ea")]
    [InlineData(Unit.Kilogram, "kg")]
    [InlineData(Unit.Litre, "l")]
    [InlineData(Unit.Gallon, "gal")]
    public void Json_UsesCode_BothWays(Unit unit, string code)
    {
        var json = JsonSerializer.Serialize(unit);

        Assert.Equal($"\"{code}\"", json);
        Assert.Equal(unit, JsonSerializer.Deserialize<Unit>(json));
    }

    [Fact]
    public void EveryUnit_HasACode_ThatRoundTrips()
    {
        Assert.Equal(Enum.GetValues<Unit>().Length, Units.All.Count);
        foreach (var unit in Enum.GetValues<Unit>())
        {
            Assert.Equal(unit, Units.FromCode(unit.ToCode()));
        }
    }

    [Fact]
    public void CountUnits_AreEachBunchDozen()
    {
        var count = Enum.GetValues<Unit>().Where(u => u.IsCountUnit());

        Assert.Equal([Unit.Each, Unit.Bunch, Unit.Dozen], count);
    }
}
