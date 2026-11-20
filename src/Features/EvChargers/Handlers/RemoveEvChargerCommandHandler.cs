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
/// Deletes the connection, so the charger is no longer read. Stored sessions are kept, and the location's "count the
/// charger with the house" setting is turned off (spec 016, US1 scenario 8).
/// </summary>
public class RemoveEvChargerCommandHandler(
    ILocationRepository<Location> locations,
    IEvChargerRepository<EvCharger> chargers,
    ILogger<RemoveEvChargerCommandHandler> logger)
    : ICommandHandler<RemoveEvChargerCommand, Result<EvChargerRemoved>>
{
    public async Task<Result<EvChargerRemoved>> Handle(RemoveEvChargerCommand command, CancellationToken ct)
    {
        Location? location = await locations.GetByIdAsync(command.LocationId, ct);
        if (location is null)
        {
            return Result.Failure<EvChargerRemoved>(Error.NotFound("Location.NotFound", "Location not found"));
        }

        EvCharger? charger = await chargers.GetByLocationAsync(command.LocationId, ct);
        if (charger is null)
        {
            return Result.Failure<EvChargerRemoved>(EvChargerErrors.NotConnected);
        }

        // Both repositories share the request's DbContext, so Delete (which saves at once) also saves the setting.
        location.SetIncludeEvCharger(false);
        chargers.Delete(charger);
        LogMessages.EvChargerRemoved(logger, command.ActingUserId, command.LocationId);

        return Result.Success(new EvChargerRemoved(command.LocationId));
    }
}
