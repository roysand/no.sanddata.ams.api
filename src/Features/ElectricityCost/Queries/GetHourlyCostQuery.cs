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

public record HourRateCostResponse(decimal RatePerKwh, decimal Cost);

public record HourlyCostItemResponse(
    DateTime HourStart,
    decimal ConsumptionKwh,
    string PricingModel,
    HourRateCostResponse? Actual,
    HourRateCostResponse? Comparison);

public record PagedHourlyCostResponse(IReadOnlyList<HourlyCostItemResponse> Items, int Page, int PageSize, int TotalCount);
