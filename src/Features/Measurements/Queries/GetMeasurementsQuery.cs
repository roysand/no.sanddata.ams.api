using Application.CQRS;
using Domain.Common;

namespace Features.Measurements.Queries;

public record GetMeasurementsQuery(
    Guid UserId,
    Guid LocationId,
    Guid? MeterId,
    DateTime? From,
    DateTime? To,
    int Page,
    int PageSize) : IQuery<Result<PagedMeasurementsResponse>>;

public record MeasurementResponse(DateTime Timestamp, Guid MeterId, int PowerWatts);

public record PagedMeasurementsResponse(IReadOnlyList<MeasurementResponse> Items, int Page, int PageSize, int TotalCount);
