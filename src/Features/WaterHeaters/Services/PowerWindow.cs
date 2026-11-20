namespace Features.WaterHeaters.Services;

/// <summary>
/// The time window of the heater's 5-minute power (spec 017, research R3): its defaults and its limits. The validator
/// uses it to refuse a bad window and the handler to get the window, so the two can't drift. It does not round to
/// whole 5 minutes.
/// </summary>
public static class PowerWindow
{
    public static readonly TimeSpan DefaultLength = TimeSpan.FromHours(3);
    public static readonly TimeSpan MaxLength = TimeSpan.FromHours(48);

    /// <summary><c>to</c> left out is now; <c>from</c> left out is <c>to</c> minus 3 hours. Both in UTC.</summary>
    public static (DateTime FromUtc, DateTime ToUtc) Resolve(DateTime? from, DateTime? to, DateTime nowUtc)
    {
        DateTime toUtc = to?.ToUniversalTime() ?? nowUtc;
        DateTime fromUtc = from?.ToUniversalTime() ?? toUtc - DefaultLength;
        return (fromUtc, toUtc);
    }

    /// <summary>Null for a valid window, otherwise why it is refused. <c>to</c> may be in the future.</summary>
    public static string? Problem(DateTime fromUtc, DateTime toUtc, DateTime nowUtc)
    {
        if (fromUtc >= toUtc)
        {
            return "from must be before to";
        }

        if (toUtc - fromUtc > MaxLength)
        {
            return "The window can be at most 48 hours";
        }

        return fromUtc > nowUtc ? "from must not be in the future" : null;
    }
}
