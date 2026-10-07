using Application.Common.Interfaces.Repositories;
using Application.CQRS;
using Domain.Common;
using Domain.Common.Entities;
using Features.Locations.Commands;
using Features.Locations.Logging;
using Features.Locations.Mappers;
using Features.Locations.Queries;
using Microsoft.Extensions.Logging;

namespace Features.Locations.Handlers;

public class UpdateLocationCommandHandler(
    ILocationRepository<Location> locationRepository,
    ILogger<UpdateLocationCommandHandler> logger)
    : ICommandHandler<UpdateLocationCommand, Result<AdminLocationResponse>>
{
    public async Task<Result<AdminLocationResponse>> Handle(UpdateLocationCommand command, CancellationToken ct)
    {
        Location? location = await locationRepository.GetByIdWithKeyAsync(command.LocationId, ct);
        if (location is null)
        {
            return Result.Failure<AdminLocationResponse>(Error.NotFound("Location.NotFound", "Location not found"));
        }

        if (await locationRepository.SerialNumberExistsAsync(command.SerialNumber, command.LocationId, ct))
        {
            return Result.Failure<AdminLocationResponse>(Error.Conflict(
                "Location.SerialNumberExists", "Another location already uses this serial number"));
        }

        bool activeChanged = location.IsActive != command.IsActive;

        location.Update(command.Name, command.Address, command.SerialNumber, command.Zone, command.HasNorgesPriceAgreement);
        location.SetActive(command.IsActive);
        await locationRepository.SaveChangesAsync(ct);

        LogMessages.LocationUpdated(logger, location.Id, command.ActingUserId);
        if (activeChanged)
        {
            LogMessages.LocationActiveChanged(logger, location.Id, location.IsActive, command.ActingUserId);
        }

        return Result.Success(LocationMapper.ToAdminResponse(location, DateTime.UtcNow));
    }
}
