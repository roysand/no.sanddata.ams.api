using Application.EvChargers;

namespace Features.Tests.EvChargers;

public class EvChargerShareTests
{
    private static readonly DateTime H0 = new(2026, 11, 19, 19, 0, 0, DateTimeKind.Utc);

    private static EvChargerHour Hour(int offset, decimal kwh, bool estimated = false, bool included = true) =>
        new(H0.AddHours(offset), kwh, estimated, included);

    [Fact]
    public void NoParts_IsNull() => Assert.Null(EvChargerShare.Sum([null, null], []));

    [Fact]
    public void Sum_AddsTheHours_AndGivesTheFirstAndLastHour()
    {
        EvChargerTotal? total = EvChargerShare.Sum([Hour(1, 4m), null, Hour(0, 1.5m)], [Hour(3, 2m, estimated: true)]);

        Assert.Equal(new EvChargerTotal(7.5m, true, true, 1, H0, H0.AddHours(3)), total);
    }

    [Fact]
    public void Sum_OfHoursWithoutAMeterReadingOnly_IsNotNull()
    {
        EvChargerTotal? total = EvChargerShare.Sum([], [Hour(0, 2m, included: false)]);

        Assert.Equal(2m, total!.Kwh);
        Assert.False(total.Included);
        Assert.Equal(1, total.HoursWithoutMeterReading);
    }

    [Theory]
    [InlineData(true, 4.5)]
    [InlineData(false, 1.5)]
    public void TotalKwh_AddsTheChargerOnlyWhenIncluded(bool included, double expected)
    {
        Assert.Equal((decimal)expected, EvChargerShare.TotalKwh(1.5m, Hour(0, 3m, included: included)));
        Assert.Equal(1.5m, EvChargerShare.TotalKwh(1.5m, (EvChargerHour?)null));
        Assert.Equal(1.5m, EvChargerShare.TotalKwh(1.5m, (EvChargerTotal?)null));
    }
}
