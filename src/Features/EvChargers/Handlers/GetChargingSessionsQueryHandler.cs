using Application.Common.Interfaces.Repositories;
using Application.CQRS;
using Domain.Common;
using Domain.Common.Entities;
using Features.EvChargers.Mappers;
using Features.EvChargers.Queries;

namespace Features.EvChargers.Handlers;

/// <summary>
/// The location's stored charging sessions, newest first (spec 016, FR-015). An owner, a viewer or an Admin may read
/// them; anyone else gets the same 404 as for a location that does not exist.
/// </summary>
public class GetChargingSessionsQueryHandler(
    ILocationRepository<Location> locations,
    IChargingSessionRepository sessions,
    TimeProvider clock)
    : IQueryHandler<GetChargingSessionsQuery, Result<ChargingSessionsResponse>>
{
    public static readonly TimeSpan DefaultWindow = TimeSpan.FromDays(30);

    public async Task<Result<ChargingSessionsResponse>> Handle(GetChargingSessionsQuery query, CancellationToken ct)
    {
        bool mayRead = query.IsAdmin || await locations.IsUserAssociatedAsync(query.UserId, query.LocationId, ct);
        if (!mayRead || await locations.GetByIdAsync(query.LocationId, ct) is null)
        {
            return Result.Failure<ChargingSessionsResponse>(Error.NotFound("Location.NotFound", "Location not found"));
        }

        DateTime to = query.To is { } t ? t.ToUniversalTime() : clock.GetUtcNow().UtcDateTime;
        DateTime from = query.From is { } f ? f.ToUniversalTime() : to - DefaultWindow;

        IReadOnlyList<ChargingSession> found = await sessions.GetSessionsAsync(query.LocationId, from, to, ct);
        return Result.Success(new ChargingSessionsResponse(found.Select(EvChargerMapper.ToResponse).ToList()));
    }
}
