using Application.ElectricityCost;

namespace Features.Tests.Pricing;

public class NorwegianHolidaysTests
{
    [Theory]
    [InlineData(2026, 4, 5)]
    [InlineData(2027, 3, 28)]
    [InlineData(2028, 4, 16)]
    [InlineData(2025, 4, 20)]
    [InlineData(2024, 3, 31)]
    public void EasterSunday_IsComputedFromTheCalendar(int year, int month, int day) =>
        Assert.Equal(new DateOnly(year, month, day), NorwegianHolidays.EasterSunday(year));

    [Theory]
    [InlineData("2026-01-01")]
    [InlineData("2026-05-01")]
    [InlineData("2026-05-17")]
    [InlineData("2026-12-25")]
    [InlineData("2026-12-26")]
    public void FixedHolidays_AreHolidays(string date) =>
        Assert.True(NorwegianHolidays.IsHoliday(DateOnly.Parse(date)));

    [Theory]
    [InlineData("2026-04-02")] // Maundy Thursday
    [InlineData("2026-04-03")] // Good Friday
    [InlineData("2026-04-06")] // Easter Monday
    [InlineData("2026-05-14")] // Ascension Day
    [InlineData("2026-05-25")] // Whit Monday
    [InlineData("2027-03-25")]
    [InlineData("2027-03-26")]
    [InlineData("2027-03-29")]
    [InlineData("2027-05-06")]
    [InlineData("2027-05-17")]
    public void EasterBasedHolidays_AreHolidays(string date) =>
        Assert.True(NorwegianHolidays.IsHoliday(DateOnly.Parse(date)));

    [Theory]
    [InlineData("2026-04-04")] // Saturday before Easter
    [InlineData("2026-04-05")] // Easter Sunday is a Sunday, not listed
    [InlineData("2026-04-07")] // Tuesday after Easter
    [InlineData("2026-01-02")]
    [InlineData("2026-05-02")]
    [InlineData("2026-12-24")]
    [InlineData("2026-12-27")]
    [InlineData("2026-06-15")]
    public void OrdinaryDays_AreNotHolidays(string date) =>
        Assert.False(NorwegianHolidays.IsHoliday(DateOnly.Parse(date)));
}
