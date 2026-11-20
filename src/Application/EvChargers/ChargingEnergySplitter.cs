namespace Application.EvChargers;

/// <summary>
/// Splits a Zaptec charging session into kWh per UTC clock hour (spec 016, research R5). Pure: no clock, no I/O.
/// <para>
/// Zaptec's <c>energyDetails</c> points are the kWh delivered in the period that ends at each timestamp: the first
/// point is at the session start with 0, the middle ones at whole hours, the last one at the session end. So each
/// point belongs to the hour that ends at it. Hours without charging have no point.
/// </para>
/// </summary>
public static class ChargingEnergySplitter
{
    /// <summary>A timestamp this close to a whole hour is taken as that hour (Zaptec sends e.g. 02:59:59).</summary>
    public static readonly TimeSpan Snap = TimeSpan.FromMinutes(2);

    /// <summary>Difference from the session's kWh above which the hours are scaled to it.</summary>
    public const decimal Tolerance = 0.001m;

    public static IReadOnlyList<ChargingHourSplit> Split(
        DateTime startUtc, DateTime endUtc, decimal totalKwh, IReadOnlyList<EnergyPoint> details)
    {
        DateTime firstHour = Floor(startUtc);
        DateTime lastHour = Floor(endUtc);

        var hours = new SortedDictionary<DateTime, decimal>();
        foreach (EnergyPoint point in details)
        {
            DateTime hour = HourEndingAt(point.TimestampUtc);
            if (hour < firstHour || hour > lastHour)
            {
                continue;
            }

            hours[hour] = hours.GetValueOrDefault(hour) + Math.Max(0m, point.Kwh);
        }

        decimal sum = hours.Values.Sum();
        if (hours.Count == 0 || sum == 0m && totalKwh > 0m)
        {
            return SpreadEvenly(startUtc, endUtc, totalKwh);
        }

        // A safety net: Roy's sessions add up exactly, but the stored session must match the Zaptec app (SC-002).
        decimal factor = Math.Abs(sum - totalKwh) > Tolerance ? totalKwh / sum : 1m;
        return hours.Select(h => new ChargingHourSplit(h.Key, h.Value * factor, Estimated: false)).ToList();
    }

    /// <summary>The kWh spread over the session's time, split at hour boundaries; every hour is estimated.</summary>
    private static IReadOnlyList<ChargingHourSplit> SpreadEvenly(DateTime startUtc, DateTime endUtc, decimal totalKwh)
    {
        if (endUtc <= startUtc)
        {
            return [new ChargingHourSplit(Floor(startUtc), totalKwh, Estimated: true)];
        }

        decimal totalTicks = (endUtc - startUtc).Ticks;
        var result = new List<ChargingHourSplit>();
        for (DateTime hour = Floor(startUtc); hour < endUtc; hour = hour.AddHours(1))
        {
            DateTime from = hour > startUtc ? hour : startUtc;
            DateTime to = hour.AddHours(1) < endUtc ? hour.AddHours(1) : endUtc;
            result.Add(new ChargingHourSplit(hour, totalKwh * (to - from).Ticks / totalTicks, Estimated: true));
        }

        return result;
    }

    /// <summary>
    /// The hour a point's energy belongs to: the hour before a (snapped) whole hour, or the hour that contains a
    /// timestamp elsewhere (the session's end point).
    /// </summary>
    private static DateTime HourEndingAt(DateTime timestampUtc)
    {
        DateTime nearest = Floor(timestampUtc.AddMinutes(30));
        TimeSpan distance = (timestampUtc - nearest).Duration();
        return distance <= Snap ? nearest.AddHours(-1) : Floor(timestampUtc);
    }

    private static DateTime Floor(DateTime utc) => new(utc.Year, utc.Month, utc.Day, utc.Hour, 0, 0, DateTimeKind.Utc);
}
