using Application.CQRS;
using Domain.Common;

namespace Features.WaterHeaters.Queries;

public sealed record GetWaterHeaterSettingsQuery(Guid LocationId) : IQuery<Result<WaterHeaterSettingsResponse>>;

/// <summary>
/// The connection as an Admin sees it. There is deliberately no field for the key. <paramref name="KeyExpiresOn"/> is the
/// OSO key's last valid Oslo day (spec 015); it is not named after the key, so no field name suggests the key itself.
/// </summary>
public sealed record WaterHeaterSettingsResponse(
    Guid LocationId, string DeviceIdHint, DateTime ConnectedAt, DateTime UpdatedAt, DateOnly KeyExpiresOn);

/// <summary>The heater's live state for a user who may see the location (or an Admin).</summary>
public sealed record GetWaterHeaterStateQuery(Guid UserId, bool IsAdmin, Guid LocationId)
    : IQuery<Result<WaterHeaterStateResponse>>;

public sealed record WaterHeaterStateResponse(
    Guid LocationId,
    DateTime ReadingTime,
    int AgeSeconds,
    bool IsStale,
    bool IsOnline,
    bool IsHeating,
    decimal? SetPointCelsius,
    WaterHeaterTemperatures Temperatures,
    WaterHeaterHotWater HotWater,
    decimal PowerKw,
    WaterHeaterSpecialMode SpecialMode,
    WaterHeaterOsoCodes OsoCodes);

/// <summary>Tank temperatures, °C. A sensor the heater does not have is null, never 0.</summary>
public sealed record WaterHeaterTemperatures(
    decimal? TopCelsius, decimal? MidCelsius, decimal? LowCelsius, decimal? FourthCelsius);

public sealed record WaterHeaterHotWater(
    int? AvailableLitres40, int? MaxLitres40, int? Showers, int? Baths, decimal? StoredEnergyKwh);

/// <summary>Mode is None, Holiday, Supercharge or BlueBattery; Start and End are null for None and BlueBattery.</summary>
public sealed record WaterHeaterSpecialMode(string Mode, DateTime? Start, DateTime? End, string? ProfileName);

/// <summary>
/// The heater's average power per 5 minutes in a window, for a user who may see the location (or an Admin). A
/// <paramref name="From"/> or <paramref name="To"/> left out gets its default (spec 017, contracts/endpoints.md).
/// </summary>
public sealed record GetWaterHeaterPowerQuery(Guid UserId, bool IsAdmin, Guid LocationId, DateTime? From, DateTime? To)
    : IQuery<Result<WaterHeaterPowerResponse>>;

/// <summary>
/// The window actually used, in UTC, and one item per 5-minute step with any sampled time, ordered by start
/// (spec 017, contracts/endpoints.md).
/// </summary>
public sealed record WaterHeaterPowerResponse(
    Guid LocationId, DateTime From, DateTime To, int StepSeconds, IReadOnlyList<WaterHeaterPowerItem> Items);

/// <summary>
/// One 5-minute step: the average power over its sampled time only, and how many seconds (1–300) were sampled
/// (spec 017, research R2).
/// </summary>
public sealed record WaterHeaterPowerItem(DateTime Start, decimal PowerKw, int SampledSeconds);

/// <summary>The period of the hot water view and what its items are: hours, days, days or months (spec 020).</summary>
public enum HotWaterPeriod
{
    Day,
    Week,
    Month,
    Year
}

/// <summary>
/// The 40 °C litres used per part of a period, for a user who may see the location (or an Admin). A
/// <paramref name="Date"/> left out is today in Oslo (spec 020, contracts/endpoints.md).
/// </summary>
public sealed record GetWaterHeaterHotWaterQuery(
    Guid UserId, bool IsAdmin, Guid LocationId, HotWaterPeriod Period, DateOnly? Date)
    : IQuery<Result<WaterHeaterHotWaterResponse>>;

/// <summary>
/// The whole period in UTC, one item per part that starts before now, and the items' totals (spec 020,
/// contracts/endpoints.md). <paramref name="TotalLitres"/> is null when no item has litres.
/// </summary>
public sealed record WaterHeaterHotWaterResponse(
    Guid LocationId,
    string Period,
    DateTime From,
    DateTime To,
    int? TotalLitres,
    int SampledMinutes,
    IReadOnlyList<WaterHeaterHotWaterItem> Items);

/// <summary>
/// One part of the period: whole 40 °C litres used, or null when the part has no sampled time (never 0 for that), and
/// how many minutes were sampled (spec 020, contracts/endpoints.md).
/// </summary>
public sealed record WaterHeaterHotWaterItem(DateTime Start, int? Litres, int SampledMinutes);

/// <summary>OSO's undocumented codes, passed on as they are.</summary>
public sealed record WaterHeaterOsoCodes(
    int? ControlMode, int? OptimizationOption, int? OptimizationSubOption, int? ProfileId);
