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
    DayCostResponse? Comparison);

public record DailyCostResponse(IReadOnlyList<DailyCostItemResponse> Items);
