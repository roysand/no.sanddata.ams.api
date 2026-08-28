using Application.CQRS;
using Application.Common.Interfaces.Repositories;
using Domain.Common;
using Domain.Common.Entities;
using Features.Measurements.Commands;

namespace Features.Measurements.Handlers;

public class IngestMeasurementsCommandHandler : ICommandHandler<IngestMeasurementsCommand, Result<IngestMeasurementsResponse>>
{
    private readonly IMeasurementEfRepository<Measurement> _measurementRepository;

    public IngestMeasurementsCommandHandler(IMeasurementEfRepository<Measurement> measurementRepository) =>
        _measurementRepository = measurementRepository;

    public async Task<Result<IngestMeasurementsResponse>> Handle(IngestMeasurementsCommand command, CancellationToken ct)
    {
        foreach (MeasurementReading reading in command.Readings)
        {
            _measurementRepository.Insert(new Measurement(
                Guid.NewGuid(),
                command.LocationId,
                command.DeviceId,
                reading.Timestamp,
                reading.MeterId,
                reading.MeterType,
                reading.PowerWatts));
        }

        await _measurementRepository.SaveChangesAsync(ct);

        return Result.Success(new IngestMeasurementsResponse(command.Readings.Count));
    }
}
