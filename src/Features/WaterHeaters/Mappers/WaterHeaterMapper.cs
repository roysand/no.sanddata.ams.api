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
        new(heater.LocationId, DeviceIdHint(heater.DeviceId), heater.CreatedAt, heater.UpdatedAt);

    /// <summary>The first and last 6 characters, so an Admin can tell which heater it is; never the whole id.</summary>
    public static string DeviceIdHint(string deviceId) =>
        deviceId.Length <= HintChars * 2
            ? new string('*', deviceId.Length)
            : $"{deviceId[..HintChars]}…{deviceId[^HintChars..]}";

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
