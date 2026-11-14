using Application.ElectricityCost;
using Domain.Common.Entities;

namespace Features.Tests.NorgesPris;

public class NorgesPrisScheduleTests
{
    private static readonly IReadOnlyList<NorgesPrisRate> Rates =
    [
        NorgesPrisRates.Rate("2020-01-01", 0.40m),
        NorgesPrisRates.Rate("2027-01-01", 0.50m, 0.01m)
    ];

    private static DateTime Utc(string iso) => DateTime.Parse(iso, null, System.Globalization.DateTimeStyles.AdjustToUniversal);

    [Fact]
    public void RateFor_BeforeAndAfterTheStartDate_UsesTheRateThatWasValid()
    {
        Assert.Equal(0.40m, NorgesPrisSchedule.RateFor(Rates, Utc("2026-12-15T10:00:00Z"))!.RatePerKwh);
        Assert.Equal(0.50m, NorgesPrisSchedule.RateFor(Rates, Utc("2027-01-15T10:00:00Z"))!.RatePerKwh);
    }

    [Fact]
    public void RateFor_TheHourThatStartsAtMidnightInOsloInWinter_UsesTheNewRate()
    {
        // 23:00Z on 31 December is 00:00 on 1 January in Oslo (UTC+1).
        Assert.Equal(0.40m, NorgesPrisSchedule.RateFor(Rates, Utc("2026-12-31T22:00:00Z"))!.RatePerKwh);
        Assert.Equal(0.50m, NorgesPrisSchedule.RateFor(Rates, Utc("2026-12-31T23:00:00Z"))!.RatePerKwh);
    }

    [Fact]
    public void RateFor_TheHourThatStartsAtMidnightInOsloInSummer_UsesTheNewRate()
    {
        IReadOnlyList<NorgesPrisRate> summer =
            [NorgesPrisRates.Rate("2020-01-01", 0.40m), NorgesPrisRates.Rate("2027-07-01", 0.55m)];

        // Oslo is UTC+2 in July: 00:00 on 1 July is 22:00Z on 30 June.
        Assert.Equal(0.40m, NorgesPrisSchedule.RateFor(summer, Utc("2027-06-30T21:00:00Z"))!.RatePerKwh);
        Assert.Equal(0.55m, NorgesPrisSchedule.RateFor(summer, Utc("2027-06-30T22:00:00Z"))!.RatePerKwh);
    }

    [Fact]
    public void RateFor_ARateStartingTodayApplies_FromTheFirstHourOfToday() => Assert.Equal(0.50m, NorgesPrisSchedule.RateFor(Rates, Utc("2027-01-01T00:00:00Z"))!.RatePerKwh);

    [Fact]
    public void RateFor_BeforeTheEarliestRate_ReturnsNull() => Assert.Null(NorgesPrisSchedule.RateFor(Rates, Utc("2019-12-31T10:00:00Z")));

    [Theory]
    [InlineData("2027-03-27T23:00:00Z", 23)] // 28 March 2027: clocks go forward
    [InlineData("2026-10-24T22:00:00Z", 25)] // 25 October 2026: clocks go back
    public void RateFor_OnAClockChangeDay_EveryHourGetsExactlyOneRate(string dayStartUtc, int hours)
    {
        DateTime start = Utc(dayStartUtc);
        IReadOnlyList<NorgesPrisRate> sameDay =
        [
            NorgesPrisRates.Rate("2020-01-01", 0.40m),
            NorgesPrisRates.Rate(NorgesPrisSchedule.OsloDateOf(start).ToString("yyyy-MM-dd"), 0.60m)
        ];

        for (int i = 0; i < hours; i++)
        {
            Assert.Equal(0.60m, NorgesPrisSchedule.RateFor(sameDay, start.AddHours(i))!.RatePerKwh);
        }

        // The hour just before the day starts still belongs to the previous Oslo date.
        Assert.Equal(0.40m, NorgesPrisSchedule.RateFor(sameDay, start.AddHours(-1))!.RatePerKwh);
        // The hour after the last real hour is the next Oslo day.
        Assert.Equal(0.60m, NorgesPrisSchedule.RateFor(sameDay, start.AddHours(hours))!.RatePerKwh);
    }
}
