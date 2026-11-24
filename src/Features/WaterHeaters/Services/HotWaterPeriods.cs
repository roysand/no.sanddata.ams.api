using Features.WaterHeaters.Queries;

namespace Features.WaterHeaters.Services;

/// <summary>
/// The Oslo periods of the hot water view and their parts, in UTC (spec 020, research R7). A day's parts are its UTC
/// hours (23, 24 or 25), a week's and a month's are Oslo days, and a year's are Oslo months. Weeks are ISO weeks,
/// Monday to Monday.
/// </summary>
public static class HotWaterPeriods
{
    // The locations are in Norway. Its own copy, as CostTime has, so the slices stay independent.
    private static readonly TimeZoneInfo Oslo = TimeZoneInfo.FindSystemTimeZoneById("Europe/Oslo");

    public static DateOnly TodayInOslo(DateTime nowUtc) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(nowUtc, Oslo));

    /// <summary>The whole period that holds <paramref name="date"/>, from Oslo midnight to Oslo midnight.</summary>
    public static (DateTime FromUtc, DateTime ToUtc) Bounds(HotWaterPeriod period, DateOnly date)
    {
        (DateOnly first, DateOnly next) = LocalBounds(period, date);
        return (MidnightUtc(first), MidnightUtc(next));
    }

    /// <summary>The start of each part of the period, in UTC and in time order.</summary>
    public static IReadOnlyList<DateTime> PartStarts(HotWaterPeriod period, DateOnly date)
    {
        (DateOnly first, DateOnly next) = LocalBounds(period, date);
        var starts = new List<DateTime>();

        if (period == HotWaterPeriod.Day)
        {
            DateTime to = MidnightUtc(next);
            for (DateTime hour = MidnightUtc(first); hour < to; hour = hour.AddHours(1))
            {
                starts.Add(hour);
            }
        }
        else
        {
            bool byMonth = period == HotWaterPeriod.Year;
            for (DateOnly day = first; day < next; day = byMonth ? day.AddMonths(1) : day.AddDays(1))
            {
                starts.Add(MidnightUtc(day));
            }
        }

        return starts;
    }

    private static (DateOnly First, DateOnly Next) LocalBounds(HotWaterPeriod period, DateOnly date)
    {
        switch (period)
        {
            case HotWaterPeriod.Week:
                DateOnly monday = date.AddDays(-((7 + (int)date.DayOfWeek - 1) % 7));
                return (monday, monday.AddDays(7));
            case HotWaterPeriod.Month:
                var firstOfMonth = new DateOnly(date.Year, date.Month, 1);
                return (firstOfMonth, firstOfMonth.AddMonths(1));
            case HotWaterPeriod.Year:
                var firstOfYear = new DateOnly(date.Year, 1, 1);
                return (firstOfYear, firstOfYear.AddYears(1));
            default:
                return (date, date.AddDays(1));
        }
    }

    private static DateTime MidnightUtc(DateOnly date) => DateTime.SpecifyKind(
        TimeZoneInfo.ConvertTimeToUtc(date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified), Oslo),
        DateTimeKind.Utc);
}
