using Application.CQRS;
using Domain.Common;

namespace Features.ElectricityCost.Queries;

public record GetHourlyCostQuery(
    Guid UserId,
    Guid LocationId,
    DateTime? From,
    DateTime? To,
    int Page,
    int PageSize) : IQuery<Result<PagedHourlyCostResponse>>;

/// <summary>
/// The parts of the price of one kWh, in NOK including VAT in the total. Grid is null (and GridSet false) when the
/// location has no grid tariff yet. The parts add up exactly to Total.
/// </summary>
public record PriceParts(
    decimal Energy, decimal Markup, decimal? Grid, decimal Taxes, decimal Vat, decimal Total, bool GridSet);

/// <summary>
/// How an hour on Norgespris terms is split by the monthly cap: Cost = KwhWithinCap x Parts.Total + KwhAboveCap x
/// AboveCapParts.Total.
/// </summary>
public record CapResponse(decimal KwhWithinCap, decimal KwhAboveCap, PriceParts AboveCapParts);

/// <param name="RatePerKwh">
/// The full price per kWh including VAT; the same as Parts.Total unless the hour is split by the Norgespris cap, when it
/// is Cost / kWh.
/// </param>
/// <param name="Cap">Null unless some kWh of the hour are above the Norgespris monthly cap.</param>
public record HourRateCostResponse(decimal RatePerKwh, decimal Cost, PriceParts Parts, CapResponse? Cap);

public record HourlyCostItemResponse(
    DateTime HourStart,
    decimal ConsumptionKwh,
    string PricingModel,
    HourRateCostResponse? Actual,
    HourRateCostResponse? Comparison);

public record PagedHourlyCostResponse(IReadOnlyList<HourlyCostItemResponse> Items, int Page, int PageSize, int TotalCount);
