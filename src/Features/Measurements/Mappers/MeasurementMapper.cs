using Features.Measurements.Commands;
using Features.Measurements.Endpoints;

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
}
