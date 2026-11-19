using Application.Common.Interfaces.External;
using Application.Common.Interfaces.Repositories;
using Application.Common.Interfaces.Security;
using Application.CQRS;
using Application.WaterHeaters;
using Domain.Common;
using Domain.Common.Entities;
using Features.WaterHeaters.Logging;
using Features.WaterHeaters.Mappers;
using Features.WaterHeaters.Queries;
using Microsoft.Extensions.Logging;

namespace Features.WaterHeaters.Handlers;

/// <summary>
/// Reads the heater's state from OSO at the time of the request. Owners and viewers of an active location may read
/// it, and so may an Admin who is not linked to it (spec 011 US2, research R7); everyone else gets the same 404.
/// Reads only: nothing is stored.
/// </summary>
public class GetWaterHeaterStateQueryHandler(
    ILocationRepository<Location> locations,
    IWaterHeaterRepository<WaterHeater> heaters,
    IWaterHeaterClient client,
    ISecretProtector protector,
    TimeProvider timeProvider,
    ILogger<GetWaterHeaterStateQueryHandler> logger)
    : IQueryHandler<GetWaterHeaterStateQuery, Result<WaterHeaterStateResponse>>
{
    private static readonly Error LocationNotFound = Error.NotFound("Location.NotFound", "Location not found");

    public async Task<Result<WaterHeaterStateResponse>> Handle(GetWaterHeaterStateQuery query, CancellationToken ct)
    {
        bool mayRead = query.IsAdmin
            ? (await locations.GetByIdAsync(query.LocationId, ct))?.IsActive == true
            : await locations.IsUserAssociatedAsync(query.UserId, query.LocationId, ct);
        if (!mayRead)
        {
            LogMessages.LocationAccessDenied(logger, query.UserId, query.LocationId);
            return Result.Failure<WaterHeaterStateResponse>(LocationNotFound);
        }

        WaterHeater? heater = await heaters.GetByLocationAsync(query.LocationId, ct);
        if (heater is null)
        {
            return Result.Failure<WaterHeaterStateResponse>(WaterHeaterErrors.NotConnected);
        }

        Result<string> key = protector.Unprotect(heater.ProtectedSubscriptionKey, heater.LocationId);
        if (key.IsFailure)
        {
            return Failed(query.LocationId, key.Error);
        }

        Result<WaterHeaterReading> reading = await client.GetStateAsync(
            new WaterHeaterCredentials(heater.LocationId, heater.DeviceId, key.Value), ct);
        if (reading.IsFailure)
        {
            return Failed(query.LocationId, reading.Error);
        }

        LogMessages.WaterHeaterStateRead(logger, query.UserId, query.LocationId);
        return Result.Success(
            WaterHeaterMapper.ToState(query.LocationId, reading.Value, timeProvider.GetUtcNow().UtcDateTime));
    }

    private Result<WaterHeaterStateResponse> Failed(Guid locationId, Error error)
    {
        LogMessages.WaterHeaterStateFailed(logger, locationId, LogMessages.Reason(error));
        return Result.Failure<WaterHeaterStateResponse>(error);
    }
}
