using Application.ElectricityCost;

namespace Application.Common.ApiKeys;

/// <summary>Which key a reminder is about.</summary>
public enum KeyKind
{
    /// <summary>The location's sensor key, used by the reader's forwarder.</summary>
    SensorKey,

    /// <summary>The OSO subscription key of the location's water heater.</summary>
    WaterHeaterKey,
}

/// <summary>What is known about a location's sensor key. The key and its hash are deliberately not part of it.</summary>
public sealed record SensorKeyFacts(
    Guid LocationId, string LocationName, bool LocationActive, bool KeyActive, DateTime ExpiresAtUtc);

/// <summary>What is known about a water heater's OSO key, for an active location.</summary>
public sealed record HeaterKeyFacts(Guid LocationId, string LocationName, DateOnly ExpiresOn);

/// <summary>
/// A key that expires soon or has expired. <see cref="ExpiresOn"/> is its last valid Oslo day, and
/// <see cref="DaysLeft"/> the whole days from today to it: 0 is its last day, negative means it has expired.
/// </summary>
public sealed record KeyReminder(
    Guid LocationId,
    string LocationName,
    KeyKind Kind,
    DateOnly ExpiresOn,
    DateTime? ExpiresAtUtc,
    int DaysLeft,
    bool Expired);

/// <summary>
/// Picks the keys an administrator must be reminded of (spec 015, research R3/R4): those that expire within
/// <c>windowDays</c> or have expired, counted in whole Oslo days. Pure; the caller reads the keys and the clock.
/// </summary>
public static class KeyReminders
{
    public const int DefaultWindowDays = 30;

    public static IReadOnlyList<KeyReminder> Due(
        DateOnly today,
        IEnumerable<SensorKeyFacts> sensorKeys,
        IEnumerable<HeaterKeyFacts> heaterKeys,
        int windowDays = DefaultWindowDays)
    {
        IEnumerable<KeyReminder> sensor = sensorKeys
            // A deactivated key is off on purpose, and an inactive location's key is refused anyway.
            .Where(k => k.LocationActive && k.KeyActive)
            .Select(k => Reminder(today, k.LocationId, k.LocationName, KeyKind.SensorKey, LastValidDay(k.ExpiresAtUtc),
                k.ExpiresAtUtc));

        IEnumerable<KeyReminder> heater = heaterKeys
            .Select(k => Reminder(today, k.LocationId, k.LocationName, KeyKind.WaterHeaterKey, k.ExpiresOn, null));

        return sensor.Concat(heater)
            .Where(r => r.DaysLeft <= windowDays)
            .OrderBy(r => r.DaysLeft)
            .ThenBy(r => r.LocationName, StringComparer.CurrentCulture)
            .ThenBy(r => r.Kind)
            .ToList();
    }

    /// <summary>
    /// A key valid up to an instant is valid on the Oslo day before that instant; one that expires at exactly Oslo
    /// midnight was last valid the day before.
    /// </summary>
    public static DateOnly LastValidDay(DateTime expiresAtUtc) =>
        NorgesPrisSchedule.OsloDateOf(DateTime.SpecifyKind(expiresAtUtc, DateTimeKind.Utc).AddTicks(-1));

    private static KeyReminder Reminder(
        DateOnly today, Guid locationId, string name, KeyKind kind, DateOnly expiresOn, DateTime? expiresAtUtc)
    {
        int daysLeft = expiresOn.DayNumber - today.DayNumber;
        return new KeyReminder(locationId, name, kind, expiresOn, expiresAtUtc, daysLeft, daysLeft < 0);
    }
}
