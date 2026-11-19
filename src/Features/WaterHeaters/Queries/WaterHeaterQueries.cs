using Application.CQRS;
using Domain.Common;

namespace Features.WaterHeaters.Queries;

public sealed record GetWaterHeaterSettingsQuery(Guid LocationId) : IQuery<Result<WaterHeaterSettingsResponse>>;

/// <summary>The connection as an Admin sees it. There is deliberately no field for the key.</summary>
public sealed record WaterHeaterSettingsResponse(Guid LocationId, string DeviceIdHint, DateTime ConnectedAt, DateTime UpdatedAt);

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

/// <summary>OSO's undocumented codes, passed on as they are.</summary>
public sealed record WaterHeaterOsoCodes(
    int? ControlMode, int? OptimizationOption, int? OptimizationSubOption, int? ProfileId);
