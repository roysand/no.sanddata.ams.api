using Application.CQRS;
using Application.Common.Interfaces.Repositories;
using Domain.Common;
using Domain.Common.Entities;
using Features.Measurements.Commands;

namespace Features.Measurements.Handlers;

public class IngestMeasurementsCommandHandler : ICommandHandler<IngestMeasurementsCommand, Result<IngestMeasurementsResponse>>
{
    private readonly IMeasurementEfRepository<Measurement> _measurementRepository;
    private readonly IMeterEfRepository<Meter> _meterRepository;

    public IngestMeasurementsCommandHandler(
        IMeasurementEfRepository<Measurement> measurementRepository,
        IMeterEfRepository<Meter> meterRepository)
    {
        _measurementRepository = measurementRepository;
        _meterRepository = meterRepository;
    }

    public async Task<Result<IngestMeasurementsResponse>> Handle(IngestMeasurementsCommand command, CancellationToken ct)
    {
        Meter? meter = await _meterRepository.FindByDeviceIdAsync(command.LocationId, command.DeviceId, ct);
        if (meter is null)
        {
            return Result.Failure<IngestMeasurementsResponse>(
                Error.NotFound("Meter.NotRegistered", "No registered reader found for this device id at this location"));
        }

        bool meterIdentityChanged = false;
        foreach (MeasurementReading reading in command.Readings)
        {
            if (meter.MeterId is null && reading.MeterId is not null)
            {
                meter.UpdateMeterIdentity(reading.MeterId, reading.MeterType);
                meterIdentityChanged = true;
            }

            _measurementRepository.Insert(new Measurement(
                Guid.NewGuid(),
                command.LocationId,
                meter.Id,
                reading.Timestamp,
                reading.PowerWatts));
        }

        if (meterIdentityChanged)
        {
            _meterRepository.Update(meter);
        }

        await _measurementRepository.SaveChangesAsync(ct);

        return Result.Success(new IngestMeasurementsResponse(command.Readings.Count));
    }
}
