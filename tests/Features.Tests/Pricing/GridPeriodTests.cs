using System.Globalization;
using Application.ElectricityCost;

namespace Features.Tests.Pricing;

public class GridPeriodTests
{
    private static DateTime Utc(string iso) =>
        DateTime.Parse(iso, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal);

    // Winter time: Oslo is UTC+1, so 05:00 Oslo is 04:00Z. Wednesday 2026-12-02.
    [Theory]
    [InlineData("2026-12-02T04:00:00Z", GridPeriod.Night)] // 05:00 Oslo
    [InlineData("2026-12-02T05:00:00Z", GridPeriod.Day)] // 06:00 Oslo
    [InlineData("2026-12-02T20:00:00Z", GridPeriod.Day)] // 21:00 Oslo
    [InlineData("2026-12-02T21:00:00Z", GridPeriod.Night)] // 22:00 Oslo
    public void OnAWeekdayInWinterTime_DayIsFrom0600UpTo2200Oslo(string utc, GridPeriod expected) =>
        Assert.Equal(expected, GridPeriods.For(Utc(utc)));

    // Summer time: Oslo is UTC+2, so 05:00 Oslo is 03:00Z. Wednesday 2026-07-01.
    [Theory]
    [InlineData("2026-07-01T03:00:00Z", GridPeriod.Night)] // 05:00 Oslo
    [InlineData("2026-07-01T04:00:00Z", GridPeriod.Day)] // 06:00 Oslo
    [InlineData("2026-07-01T19:00:00Z", GridPeriod.Day)] // 21:00 Oslo
    [InlineData("2026-07-01T20:00:00Z", GridPeriod.Night)] // 22:00 Oslo
    public void OnAWeekdayInSummerTime_DayIsFrom0600UpTo2200Oslo(string utc, GridPeriod expected) =>
        Assert.Equal(expected, GridPeriods.For(Utc(utc)));

    [Theory]
    [InlineData("2026-12-05T12:00:00Z")] // Saturday 13:00 Oslo
    [InlineData("2026-12-06T12:00:00Z")] // Sunday 13:00 Oslo
    public void OnASaturdayOrSunday_EveryHourIsNight(string utc) =>
        Assert.Equal(GridPeriod.Night, GridPeriods.For(Utc(utc)));

    [Theory]
    [InlineData("2026-05-01T10:00:00Z")] // Friday 1 May
    [InlineData("2026-05-17T10:00:00Z")] // Sunday, but also 17 May
    [InlineData("2026-04-03T10:00:00Z")] // Good Friday
    [InlineData("2026-12-25T10:00:00Z")] // Friday 25 December
    public void OnAPublicHoliday_EveryHourIsNight(string utc) =>
        Assert.Equal(GridPeriod.Night, GridPeriods.For(Utc(utc)));

    [Fact]
    public void TheDayAfterAHoliday_IsAnOrdinaryWeekday() =>
        Assert.Equal(GridPeriod.Day, GridPeriods.For(Utc("2026-04-07T10:00:00Z"))); // Tuesday after Easter

    [Fact]
    public void TheHourAtMidnightOsloIsDecidedByTheOsloDateNotTheUtcDate()
    {
        // 2026-12-04T23:00Z is Saturday 00:00 in Oslo: night. 2026-12-03T23:00Z is Friday 00:00: also night (before 06).
        Assert.Equal(GridPeriod.Night, GridPeriods.For(Utc("2026-12-04T23:00:00Z")));
        // Friday 2026-12-04 at 06:00 Oslo is 05:00Z and is a weekday day hour.
        Assert.Equal(GridPeriod.Day, GridPeriods.For(Utc("2026-12-04T05:00:00Z")));
        // Friday 22:00 Oslo (21:00Z) is night even though it is still Friday.
        Assert.Equal(GridPeriod.Night, GridPeriods.For(Utc("2026-12-04T21:00:00Z")));
    }

    [Theory]
    [InlineData("2027-03-27T23:00:00Z", 23)] // Sunday 28 March 2027: clocks go forward
    [InlineData("2026-10-24T22:00:00Z", 25)] // Sunday 25 October 2026: clocks go back
    public void OnAClockChangeDay_EveryRealHourGetsOnePeriod(string dayStartUtc, int hours)
    {
        DateTime start = Utc(dayStartUtc);

        // Both days are Sundays: night all day, for every one of the 23 or 25 real hours.
        for (int i = 0; i < hours; i++)
        {
            Assert.Equal(GridPeriod.Night, GridPeriods.For(start.AddHours(i)));
        }
    }

    [Fact]
    public void OnTheClockChangeMondayAfterTheShortDay_DayStartsAt0600OsloSummerTime()
    {
        // Monday 29 March 2027 is Easter Monday, a holiday: night. Use Tuesday 30 March: 06:00 Oslo (UTC+2) is 04:00Z.
        Assert.Equal(GridPeriod.Night, GridPeriods.For(Utc("2027-03-30T03:00:00Z")));
        Assert.Equal(GridPeriod.Day, GridPeriods.For(Utc("2027-03-30T04:00:00Z")));
    }
}
