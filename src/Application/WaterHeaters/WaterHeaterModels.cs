using Domain.Common;

namespace Application.WaterHeaters;

/// <summary>
/// The decrypted values needed to call OSO for one heater. Built just before a call and never stored or logged:
/// <see cref="ToString"/> prints only the location id.
/// </summary>
public sealed record WaterHeaterCredentials(Guid LocationId, string DeviceId, string SubscriptionKey)
{
    public override string ToString() => $"WaterHeaterCredentials(LocationId={LocationId})";
}

public enum SpecialMode
{
    None,
    Holiday,
    Supercharge,
    BlueBattery
}

/// <summary>OSO's undocumented control and optimisation codes, passed on without interpretation.</summary>
public sealed record OsoCodes(int? ControlMode, int? OptimizationOption, int? OptimizationSubOption, int? ProfileId);

/// <summary>What OSO says about a heater right now, cleaned of OSO's spelling and time quirks.</summary>
public sealed record WaterHeaterReading(
    DateTime ReadingTimeUtc,
    DateTime? LastActivityUtc,
    bool IsOnline,
    bool IsHeating,
    decimal? SetPointCelsius,
    decimal? TemperatureTopCelsius,
    decimal? TemperatureMidCelsius,
    decimal? TemperatureLowCelsius,
    decimal? TemperatureFourthCelsius,
    int? AvailableLitres40,
    int? MaxLitres40,
    int? AvailableShowers,
    int? AvailableBaths,
    decimal PowerKw,
    decimal? StoredEnergyKwh,
    SpecialMode SpecialMode,
    DateTime? SpecialModeStartUtc,
    DateTime? SpecialModeEndUtc,
    string? ProfileName,
    OsoCodes OsoCodes);

/// <summary>Errors of the water heater feature. Descriptions are fixed text and never include a key or device id.</summary>
public static class WaterHeaterErrors
{
    public static readonly Error Rejected = Error.Problem(
        "WaterHeater.Rejected", "OSO rejected the key or device id; an Admin must reconnect the water heater");
    public static readonly Error Unavailable = Error.Problem(
        "WaterHeater.Unavailable", "The water heater service (OSO) is unavailable; try again later");
    public static readonly Error InvalidResponse = Error.Problem(
        "WaterHeater.InvalidResponse", "The water heater service (OSO) answered in an unexpected way");
    public static readonly Error KeyUnreadable = Error.Problem(
        "WaterHeater.KeyUnreadable", "The stored OSO key cannot be read; an Admin must reconnect the water heater");
    public static readonly Error EncryptionNotConfigured = Error.Problem(
        "WaterHeater.EncryptionNotConfigured", "Water heaters cannot be connected: the server has no encryption key set");
    public static readonly Error NotConnected = Error.NotFound(
        "WaterHeater.NotConnected", "No water heater is connected to this location");
    public static readonly Error KeyExpiryOutOfRange = Error.Validation(
        "WaterHeater.KeyExpiryOutOfRange", "The key's expiry date must be after today and at most five years ahead");
    public static readonly Error DeviceInUse = Error.Conflict(
        "WaterHeater.DeviceInUse", "This water heater is already connected to another location");
}
