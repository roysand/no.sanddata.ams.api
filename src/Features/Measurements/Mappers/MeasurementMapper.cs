using Domain.Common.Entities;
using Features.Measurements.Commands;
using Features.Measurements.Endpoints;
using Features.Measurements.Queries;

namespace Features.Measurements.Mappers;

public static class MeasurementMapper
{
    public static IngestMeasurementsCommand ToCommand(Guid locationId, IngestMeasurementsRequest request) =>
        new(locationId, request.DeviceId, request.Readings.Select(ToReading).ToList());

    private static MeasurementReading ToReading(MeasurementReadingRequest request) =>
        new(
            DateTimeOffset.FromUnixTimeSeconds(request.Timestamp).UtcDateTime,
            request.MeterId,
            request.MeterType,
            request.PowerWatts);

    public static MeasurementResponse ToResponse(Measurement measurement) =>
        new(measurement.Timestamp, measurement.MeterId, measurement.PowerWatts);

    public static PagedMeasurementsResponse ToPagedResponse(
        IReadOnlyList<Measurement> items, int page, int pageSize, int totalCount) =>
        new(items.Select(ToResponse).ToList(), page, pageSize, totalCount);
}
