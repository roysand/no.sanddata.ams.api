using Application.CQRS;
using Domain.Common;

namespace Features.ElectricityCost.Queries;

public record GetCurrentHourCostQuery(Guid UserId, Guid LocationId) : IQuery<Result<CurrentHourCostResponse>>;

public record CurrentRateCostResponse(decimal RatePerKwh, decimal CostSoFar);

public record CurrentHourCostResponse(
    DateTime HourStart,
    decimal ConsumptionKwhSoFar,
    string PricingModel,
    CurrentRateCostResponse? Actual,
    CurrentRateCostResponse? Comparison);
