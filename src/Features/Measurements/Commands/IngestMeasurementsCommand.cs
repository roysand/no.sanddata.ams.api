using Application.CQRS;
using Domain.Common;

namespace Features.Measurements.Commands;

public record IngestMeasurementsCommand(Guid LocationId, string DeviceId, IReadOnlyList<MeasurementReading> Readings)
    : ICommand<Result<IngestMeasurementsResponse>>;

public record MeasurementReading(DateTime Timestamp, string? MeterId, string? MeterType, int PowerWatts);

public record IngestMeasurementsResponse(int Accepted);
