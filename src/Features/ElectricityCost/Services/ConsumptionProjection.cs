using Application.Common.Interfaces.Repositories;

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
    /// <summary>The window the profile is taken from: the 7 days before the current hour.</summary>
    public const int WindowHours = 7 * 24;

    private static readonly TimeZoneInfo Oslo = TimeZoneInfo.FindSystemTimeZoneById("Europe/Oslo");

    /// <param name="window">The hours with readings in the 7 days before the current hour, one entry per hour.</param>
    /// <param name="remainingHourStarts">The hours to project, as UTC hour starts.</param>
    public static ProjectionResult Project(
        IReadOnlyList<(DateTime HourStartUtc, decimal Kwh)> window, IReadOnlyList<DateTime> remainingHourStarts)
    {
        var profile = window
            .GroupBy(h => Key(h.HourStartUtc))
            .ToDictionary(g => g.Key, g => g.Average(h => h.Kwh));
        decimal fallback = window.Count == 0 ? 0m : window.Average(h => h.Kwh);

        List<(DateTime, decimal)> hours = remainingHourStarts
            .Select(hour => (hour, profile.TryGetValue(Key(hour), out decimal kwh) ? kwh : fallback))
            .ToList();

        return new ProjectionResult(hours, window.Count);
    }

    /// <summary>
    /// The current hour and the rest of the month, projected from the hourly rows of the 168 hours before the current
    /// hour. The bill and the Norgespris status both call this, so their projections are the same (spec 018, R5).
    /// </summary>
    /// <param name="rows">Hourly rows (any meters); those outside the 168-hour window are ignored.</param>
    public static ProjectionResult ProjectRestOfMonth(
        IReadOnlyList<HourConsumption> rows, DateTime currentHourStartUtc, DateTime monthEndUtc)
    {
        DateTime windowStartUtc = currentHourStartUtc.AddHours(-WindowHours);
        List<(DateTime, decimal)> window = rows
            .Select(r => (Hour: DateTime.SpecifyKind(r.BucketStart, DateTimeKind.Utc), r.ConsumptionKwh))
            .Where(r => r.Hour >= windowStartUtc && r.Hour < currentHourStartUtc)
            .GroupBy(r => r.Hour)
            .Select(g => (g.Key, g.Sum(r => r.ConsumptionKwh)))
            .ToList();

        var remaining = new List<DateTime>();
        for (DateTime hour = currentHourStartUtc; hour < monthEndUtc; hour = hour.AddHours(1))
        {
            remaining.Add(hour);
        }

        return Project(window, remaining);
    }

    private static (DayOfWeek, int) Key(DateTime hourStartUtc)
    {
        DateTime local = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(hourStartUtc, DateTimeKind.Utc), Oslo);
        return (local.DayOfWeek, local.Hour);
    }
}
