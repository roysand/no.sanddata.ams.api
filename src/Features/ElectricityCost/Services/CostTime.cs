namespace Features.ElectricityCost.Services;

internal static class CostTime
{
    // Locations are in Norway: a "day" for daily totals runs from local midnight to local midnight.
    private static readonly TimeZoneInfo Oslo = TimeZoneInfo.FindSystemTimeZoneById("Europe/Oslo");

    public static DateTime ToUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
    };

    /// <summary>The Oslo calendar date of an instant; an unspecified-kind value is already a plain date.</summary>
    public static DateTime LocalDate(DateTime value) => value.Kind == DateTimeKind.Unspecified
        ? value.Date
        : TimeZoneInfo.ConvertTimeFromUtc(ToUtc(value), Oslo).Date;

    public static DateTime LocalDateOf(DateTime utcInstant) => TimeZoneInfo.ConvertTimeFromUtc(utcInstant, Oslo).Date;

    /// <summary>The UTC instant at which the given Oslo calendar day starts.</summary>
    public static DateTime LocalDayStartUtc(DateTime localDate) => DateTime.SpecifyKind(
        TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(localDate.Date, DateTimeKind.Unspecified), Oslo),
        DateTimeKind.Utc);
}
