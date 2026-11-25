using Application.WaterHeaters;
using Features.WaterHeaters.Services;

namespace Features.Tests.WaterHeaters;

/// <summary>The hourly energy balance behind the hot water used (spec 020, research R6).</summary>
public class HotWaterBalanceTests
{
    private static readonly HotWaterRuleValues Rule = new(0.04m, 3);
    private static readonly DateTime H8 = new(2026, 11, 25, 8, 0, 0, DateTimeKind.Utc);

    private static WaterHeaterLitresHour L(DateTime hour, int first, int last) => new(hour, first, last);

    private static WaterHeaterHourEnergy E(DateTime hour, decimal kwh, decimal seconds = 3600m) =>
        new(hour, kwh, seconds);

    private static IReadOnlyList<WaterHeaterHotWaterHour> Count(
        IReadOnlyList<WaterHeaterLitresHour> litres, IReadOnlyList<WaterHeaterHourEnergy> energy, DateTime from) =>
        HotWaterBalance.Count(litres, energy, from, Rule);

    /// <summary>
    /// Roy's samples of 2026-11-25 (Oslo 09, 10, 17 and 18). The kWh are the hours' mean power. The OSO app's Day view
    /// showed about 50, 22, 26 and 8 L, 106 L for the day.
    /// </summary>
    [Fact]
    public void RoysFourHours_MatchTheOsoApp()
    {
        DateTime h16 = H8.AddHours(8);
        IReadOnlyList<WaterHeaterHotWaterHour> morning = Count(
            [L(H8.AddHours(-1), 358, 358), L(H8, 358, 320), L(H8.AddHours(1), 320, 348)],
            [E(H8, 0.4765m), E(H8.AddHours(1), 1.9990m)],
            H8);
        IReadOnlyList<WaterHeaterHotWaterHour> evening = Count(
            [L(h16.AddHours(-1), 344, 344), L(h16, 344, 339), L(h16.AddHours(1), 339, 361)],
            [E(h16, 0.9678m), E(h16.AddHours(1), 1.1570m)],
            h16);

        int[] litres = [.. morning.Concat(evening).Select(h => (int)Math.Round(h.Litres))];

        // Shower and reheating: 38 L fell and 0.48 kWh heated → 50; the litres rose 28 while 2 kWh heated → 22.
        Assert.Equal([50, 22, 29, 7], litres);
        Assert.InRange(litres.Sum(), 100, 112);
    }

    [Fact]
    public void WaterDrawnWhileHeating_IsCountedThoughTheLitresRose()
    {
        // The tank gained 10 L, but 1.2 kWh make 30 L: 20 L were drawn.
        WaterHeaterHotWaterHour hour =
            Assert.Single(Count([L(H8.AddHours(-1), 300, 300), L(H8, 300, 310)], [E(H8, 1.2m)], H8));

        Assert.Equal(20m, hour.Litres);
    }

    [Fact]
    public void TheStart_IsTheLastLitresOfTheHourBefore()
    {
        // The hour before ended at 350, though this hour's first sample already shows 340.
        WaterHeaterHotWaterHour hour =
            Assert.Single(Count([L(H8.AddHours(-1), 360, 350), L(H8, 340, 330)], [E(H8, 0m)], H8));

        Assert.Equal(20m, hour.Litres);
    }

    [Fact]
    public void WithoutTheHourBefore_TheHoursOwnFirstLitresAreTheStart()
    {
        WaterHeaterHotWaterHour hour = Assert.Single(Count([L(H8, 340, 330)], [E(H8, 0m)], H8));

        Assert.Equal(10m, hour.Litres);
    }

    [Fact]
    public void AGapOfAnHour_UsesTheHoursOwnFirstLitres()
    {
        // 06:00 has litres, 07:00 has none: 08:00 starts from its own first sample.
        IReadOnlyList<WaterHeaterHotWaterHour> hours =
            Count([L(H8.AddHours(-2), 400, 400), L(H8, 340, 330)], [E(H8, 0m)], H8);

        Assert.Equal(10m, Assert.Single(hours).Litres);
    }

    [Theory]
    [InlineData(358, 356, 0)] // standby cooling, 2 L: under the floor
    [InlineData(358, 355, 3)] // 3 L: counts
    [InlineData(330, 361, 0)] // more gained than heated: never negative
    public void TheFloor_CountsSmallAndNegativeHoursAsZero(int start, int end, decimal expected)
    {
        WaterHeaterHotWaterHour hour =
            Assert.Single(Count([L(H8.AddHours(-1), start, start), L(H8, start, end)], [E(H8, 0m)], H8));

        Assert.Equal(expected, hour.Litres);
    }

    [Fact]
    public void TheHourBeforeFrom_IsNotReturned()
    {
        IReadOnlyList<WaterHeaterHotWaterHour> hours = Count(
            [L(H8.AddHours(-1), 400, 300), L(H8, 300, 290)],
            [E(H8.AddHours(-1), 0m), E(H8, 0m)],
            H8);

        Assert.Equal(H8, Assert.Single(hours).HourStartUtc);
    }

    [Fact]
    public void SampledSeconds_ComeFromThePowerView()
    {
        WaterHeaterHotWaterHour hour = Assert.Single(Count([L(H8, 300, 300)], [E(H8, 0m, 1800m)], H8));

        Assert.Equal(1800m, hour.SampledSeconds);
    }

    [Fact]
    public void AnHourWithoutPower_CountsOnlyTheFall()
    {
        WaterHeaterHotWaterHour hour =
            Assert.Single(Count([L(H8.AddHours(-1), 320, 320), L(H8, 320, 310)], [], H8));

        Assert.Equal(10m, hour.Litres);
        Assert.Equal(0m, hour.SampledSeconds);
    }

    [Fact]
    public void TheCurrentRule_IsTheCalibratedOne() =>
        Assert.Equal(new HotWaterRuleValues(0.04m, 5), HotWaterRule.Current);

    /// <summary>
    /// Roy's hourly summary of 2026-11-25 with the current rule: a quiet hour's idle loss of 4 L counts 0, the morning
    /// wash with the heater running counts (Oslo 00 and 06).
    /// </summary>
    [Fact]
    public void TheCurrentRule_DropsIdleLossButKeepsTheMorningWash()
    {
        DateTime h23 = H8.AddHours(-9);
        DateTime h05 = H8.AddHours(-3);

        IReadOnlyList<WaterHeaterHotWaterHour> quiet = HotWaterBalance.Count(
            [L(h23.AddHours(-1), 359, 359), L(h23, 359, 355)], [E(h23, 0.012m)], h23, HotWaterRule.Current);
        IReadOnlyList<WaterHeaterHotWaterHour> wash = HotWaterBalance.Count(
            [L(h05.AddHours(-1), 344, 341), L(h05, 341, 337)], [E(h05, 0.481m)], h05, HotWaterRule.Current);

        Assert.Equal(0m, Assert.Single(quiet).Litres);
        Assert.Equal(16, (int)Math.Round(Assert.Single(wash).Litres));
    }
}
