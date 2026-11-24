using Application.WaterHeaters;
using Domain.Common.Entities;
using Features.WaterHeaters.Queries;

namespace Features.WaterHeaters.Mappers;

public static class WaterHeaterMapper
{
    private const int HintChars = 6;

    /// <summary>A reading older than this is marked stale (spec 011, FR-008).</summary>
    public const int StaleAfterSeconds = 15 * 60;

    public static WaterHeaterSettingsResponse ToSettings(WaterHeater heater) =>
        new(heater.LocationId, DeviceIdHint(heater.DeviceId), heater.CreatedAt, heater.UpdatedAt,
            heater.SubscriptionKeyExpiresOn);

    /// <summary>The first and last 6 characters, so an Admin can tell which heater it is; never the whole id.</summary>
    public static string DeviceIdHint(string deviceId) =>
        deviceId.Length <= HintChars * 2
            ? new string('*', deviceId.Length)
            : $"{deviceId[..HintChars]}…{deviceId[^HintChars..]}";

    /// <summary>
    /// A step's average power over its sampled time, rounded to watts, or null when the step rounds to 0 sampled
    /// seconds (spec 017, research R2). The power divides by the unrounded seconds.
    /// </summary>
    public static WaterHeaterPowerItem? ToPowerItem(WaterHeaterPowerStep step)
    {
        int seconds = (int)Math.Round(step.SampledSeconds, MidpointRounding.AwayFromZero);
        if (seconds == 0)
        {
            return null;
        }

        decimal powerKw = Math.Round(step.Kwh * 3600m / step.SampledSeconds, 3, MidpointRounding.AwayFromZero);
        return new WaterHeaterPowerItem(step.StepStartUtc, powerKw, seconds);
    }

    /// <summary>
    /// Rolls the hourly litres up into the period's parts (spec 020, research R7). Only parts that start before
    /// <paramref name="nowUtc"/> get an item; each part ends at the next start, the last at <paramref name="toUtc"/>.
    /// A part without hours has null litres. Otherwise the litres are summed, then rounded half away from zero, and
    /// the sampled minutes are rounded down and capped at the part's length (a pair spills into its later hour).
    /// </summary>
    public static IReadOnlyList<WaterHeaterHotWaterItem> ToHotWaterItems(
        IReadOnlyList<DateTime> partStarts, DateTime toUtc, DateTime nowUtc, IReadOnlyList<WaterHeaterHotWaterHour> hours)
    {
        var items = new List<WaterHeaterHotWaterItem>();
        for (int i = 0; i < partStarts.Count && partStarts[i] < nowUtc; i++)
        {
            DateTime start = partStarts[i];
            DateTime end = i + 1 < partStarts.Count ? partStarts[i + 1] : toUtc;

            var inPart = hours.Where(h => h.HourStartUtc >= start && h.HourStartUtc < end).ToList();
            if (inPart.Count == 0)
            {
                items.Add(new WaterHeaterHotWaterItem(start, null, 0));
                continue;
            }

            int litres = (int)Math.Round(inPart.Sum(h => h.Litres), MidpointRounding.AwayFromZero);
            int minutes = Math.Min((int)(inPart.Sum(h => h.SampledSeconds) / 60), (int)(end - start).TotalMinutes);
            items.Add(new WaterHeaterHotWaterItem(start, litres, minutes));
        }

        return items;
    }

    /// <summary>The period as the response names it: <c>day</c>, <c>week</c>, <c>month</c> or <c>year</c>.</summary>
    public static string ToWire(HotWaterPeriod p) => p.ToString().ToLowerInvariant();

    public static WaterHeaterStateResponse ToState(Guid locationId, WaterHeaterReading reading, DateTime nowUtc)
    {
        // A reading slightly "in the future" (clock skew between OSO and us) counts as brand new.
        int ageSeconds = (int)Math.Max(0, Math.Floor((nowUtc - reading.ReadingTimeUtc).TotalSeconds));
        bool hasTimes = reading.SpecialMode is SpecialMode.Holiday or SpecialMode.Supercharge;

        return new WaterHeaterStateResponse(
            locationId,
            reading.ReadingTimeUtc,
            ageSeconds,
            ageSeconds > StaleAfterSeconds,
            reading.IsOnline,
            reading.IsHeating,
            reading.SetPointCelsius,
            new WaterHeaterTemperatures(
                reading.TemperatureTopCelsius,
                reading.TemperatureMidCelsius,
                reading.TemperatureLowCelsius,
                reading.TemperatureFourthCelsius),
            new WaterHeaterHotWater(
                reading.AvailableLitres40,
                reading.MaxLitres40,
                reading.AvailableShowers,
                reading.AvailableBaths,
                reading.StoredEnergyKwh),
            reading.PowerKw,
            new WaterHeaterSpecialMode(
                reading.SpecialMode.ToString(),
                hasTimes ? reading.SpecialModeStartUtc : null,
                hasTimes ? reading.SpecialModeEndUtc : null,
                reading.ProfileName),
            new WaterHeaterOsoCodes(
                reading.OsoCodes.ControlMode,
                reading.OsoCodes.OptimizationOption,
                reading.OsoCodes.OptimizationSubOption,
                reading.OsoCodes.ProfileId));
    }
}
