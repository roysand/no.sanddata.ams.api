namespace Application.ElectricityCost;

/// <summary>Which of the two grid energy rates applies to an hour.</summary>
public enum GridPeriod
{
    /// <summary>Weekdays that are not public holidays, from 06:00 up to but not including 22:00 Oslo time.</summary>
    Day,

    /// <summary>All other hours: nights, weekends and public holidays.</summary>
    Night
}

public static class GridPeriods
{
    private static readonly TimeZoneInfo Oslo = TimeZoneInfo.FindSystemTimeZoneById("Europe/Oslo");

    private const int FirstDayHour = 6;
    private const int FirstNightHour = 22;

    /// <summary>
    /// The period of the hour that starts at <paramref name="hourStartUtc"/>. Decided from the Oslo local time of that
    /// hour, so a day with 23 or 25 hours needs no special case.
    /// </summary>
    public static GridPeriod For(DateTime hourStartUtc)
    {
        DateTime local = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(hourStartUtc, DateTimeKind.Utc), Oslo);

        bool weekday = local.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday);
        bool dayHour = local.Hour is >= FirstDayHour and < FirstNightHour;

        return weekday && dayHour && !NorwegianHolidays.IsHoliday(DateOnly.FromDateTime(local))
            ? GridPeriod.Day
            : GridPeriod.Night;
    }
}
