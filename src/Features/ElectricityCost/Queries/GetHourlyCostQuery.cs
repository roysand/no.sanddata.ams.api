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

/// <param name="RatePerKwh">The full price per kWh including VAT; the same as Parts.Total.</param>
public record HourRateCostResponse(decimal RatePerKwh, decimal Cost, PriceParts Parts);

public record HourlyCostItemResponse(
    DateTime HourStart,
    decimal ConsumptionKwh,
    string PricingModel,
    HourRateCostResponse? Actual,
    HourRateCostResponse? Comparison);

public record PagedHourlyCostResponse(IReadOnlyList<HourlyCostItemResponse> Items, int Page, int PageSize, int TotalCount);
