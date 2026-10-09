using Application.Common.Interfaces.Repositories;
using Application.CQRS;
using Domain.Common;
using Domain.Common.Entities;
using Features.Meters.Commands;
using Features.Meters.Logging;
using Features.Meters.Mappers;
using Microsoft.Extensions.Logging;

namespace Features.Meters.Handlers;

public class CreateMeterCommandHandler : ICommandHandler<CreateMeterCommand, Result<MeterResponse>>
{
    private readonly IMeterRepository<Meter> _meterRepository;
    private readonly ILocationRepository<Location> _locationRepository;
    private readonly IUserLocationRepository<UserLocation> _userLocationRepository;
    private readonly ILogger<CreateMeterCommandHandler> _logger;

    public CreateMeterCommandHandler(
        IMeterRepository<Meter> meterRepository,
        ILocationRepository<Location> locationRepository,
        IUserLocationRepository<UserLocation> userLocationRepository,
        ILogger<CreateMeterCommandHandler> logger)
    {
        _meterRepository = meterRepository;
        _locationRepository = locationRepository;
        _userLocationRepository = userLocationRepository;
        _logger = logger;
    }

    public async Task<Result<MeterResponse>> Handle(CreateMeterCommand command, CancellationToken ct)
    {
        // Administrators manage every location (active or not); everyone else must own it - a viewer may read a
        // location but not change it. Either way the answer is the same as for a missing location, so
        // non-owners learn nothing about it.
        bool allowed = command.IsAdmin
            ? await _locationRepository.ExistsAsync(l => l.Id == command.LocationId, ct)
            : await _userLocationRepository.IsOwnerAsync(command.UserId, command.LocationId, ct);
        if (!allowed)
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

        if (command.IsAdmin)
        {
            LogMessages.ReaderRegisteredByAdmin(_logger, command.LocationId, command.UserId);
        }

        return Result.Success(MeterMapper.ToResponse(meter));
    }
}
