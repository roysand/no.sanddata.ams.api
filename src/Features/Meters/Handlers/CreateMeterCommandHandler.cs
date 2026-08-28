using Application.CQRS;
using Application.Common.Interfaces.Repositories;
using Domain.Common;
using Domain.Common.Entities;
using Features.Meters.Commands;
using Features.Meters.Mappers;

namespace Features.Meters.Handlers;

public class CreateMeterCommandHandler : ICommandHandler<CreateMeterCommand, Result<MeterResponse>>
{
    private readonly IMeterEfRepository<Meter> _meterRepository;
    private readonly ILocationEfRepository<Location> _locationRepository;

    public CreateMeterCommandHandler(
        IMeterEfRepository<Meter> meterRepository,
        ILocationEfRepository<Location> locationRepository)
    {
        _meterRepository = meterRepository;
        _locationRepository = locationRepository;
    }

    public async Task<Result<MeterResponse>> Handle(CreateMeterCommand command, CancellationToken ct)
    {
        Location? location = await _locationRepository.GetByIdAsync(command.LocationId, ct);
        if (location is null)
        {
            return Result.Failure<MeterResponse>(Error.NotFound("Location.NotFound", "Location not found"));
        }

        bool alreadyRegistered = await _meterRepository.ExistsAsync(
            m => m.LocationId == command.LocationId && m.DeviceId == command.DeviceId, ct);
        if (alreadyRegistered)
        {
            return Result.Failure<MeterResponse>(
                Error.Conflict("Meter.DeviceIdExists", "A reader with this device id is already registered at this location"));
        }

        var meter = new Meter(Guid.NewGuid(), command.LocationId, command.DeviceId, command.Comment, isActive: true);
        _meterRepository.Insert(meter);
        await _meterRepository.SaveChangesAsync(ct);

        return Result.Success(MeterMapper.ToResponse(meter));
    }
}
