using Application.CQRS;
using Domain.Common;

namespace Features.ElectricityCost.Queries;

public record GetCurrentHourCostQuery(Guid UserId, Guid LocationId) : IQuery<Result<CurrentHourCostResponse>>;

/// <param name="RatePerKwh">The full price per kWh including VAT; the same as Parts.Total.</param>
public record CurrentRateCostResponse(decimal RatePerKwh, decimal CostSoFar, PriceParts Parts);

public record CurrentHourCostResponse(
    DateTime HourStart,
    decimal ConsumptionKwhSoFar,
    string PricingModel,
    CurrentRateCostResponse? Actual,
    CurrentRateCostResponse? Comparison);
