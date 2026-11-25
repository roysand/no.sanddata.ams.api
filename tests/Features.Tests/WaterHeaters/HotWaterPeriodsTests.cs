using Features.WaterHeaters.Queries;
using Features.WaterHeaters.Services;

namespace Features.Tests.WaterHeaters;

/// <summary>The Oslo periods of the hot water view and their parts, in UTC (spec 020, research R7).</summary>
public class HotWaterPeriodsTests
{
    private static DateTime Utc(int y, int m, int d, int h = 0) => new(y, m, d, h, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void ADay_IsOsloMidnightToOsloMidnight() =>
        Assert.Equal(
            (Utc(2026, 11, 20, 23), Utc(2026, 11, 21, 23)),
            HotWaterPeriods.Bounds(HotWaterPeriod.Day, new DateOnly(2026, 11, 21)));

    [Fact]
    public void AWeek_RunsFromMondayToMonday_AcrossTheYear() =>
        Assert.Equal(
            (Utc(2026, 12, 27, 23), Utc(2027, 1, 3, 23)),
            HotWaterPeriods.Bounds(HotWaterPeriod.Week, new DateOnly(2026, 12, 31)));

    [Fact]
    public void AMonth_IsTheFirstToTheFirst() =>
        Assert.Equal(
            (Utc(2026, 1, 31, 23), Utc(2026, 2, 28, 23)),
            HotWaterPeriods.Bounds(HotWaterPeriod.Month, new DateOnly(2026, 2, 10)));

    [Fact]
    public void AYear_IsTheFirstOfJanuaryToTheNext() =>
        Assert.Equal(
            (Utc(2025, 12, 31, 23), Utc(2026, 12, 31, 23)),
            HotWaterPeriods.Bounds(HotWaterPeriod.Year, new DateOnly(2026, 6, 1)));

    [Fact]
    public void TheDayTheClocksGoForward_Has23Hours()
    {
        IReadOnlyList<DateTime> hours = HotWaterPeriods.PartStarts(HotWaterPeriod.Day, new DateOnly(2026, 3, 29));

        Assert.Equal(23, hours.Count);
        Assert.Equal(Utc(2026, 3, 28, 23), hours[0]);
        Assert.Equal(Utc(2026, 3, 29, 21), hours[^1]);
    }

    [Fact]
    public void TheDayTheClocksGoBack_Has25Hours()
    {
        IReadOnlyList<DateTime> hours = HotWaterPeriods.PartStarts(HotWaterPeriod.Day, new DateOnly(2026, 10, 25));

        Assert.Equal(25, hours.Count);
        Assert.Equal(Utc(2026, 10, 24, 22), hours[0]);
        Assert.Equal(Utc(2026, 10, 25, 22), hours[^1]);
    }

    [Fact]
    public void AWeek_HasSevenOsloMidnights_AcrossTheClockChange()
    {
        IReadOnlyList<DateTime> days = HotWaterPeriods.PartStarts(HotWaterPeriod.Week, new DateOnly(2026, 10, 25));

        Assert.Equal(
            [
                Utc(2026, 10, 18, 22), Utc(2026, 10, 19, 22), Utc(2026, 10, 20, 22), Utc(2026, 10, 21, 22),
                Utc(2026, 10, 22, 22), Utc(2026, 10, 23, 22), Utc(2026, 10, 24, 22)
            ],
            days);
        // The last day holds the change: it ends at the Monday midnight in winter time, 25 hours later.
        Assert.Equal(
            Utc(2026, 10, 25, 23),
            HotWaterPeriods.Bounds(HotWaterPeriod.Week, new DateOnly(2026, 10, 25)).ToUtc);
    }

    [Fact]
    public void November_Has30Days()
    {
        IReadOnlyList<DateTime> days = HotWaterPeriods.PartStarts(HotWaterPeriod.Month, new DateOnly(2026, 11, 21));

        Assert.Equal(30, days.Count);
        Assert.Equal(Utc(2026, 10, 31, 23), days[0]);
        Assert.Equal(Utc(2026, 11, 29, 23), days[^1]);
    }

    [Fact]
    public void AYear_Has12OsloFirstsOfTheMonth()
    {
        IReadOnlyList<DateTime> months = HotWaterPeriods.PartStarts(HotWaterPeriod.Year, new DateOnly(2026, 6, 1));

        Assert.Equal(12, months.Count);
        Assert.Equal(Utc(2025, 12, 31, 23), months[0]);
        Assert.Equal(Utc(2026, 3, 31, 22), months[3]);
        Assert.Equal(Utc(2026, 11, 30, 23), months[^1]);
    }

    [Fact]
    public void HalfPastMidnightInOslo_IsAlreadyTheNextDay() =>
        Assert.Equal(
            new DateOnly(2026, 11, 21),
            HotWaterPeriods.TodayInOslo(new DateTime(2026, 11, 20, 23, 30, 0, DateTimeKind.Utc)));
}
