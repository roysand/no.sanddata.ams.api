using Application.CQRS;
using Domain.Common;

namespace Features.ElectricityCost.Queries;

public record GetCurrentHourCostQuery(Guid UserId, Guid LocationId) : IQuery<Result<CurrentHourCostResponse>>;

/// <param name="RatePerKwh">The full price per kWh including VAT; the same as Parts.Total.</param>
/// <param name="Cap">Null unless some kWh of the hour are above the Norgespris monthly cap.</param>
public record CurrentRateCostResponse(decimal RatePerKwh, decimal CostSoFar, PriceParts Parts, CapResponse? Cap);

public record CurrentHourCostResponse(
    DateTime HourStart,
    decimal ConsumptionKwhSoFar,
    string PricingModel,
    CurrentRateCostResponse? Actual,
    CurrentRateCostResponse? Comparison);
