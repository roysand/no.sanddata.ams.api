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

public class UpdateOwnLocationCommandHandler(
    ILocationRepository<Location> locationRepository,
    IUserLocationRepository<UserLocation> userLocationRepository,
    ILogger<UpdateOwnLocationCommandHandler> logger)
    : ICommandHandler<UpdateOwnLocationCommand, Result<LocationSummaryResponse>>
{
    public async Task<Result<LocationSummaryResponse>> Handle(UpdateOwnLocationCommand command, CancellationToken ct)
    {
        // A viewer, a stranger and a missing location all get the same answer, so nobody learns that a location exists.
        // Owners may edit an inactive location - that is how they switch it on again.
        Location? location = await userLocationRepository.IsOwnerAsync(command.ActingUserId, command.LocationId, ct)
            ? await locationRepository.GetByIdWithKeyAsync(command.LocationId, ct)
            : null;
        if (location is null)
        {
            return Result.Failure<LocationSummaryResponse>(Error.NotFound("Location.NotFound", "Location not found"));
        }

        bool activeChanged = location.IsActive != command.IsActive;

        location.UpdateDetails(command.Name, command.Address);
        location.SetActive(command.IsActive);
        await locationRepository.SaveChangesAsync(ct);

        LogMessages.LocationUpdated(logger, location.Id, command.ActingUserId);
        if (activeChanged)
        {
            LogMessages.LocationActiveChanged(logger, location.Id, location.IsActive, command.ActingUserId);
        }

        return Result.Success(LocationMapper.ToResponse(location, LocationRole.Owner));
    }
}
