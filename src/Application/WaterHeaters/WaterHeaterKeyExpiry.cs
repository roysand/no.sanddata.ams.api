using Application.ElectricityCost;

namespace Application.WaterHeaters;

/// <summary>
/// The rules for the OSO key's expiry date (spec 015, research R1): a calendar day in Oslo, after today and at most five
/// years ahead, one year from today when none is given.
/// </summary>
public static class WaterHeaterKeyExpiry
{
    private const int MaxYearsAhead = 5;

    /// <summary>Today in Oslo, the day the range is counted from.</summary>
    public static DateOnly Today(TimeProvider clock) => NorgesPrisSchedule.OsloDateOf(clock.GetUtcNow().UtcDateTime);

    /// <summary>OSO keys are valid for a year, so that is the date when the administrator gives none.</summary>
    public static DateOnly Default(DateOnly today) => today.AddYears(1);

    public static bool IsInRange(DateOnly expiresOn, DateOnly today) =>
        expiresOn > today && expiresOn <= today.AddYears(MaxYearsAhead);
}
