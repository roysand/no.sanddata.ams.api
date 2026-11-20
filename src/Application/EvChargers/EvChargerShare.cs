namespace Application.EvChargers;

/// <summary>The charger's kWh in one hour, and whether the location counts it with the house (spec 016).</summary>
public sealed record EvChargerHour(DateTime HourStartUtc, decimal Kwh, bool Estimated, bool Included);

/// <summary>
/// The sum of the charger's hours in a day or a month. <paramref name="HoursWithoutMeterReading"/> counts the charging
/// hours that had no meter reading for the house: they are in the sum, but not in the hourly list.
/// </summary>
public sealed record EvChargerTotal(
    decimal Kwh,
    bool Estimated,
    bool Included,
    int HoursWithoutMeterReading,
    DateTime FirstHourUtc,
    DateTime LastHourUtc);

/// <summary>
/// The pure rules for the charger's part of the figures (spec 016, research R6/R7). The charger is never priced, and
/// the meter's kWh, on which every cost is based, never changes: the charger only adds to the total kWh when included.
/// </summary>
public static class EvChargerShare
{
    public static EvChargerHour ForHour(EvChargerHourEnergy energy, bool included) =>
        new(energy.HourStartUtc, energy.Kwh, energy.Estimated, included);

    /// <summary>The meter's kWh, plus the charger's when the location counts it with the house.</summary>
    public static decimal TotalKwh(decimal meterKwh, EvChargerHour? part) =>
        part is { Included: true } ? meterKwh + part.Kwh : meterKwh;

    public static decimal TotalKwh(decimal meterKwh, EvChargerTotal? total) =>
        total is { Included: true } ? meterKwh + total.Kwh : meterKwh;

    /// <summary>
    /// The sum of the hours with a charger part (on meter hours) and the charging hours without a meter reading; null
    /// when there are none, so "no data" is never shown as zero.
    /// </summary>
    public static EvChargerTotal? Sum(IEnumerable<EvChargerHour?> onMeterHours, IReadOnlyCollection<EvChargerHour> withoutMeterReading)
    {
        var parts = onMeterHours.OfType<EvChargerHour>().Concat(withoutMeterReading).ToList();
        if (parts.Count == 0)
        {
            return null;
        }

        return new EvChargerTotal(
            parts.Sum(p => p.Kwh),
            parts.Any(p => p.Estimated),
            parts[0].Included,
            withoutMeterReading.Count,
            parts.Min(p => p.HourStartUtc),
            parts.Max(p => p.HourStartUtc));
    }
}
