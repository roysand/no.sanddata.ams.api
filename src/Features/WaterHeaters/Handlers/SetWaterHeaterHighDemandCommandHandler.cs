using Application.Common.Interfaces.External;
using Application.Common.Interfaces.Repositories;
using Application.Common.Interfaces.Security;
using Application.CQRS;
using Application.WaterHeaters;
using Domain.Common;
using Domain.Common.Entities;
using Features.WaterHeaters.Commands;
using Features.WaterHeaters.Logging;
using Features.WaterHeaters.Mappers;
using Microsoft.Extensions.Logging;

namespace Features.WaterHeaters.Handlers;

/// <summary>
/// Turns the heater's High Demand on or off for an Admin (spec 022). The role is checked by the endpoint. The
/// location must exist, active or not, and have a heater. The switch is sent to OSO once, never retried (research R3).
/// Then the state is read back once: a failed read-back still answers success, with no state (FR-004). Nothing is
/// stored (FR-010).
/// </summary>
public class SetWaterHeaterHighDemandCommandHandler(
    ILocationRepository<Location> locations,
    IWaterHeaterRepository<WaterHeater> heaters,
    IWaterHeaterClient client,
    ISecretProtector protector,
    TimeProvider timeProvider,
    ILogger<SetWaterHeaterHighDemandCommandHandler> logger)
    : ICommandHandler<SetWaterHeaterHighDemandCommand, Result<WaterHeaterHighDemandResponse>>
{
    public async Task<Result<WaterHeaterHighDemandResponse>> Handle(
        SetWaterHeaterHighDemandCommand command, CancellationToken ct)
    {
        if (await locations.GetByIdAsync(command.LocationId, ct) is null)
        {
            return Result.Failure<WaterHeaterHighDemandResponse>(
                Error.NotFound("Location.NotFound", "Location not found"));
        }

        WaterHeater? heater = await heaters.GetByLocationAsync(command.LocationId, ct);
        if (heater is null)
        {
            return Result.Failure<WaterHeaterHighDemandResponse>(WaterHeaterErrors.NotConnected);
        }

        Result<string> key = protector.Unprotect(heater.ProtectedSubscriptionKey, heater.LocationId);
        if (key.IsFailure)
        {
            return Failed(command, key.Error);
        }

        var credentials = new WaterHeaterCredentials(heater.LocationId, heater.DeviceId, key.Value);
        Result switched = await client.SetHighDemandAsync(credentials, command.On, ct);
        if (switched.IsFailure)
        {
            return Failed(command, switched.Error);
        }

        Result<WaterHeaterReading> reading = await client.GetStateAsync(credentials, ct);
        LogMessages.WaterHeaterHighDemandSwitched(
            logger, command.ActingUserId, command.LocationId, command.On, reading.IsSuccess);

        return Result.Success(WaterHeaterMapper.ToHighDemandResponse(
            command.LocationId,
            command.On,
            reading.IsSuccess ? reading.Value : null,
            timeProvider.GetUtcNow().UtcDateTime));
    }

    private Result<WaterHeaterHighDemandResponse> Failed(SetWaterHeaterHighDemandCommand command, Error error)
    {
        LogMessages.WaterHeaterHighDemandFailed(
            logger, command.ActingUserId, command.LocationId, command.On, LogMessages.Reason(error));
        return Result.Failure<WaterHeaterHighDemandResponse>(error);
    }
}
