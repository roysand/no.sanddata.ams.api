using Application.EvChargers;

namespace Features.Tests.EvChargers;

public class ChargingEnergySplitterTests
{
    private static DateTime U(string iso) => DateTime.Parse(iso, null,
        System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal);

    private static EnergyPoint P(string iso, decimal kwh) => new(U(iso), kwh);

    private static IReadOnlyList<ChargingHourSplit> Split(string start, string end, decimal total, params EnergyPoint[] points) =>
        ChargingEnergySplitter.Split(U(start), U(end), total, points);

    [Fact]
    public void TheRealSession_GivesTheKwhOfEachHour()
    {
        // Roy's session of 2026-10-04 (ZaptecSamples.FixtureSession), points rounded to the øre of a kWh.
        IReadOnlyList<ChargingHourSplit> hours = Split("2026-10-04T17:52:08.13Z", "2026-10-05T11:28:38.087Z", 33.982m,
            P("2026-10-04T17:52:08Z", 0m), P("2026-10-04T18:00:00Z", 0.002m), P("2026-10-04T20:00:00Z", 1.41m),
            P("2026-10-04T21:00:00Z", 4.527m), P("2026-10-04T22:00:00Z", 4.52m), P("2026-10-04T23:00:00Z", 4.522m),
            P("2026-10-05T00:00:00Z", 4.52m), P("2026-10-05T01:00:00Z", 4.519m), P("2026-10-05T02:00:00Z", 4.511m),
            P("2026-10-05T02:59:59Z", 4.516m), P("2026-10-05T04:00:00Z", 0.935m), P("2026-10-05T11:28:38Z", 0m));

        Assert.Equal(
            [
                (U("2026-10-04T17:00:00Z"), 0.002m), (U("2026-10-04T19:00:00Z"), 1.41m),
                (U("2026-10-04T20:00:00Z"), 4.527m), (U("2026-10-04T21:00:00Z"), 4.52m),
                (U("2026-10-04T22:00:00Z"), 4.522m), (U("2026-10-04T23:00:00Z"), 4.52m),
                (U("2026-10-05T00:00:00Z"), 4.519m), (U("2026-10-05T01:00:00Z"), 4.511m),
                (U("2026-10-05T02:00:00Z"), 4.516m), (U("2026-10-05T03:00:00Z"), 0.935m),
                (U("2026-10-05T11:00:00Z"), 0m),
            ],
            hours.Select(h => (h.HourStartUtc, h.Kwh)));
        Assert.All(hours, h => Assert.False(h.Estimated));
        Assert.All(hours, h => Assert.Equal(DateTimeKind.Utc, h.HourStartUtc.Kind));
        Assert.Equal(33.982m, hours.Sum(h => h.Kwh));
    }

    [Fact]
    public void AFirstPartialHour_GoesToTheHourItEnds()
    {
        IReadOnlyList<ChargingHourSplit> hours = Split("2026-10-08T18:23:09Z", "2026-10-08T19:30:00Z", 4.9m,
            P("2026-10-08T18:23:09Z", 0m), P("2026-10-08T19:00:00Z", 2.706m), P("2026-10-08T19:30:00Z", 2.194m));

        Assert.Equal([(U("2026-10-08T18:00:00Z"), 2.706m), (U("2026-10-08T19:00:00Z"), 2.194m)],
            hours.Select(h => (h.HourStartUtc, h.Kwh)));
    }

    [Fact]
    public void AnEndPointMidHour_GoesToTheHourThatContainsIt()
    {
        IReadOnlyList<ChargingHourSplit> hours = Split("2026-10-22T22:09:12Z", "2026-10-23T05:57:48Z", 4.258m,
            P("2026-10-23T05:57:48Z", 4.258m));

        Assert.Equal(U("2026-10-23T05:00:00Z"), Assert.Single(hours).HourStartUtc);
    }

    [Fact]
    public void SkippedHours_AreNotInvented()
    {
        IReadOnlyList<ChargingHourSplit> hours = Split("2026-10-04T17:52:00Z", "2026-10-04T20:00:00Z", 1.412m,
            P("2026-10-04T18:00:00Z", 0.002m), P("2026-10-04T20:00:00Z", 1.41m));

        Assert.Equal([U("2026-10-04T17:00:00Z"), U("2026-10-04T19:00:00Z")], hours.Select(h => h.HourStartUtc));
    }

