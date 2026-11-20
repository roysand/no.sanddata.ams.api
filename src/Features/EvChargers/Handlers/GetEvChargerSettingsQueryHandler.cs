using Application.Common.Interfaces.Repositories;
using Application.CQRS;
using Application.EvChargers;
using Domain.Common;
using Domain.Common.Entities;
using Features.EvChargers.Mappers;
using Features.EvChargers.Queries;

namespace Features.EvChargers.Handlers;

/// <summary>The location's charger connection, from the database only (no call to Zaptec).</summary>
public class GetEvChargerSettingsQueryHandler(
    ILocationRepository<Location> locations,
    IEvChargerRepository<EvCharger> chargers)
    : IQueryHandler<GetEvChargerSettingsQuery, Result<EvChargerSettingsResponse>>
{
    public async Task<Result<EvChargerSettingsResponse>> Handle(GetEvChargerSettingsQuery query, CancellationToken ct)
    {
        Location? location = await locations.GetByIdAsync(query.LocationId, ct);
        if (location is null)
        {
            return Result.Failure<EvChargerSettingsResponse>(Error.NotFound("Location.NotFound", "Location not found"));
        }

        EvCharger? charger = await chargers.GetByLocationAsync(query.LocationId, ct);
        return charger is null
            ? Result.Failure<EvChargerSettingsResponse>(EvChargerErrors.NotConnected)
            : Result.Success(EvChargerMapper.ToSettings(charger, location.IncludeEvChargerInTotals));
    }
}
