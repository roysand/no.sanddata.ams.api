namespace Features.ElectricityCost.Services;

/// <param name="Hours">One projected kWh per remaining hour, in the order given.</param>
/// <param name="BasedOnHours">The hours of the window that had a reading (0 to 168).</param>
public sealed record ProjectionResult(IReadOnlyList<(DateTime HourStartUtc, decimal Kwh)> Hours, int BasedOnHours);

/// <summary>
/// The consumption of the rest of the month, projected from the hourly profile of the last 7 days: each remaining hour
/// gets the average kWh of the window hours with the same Oslo weekday and hour of day (normally one hour; two for the
/// repeated hour of the 25-hour day). A remaining hour with no matching hour (the 23-hour day, a gap in the readings,
/// a new location) gets the average kWh per hour of the window; with no readings at all it gets 0. Pure.
/// </summary>
public static class ConsumptionProjection
{
    private static readonly TimeZoneInfo Oslo = TimeZoneInfo.FindSystemTimeZoneById("Europe/Oslo");

    /// <param name="window">The hours with readings in the 7 days before the current hour, one entry per hour.</param>
    /// <param name="remainingHourStarts">The hours to project, as UTC hour starts.</param>
    public static ProjectionResult Project(
        IReadOnlyList<(DateTime HourStartUtc, decimal Kwh)> window, IReadOnlyList<DateTime> remainingHourStarts)
    {
        Dictionary<(DayOfWeek, int), decimal> profile = window
            .GroupBy(h => Key(h.HourStartUtc))
            .ToDictionary(g => g.Key, g => g.Average(h => h.Kwh));
        decimal fallback = window.Count == 0 ? 0m : window.Average(h => h.Kwh);

        List<(DateTime, decimal)> hours = remainingHourStarts
            .Select(hour => (hour, profile.TryGetValue(Key(hour), out decimal kwh) ? kwh : fallback))
            .ToList();

        return new ProjectionResult(hours, window.Count);
    }

    private static (DayOfWeek, int) Key(DateTime hourStartUtc)
    {
        DateTime local = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(hourStartUtc, DateTimeKind.Utc), Oslo);
        return (local.DayOfWeek, local.Hour);
    }
}
