using System.Globalization;
using Features.ElectricityCost.Services;

namespace Features.Tests.ElectricityCost;

/// <summary>The projection of the rest of the month from the hourly profile of the last 7 days (research R6).</summary>
public class ConsumptionProjectionTests
{
    private static DateTime Utc(string iso) =>
        DateTime.Parse(iso, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal);

    private static List<(DateTime, decimal)> Hours(DateTime from, int count, Func<int, decimal> kwh) =>
        Enumerable.Range(0, count).Select(i => (from.AddHours(i), kwh(i))).ToList();

    [Fact]
    public void AFullWindow_RepeatsLastWeekHourByHour()
    {
        // 2026-12-03 00:00 UTC (01:00 Oslo, winter time), 168 hours with kWh = the index of the hour.
        DateTime windowStart = Utc("2026-12-03T00:00:00Z");
        List<(DateTime, decimal)> window = Hours(windowStart, 168, i => i);
        List<DateTime> next = Enumerable.Range(0, 168).Select(i => windowStart.AddDays(7).AddHours(i)).ToList();

        ProjectionResult result = ConsumptionProjection.Project(window, next);

        Assert.Equal(168, result.BasedOnHours);
        Assert.Equal(Enumerable.Range(0, 168).Select(i => (decimal)i), result.Hours.Select(h => h.Kwh));
        Assert.Equal(next, result.Hours.Select(h => h.HourStartUtc));
    }

    [Fact]
    public void TheRepeatedHourOfThe25HourDay_IsAveraged()
    {
        // 2026-10-25: 02:00 Oslo happens twice, at 00:00 UTC (summer time) and 01:00 UTC (winter time).
        List<(DateTime, decimal)> window =
        [
            (Utc("2026-10-25T00:00:00Z"), 2m),
            (Utc("2026-10-25T01:00:00Z"), 4m)
        ];

        // Sunday 2026-11-01 02:00 Oslo (winter time) is 01:00 UTC.
        ProjectionResult result = ConsumptionProjection.Project(window, [Utc("2026-11-01T01:00:00Z")]);

        Assert.Equal(3m, result.Hours.Single().Kwh);
    }

    [Fact]
    public void AnHourWithNoMatch_GetsTheAverageKwhPerHourOfTheWindow()
    {
        // 2027-03-28 has no 02:00 Oslo; the week after, Sunday 2027-04-04 02:00 Oslo (00:00 UTC) has no match.
        DateTime windowStart = Utc("2027-03-27T23:00:00Z"); // Sunday 2027-03-28 00:00 Oslo
        List<(DateTime, decimal)> window = Hours(windowStart, 23, i => i % 2 == 0 ? 1m : 3m); // the 23-hour day

        ProjectionResult result = ConsumptionProjection.Project(window, [Utc("2027-04-04T00:00:00Z")]);

        Assert.Equal(window.Average(h => h.Item2), result.Hours.Single().Kwh);
    }

    [Fact]
    public void AWindowWithThreeDaysOfReadings_UsesThem_AndTheAverageForTheRest()
    {
        DateTime windowStart = Utc("2026-12-03T00:00:00Z");
        // Readings on the last three days only (2026-12-07 to 09): 1, 2 and 6 kWh an hour, so the average is 3.
        List<(DateTime, decimal)> window = Hours(windowStart.AddDays(4), 72, i => i < 24 ? 1m : i < 48 ? 2m : 6m);

        ProjectionResult result = ConsumptionProjection.Project(
            window, [windowStart.AddDays(11), windowStart.AddDays(7)]);

        Assert.Equal(72, result.BasedOnHours);
        Assert.Equal(1m, result.Hours[0].Kwh); // 2026-12-14 matches Monday 2026-12-07
        Assert.Equal(3m, result.Hours[1].Kwh); // 2026-12-10: no reading on a Thursday, so the window average
    }

    [Fact]
    public void NoReadings_ProjectZeroKwh()
    {
        ProjectionResult result = ConsumptionProjection.Project([], [Utc("2026-12-10T12:00:00Z")]);

        Assert.Equal(0m, result.Hours.Single().Kwh);
        Assert.Equal(0, result.BasedOnHours);
    }
}