    [Fact]
    public void TwoPointsInOneHour_AreAdded()
    {
        IReadOnlyList<ChargingHourSplit> hours = Split("2026-10-04T18:00:00Z", "2026-10-04T18:50:00Z", 3m,
            P("2026-10-04T18:20:00Z", 1m), P("2026-10-04T18:50:00Z", 2m));

        ChargingHourSplit hour = Assert.Single(hours);
        Assert.Equal(3m, hour.Kwh);
    }

    [Fact]
    public void TheStartPointOnAWholeHour_IsDropped()
    {
        IReadOnlyList<ChargingHourSplit> hours = Split("2026-10-04T18:00:30Z", "2026-10-04T19:00:00Z", 4.4m,
            P("2026-10-04T18:00:30Z", 0m), P("2026-10-04T19:00:00Z", 4.4m));

        Assert.Equal(U("2026-10-04T18:00:00Z"), Assert.Single(hours).HourStartUtc);
    }

    [Fact]
    public void NoPoints_SpreadEvenlyAndAreEstimated()
    {
        IReadOnlyList<ChargingHourSplit> hours = Split("2026-10-08T22:30:00Z", "2026-10-09T00:30:00Z", 8m);

        Assert.Equal(
            [(U("2026-10-08T22:00:00Z"), 2m), (U("2026-10-08T23:00:00Z"), 4m), (U("2026-10-09T00:00:00Z"), 2m)],
            hours.Select(h => (h.HourStartUtc, h.Kwh)));
        Assert.All(hours, h => Assert.True(h.Estimated));
    }

    [Fact]
    public void OnlyZeroPoints_ForASessionWithEnergy_SpreadEvenly()
    {
        IReadOnlyList<ChargingHourSplit> hours = Split("2026-10-08T22:00:00Z", "2026-10-08T23:00:00Z", 4m,
            P("2026-10-08T22:00:00Z", 0m), P("2026-10-08T22:30:00Z", 0m));

        Assert.True(Assert.Single(hours).Estimated);
        Assert.Equal(4m, hours[0].Kwh);
    }

    [Fact]
    public void PointsThatDoNotAddUp_AreScaledToTheSession()
    {
        IReadOnlyList<ChargingHourSplit> hours = Split("2026-10-08T18:00:00Z", "2026-10-08T20:00:00Z", 10m,
            P("2026-10-08T19:00:00Z", 2m), P("2026-10-08T20:00:00Z", 2m));

        Assert.Equal([5m, 5m], hours.Select(h => h.Kwh));
        Assert.All(hours, h => Assert.False(h.Estimated));
    }

    [Fact]
    public void ANegativePoint_CountsAsZero()
    {
        IReadOnlyList<ChargingHourSplit> hours = Split("2026-10-08T18:00:00Z", "2026-10-08T20:00:00Z", 4m,
            P("2026-10-08T19:00:00Z", -1m), P("2026-10-08T20:00:00Z", 4m));

        Assert.Equal([0m, 4m], hours.Select(h => h.Kwh));
    }

    [Fact]
    public void ASessionShorterThanAnHour_IsOneHour()
    {
        IReadOnlyList<ChargingHourSplit> hours = Split("2026-10-08T18:10:00Z", "2026-10-08T18:40:00Z", 2.2m);

        ChargingHourSplit hour = Assert.Single(hours);
        Assert.Equal((U("2026-10-08T18:00:00Z"), 2.2m), (hour.HourStartUtc, hour.Kwh));
    }

    [Fact]
    public void TheDaylightSavingChange_NeedsNoSpecialCase()
    {
        // 2026-10-25: Oslo goes from +2 to +1 at 01:00 UTC; the split is in UTC hours, so 4 hours are 4 hours.
        IReadOnlyList<ChargingHourSplit> hours = Split("2026-10-24T23:00:00Z", "2026-10-25T03:00:00Z", 8m);

        Assert.Equal(4, hours.Count);
        Assert.All(hours, h => Assert.Equal(2m, h.Kwh));
    }

    [Theory]
    [InlineData("2026-10-04T17:52:08Z", "2026-10-05T11:28:38Z", 33.982)]
    [InlineData("2026-10-08T22:30:00Z", "2026-10-09T00:30:00Z", 8.0)]
    [InlineData("2026-10-08T18:10:00Z", "2026-10-08T18:40:00Z", 0.0)]
    public void TheHours_AlwaysAddUpToTheSession(string start, string end, double total)
    {
        IReadOnlyList<ChargingHourSplit> hours = Split(start, end, (decimal)total);

        Assert.Equal((decimal)total, Math.Round(hours.Sum(h => h.Kwh), 4));
    }
}
