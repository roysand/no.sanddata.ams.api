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
    WaterHeaterDayResponse? WaterHeater);

/// <summary>The sums of the day's hourly water heater parts (spec 012); null when no hour has one.</summary>
public record WaterHeaterDayResponse(decimal Kwh, decimal? Cost, int SampledMinutes, bool Capped);

public record DailyCostResponse(IReadOnlyList<DailyCostItemResponse> Items);
