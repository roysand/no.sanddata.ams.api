using Application.CQRS;
using Domain.Common;

namespace Features.Measurements.Queries;

public record GetLatestMeasurementQuery(Guid UserId, Guid LocationId, Guid? MeterId) : IQuery<Result<LatestMeasurementResponse>>;

public record LatestMeasurementResponse(MeasurementResponse? Measurement);
