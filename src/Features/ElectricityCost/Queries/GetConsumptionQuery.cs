using Application.CQRS;
using Domain.Common;

namespace Features.ElectricityCost.Queries;

public record GetConsumptionQuery(
    Guid UserId,
    Guid LocationId,
    Guid? MeterId,
    string Granularity,
    DateTime? From,
    DateTime? To) : IQuery<Result<ConsumptionResponse>>;

public record ConsumptionItemResponse(DateTime PeriodStart, decimal ConsumptionKwh);

public record ConsumptionResponse(string Granularity, IReadOnlyList<ConsumptionItemResponse> Items);
