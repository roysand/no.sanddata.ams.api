using Application.Common.Interfaces.External;
using Application.Common.Interfaces.Repositories;
using Application.CQRS;
using Application.EvChargers;
using Domain.Common;
using Domain.Common.Entities;
using Features.EvChargers.Mappers;
using Features.EvChargers.Queries;
using Features.EvChargers.Services;

namespace Features.EvChargers.Handlers;

/// <summary>
/// The location's car charger state right now (spec 021). An owner, a viewer or an Admin may read it, as the session
/// list (FR-005); everyone else gets the same 404. A location without a charger gets "no charger" and Zaptec isn't
/// asked. Zaptec is read through the shared <see cref="ChargerStateCache"/>, at most every 3 minutes per charger
/// (FR-002). Reads only: nothing is stored, and the figures are never counted (FR-004).
/// </summary>
public class GetChargerStateQueryHandler(
    ILocationRepository<Location> locations,
    IEvChargerRepository<EvCharger> chargers,
    IEvChargerClient client,
    ChargerStateCache cache,
    TimeProvider timeProvider)
    : IQueryHandler<GetChargerStateQuery, Result<ChargerStateResponse>>
{
    public async Task<Result<ChargerStateResponse>> Handle(GetChargerStateQuery query, CancellationToken ct)
    {
        bool mayRead = query.IsAdmin || await locations.IsUserAssociatedAsync(query.UserId, query.LocationId, ct);
        if (!mayRead || await locations.GetByIdAsync(query.LocationId, ct) is null)
        {
            return Result.Failure<ChargerStateResponse>(Error.NotFound("Location.NotFound", "Location not found"));
        }

        EvCharger? charger = await chargers.GetByLocationAsync(query.LocationId, ct);
        if (charger is null)
        {
            return Result.Success(EvChargerMapper.ToStateResponse(ChargerLiveState.NoCharger()));
        }

        if (!client.IsConfigured)
        {
            return Result.Failure<ChargerStateResponse>(EvChargerErrors.NotConfigured);
        }

        Result<(ZaptecChargerState Values, DateTime ReadAtUtc)> read =
            await cache.GetAsync(charger.ChargerId, query.LocationId, ct);
        if (read.IsFailure)
        {
            return Result.Failure<ChargerStateResponse>(read.Error);
        }

        // Worked out now, not when Zaptec was read, so the offline rule uses the time of the request.
        var state =
            ChargerLiveState.From(read.Value.Values, read.Value.ReadAtUtc, timeProvider.GetUtcNow().UtcDateTime);
        return Result.Success(EvChargerMapper.ToStateResponse(state));
    }
}
