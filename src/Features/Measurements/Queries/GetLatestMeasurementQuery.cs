using Application.CQRS;
using Domain.Common;

namespace Features.Measurements.Queries;

public record GetLatestMeasurementQuery(Guid UserId, Guid LocationId, Guid? MeterId) : IQuery<Result<LatestMeasurementResponse>>;

public record LatestMeasurementResponse(LatestMeasurement? Measurement);

/// <summary>The newest reading plus whether it is too old to be shown as "right now" (see Measurements:StaleAfterSeconds).</summary>
public record LatestMeasurement(DateTime Timestamp, Guid MeterId, int PowerWatts, bool IsStale);
