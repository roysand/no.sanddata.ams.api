using Application.WaterHeaters;

namespace Features.Tests.WaterHeaters;

public class WaterHeaterShareTests
{
    private static readonly DateTime H0 = new(2026, 11, 20, 5, 0, 0, DateTimeKind.Utc);

    private static WaterHeaterHourEnergy Energy(decimal kwh, decimal seconds = 3600m, DateTime? hour = null) =>
        new(hour ?? H0, kwh, seconds);

    [Fact]
    public void TheCost_IsTheHoursCostTimesTheHeatersShareOfTheKwh()
    {
        WaterHeaterHour part = WaterHeaterShare.ForHour(Energy(2m), 4m, 4.00m);

        Assert.Equal(2m, part.Kwh);
        Assert.Equal(2.00m, part.Cost);
        Assert.False(part.Capped);
        Assert.Equal(60, part.SampledMinutes);
        Assert.Equal(H0, part.HourStartUtc);
    }

    [Fact]
    public void ANorgesprisSplitHour_SharesTheWholeCostNotOneRate()
    {
        // 3 kWh: 1 within the cap at 0.50 and 2 above it at 1.25 → 3.00 in all, an average of 1.00 per kWh.
        WaterHeaterHour part = WaterHeaterShare.ForHour(Energy(1.5m), 3m, 3.00m);

        Assert.Equal(1.50m, part.Cost);
    }

    [Fact]
    public void MoreSampledKwhThanMetered_IsCappedAtTheHour()
    {
        WaterHeaterHour part = WaterHeaterShare.ForHour(Energy(3.5m), 3.0m, 2.70m);

        Assert.Equal(3.0m, part.Kwh);
        Assert.True(part.Capped);
        Assert.Equal(2.70m, part.Cost);
    }

    [Fact]
    public void AnHourWithoutAPrice_HasKwhButNoCost()
    {
        WaterHeaterHour part = WaterHeaterShare.ForHour(Energy(1.2m), 2m, null);

        Assert.Equal(1.2m, part.Kwh);
        Assert.Null(part.Cost);
    }

    [Fact]
    public void AnHourWithZeroMeteredKwh_GivesZero()
    {
        WaterHeaterHour part = WaterHeaterShare.ForHour(Energy(0.4m), 0m, 0m);

        Assert.Equal(0m, part.Kwh);
        Assert.Equal(0m, part.Cost);
        Assert.True(part.Capped);
    }

    [Theory]
    [InlineData(3600, 60)]
    [InlineData(3599, 59)]
    [InlineData(59, 0)]
    public void SampledMinutes_AreRoundedDown(int seconds, int minutes) => Assert.Equal(minutes, WaterHeaterShare.ForHour(Energy(1m, seconds), 2m, 1m).SampledMinutes);

    [Fact]
    public void TheSum_AddsTheHoursExactly()
    {
        WaterHeaterHour?[] hours =
        [
            new WaterHeaterHour(H0, 1.111m, 0.3333m, 60, false),
            null,
            new WaterHeaterHour(H0.AddHours(2), 2.222m, 0.6667m, 45, true),
            new WaterHeaterHour(H0.AddHours(1), 0.001m, 0.0001m, 1, false),
        ];

        WaterHeaterTotal? total = WaterHeaterShare.Sum(hours);

        Assert.NotNull(total);
        Assert.Equal(3.334m, total.Kwh);
        Assert.Equal(1.0001m, total.Cost);
        Assert.Equal(106, total.SampledMinutes);
        Assert.True(total.Capped);
        Assert.Equal(H0, total.FirstHourUtc);
        Assert.Equal(H0.AddHours(2), total.LastHourUtc);
    }

    [Fact]
    public void TheSumsCost_IsNullWhenAnyHourHasNoCost()
    {
        WaterHeaterTotal? total = WaterHeaterShare.Sum(
        [
            new WaterHeaterHour(H0, 1m, 1m, 60, false),
            new WaterHeaterHour(H0.AddHours(1), 1m, null, 60, false),
        ]);

        Assert.NotNull(total);
        Assert.Equal(2m, total.Kwh);
        Assert.Null(total.Cost);
    }

    [Fact]
    public void NoParts_GiveNoSum()
    {
        Assert.Null(WaterHeaterShare.Sum([]));
        Assert.Null(WaterHeaterShare.Sum([null, null]));
    }
}
