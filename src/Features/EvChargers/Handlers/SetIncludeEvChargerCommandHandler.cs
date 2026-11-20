using Application.Common.Interfaces.Repositories;
using Application.CQRS;
using Application.EvChargers;
using Domain.Common;
using Domain.Common.Entities;
using Features.EvChargers.Commands;
using Features.EvChargers.Logging;
using Microsoft.Extensions.Logging;

namespace Features.EvChargers.Handlers;

/// <summary>
/// The "count the charger with the house" checkbox (spec 016, US3, research R9). An owner of the location or an Admin
/// may change it; a viewer gets Forbidden (403: they can see the location, so it reveals nothing); anyone else gets
/// 404. Turning it on needs a charger connected now or sessions stored from one earlier.
/// </summary>
public class SetIncludeEvChargerCommandHandler(
    ILocationRepository<Location> locations,
    IUserLocationRepository<UserLocation> links,
    IEvChargerRepository<EvCharger> chargers,
    IChargingSessionRepository sessions,
    ILogger<SetIncludeEvChargerCommandHandler> logger)
    : ICommandHandler<SetIncludeEvChargerCommand, Result<EvChargerIncludeResponse>>
{
    public async Task<Result<EvChargerIncludeResponse>> Handle(SetIncludeEvChargerCommand command, CancellationToken ct)
    {
        if (!command.IsAdmin && !await links.IsOwnerAsync(command.ActingUserId, command.LocationId, ct))
        {
            return await locations.IsUserAssociatedAsync(command.ActingUserId, command.LocationId, ct)
                ? Result.Failure<EvChargerIncludeResponse>(EvChargerErrors.Forbidden)
                : Result.Failure<EvChargerIncludeResponse>(Error.NotFound("Location.NotFound", "Location not found"));
        }

        Location? location = await locations.GetByIdAsync(command.LocationId, ct);
        if (location is null)
        {
            return Result.Failure<EvChargerIncludeResponse>(Error.NotFound("Location.NotFound", "Location not found"));
        }

        if (command.Include
            && await chargers.GetByLocationAsync(command.LocationId, ct) is null
            && !await sessions.AnyForLocationAsync(command.LocationId, ct))
        {
            return Result.Failure<EvChargerIncludeResponse>(EvChargerErrors.NotConnected);
        }

        location.SetIncludeEvCharger(command.Include);
        await locations.SaveChangesAsync(ct);
        LogMessages.EvChargerIncludeChanged(logger, command.LocationId, command.Include, command.ActingUserId);

        return Result.Success(new EvChargerIncludeResponse(command.LocationId, command.Include));
    }
}
