using Application.CQRS;
using Domain.Common;

namespace Features.ElectricityCost.Queries;

public record GetDailyCostQuery(Guid UserId, Guid LocationId, DateTime? From, DateTime? To)
    : IQuery<Result<DailyCostResponse>>;

public record DayCostResponse(decimal Cost);

public record DailyCostItemResponse(
    DateOnly Date,
    decimal ConsumptionKwh,
    string PricingModel,
    DayCostResponse? Actual,
    DayCostResponse? Comparison,
    WaterHeaterDayResponse? WaterHeater,
    decimal TotalKwh,
    EvChargerDayResponse? EvCharger);

/// <summary>
/// The sum of the day's EV charger hours (spec 016), including charging hours that had no meter reading, which
/// <paramref name="HoursWithoutMeterReading"/> counts. Null when the day has no charging data.
/// </summary>
public record EvChargerDayResponse(decimal Kwh, bool Estimated, bool Included, int HoursWithoutMeterReading);

/// <summary>The sums of the day's hourly water heater parts (spec 012); null when no hour has one.</summary>
public record WaterHeaterDayResponse(decimal Kwh, decimal? Cost, int SampledMinutes, bool Capped);

public record DailyCostResponse(IReadOnlyList<DailyCostItemResponse> Items);
