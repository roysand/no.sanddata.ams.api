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
using Features.WaterHeaters.Queries;
using Microsoft.Extensions.Logging;

namespace Features.WaterHeaters.Handlers;

/// <summary>
/// Checks the key and device id with OSO before anything is saved, then stores the device id and the key encrypted.
/// Connecting again replaces the old values.
/// </summary>
public class ConnectWaterHeaterCommandHandler(
    ILocationRepository<Location> locations,
    IWaterHeaterRepository<WaterHeater> heaters,
    IWaterHeaterClient client,
    ISecretProtector protector,
    ILogger<ConnectWaterHeaterCommandHandler> logger)
    : ICommandHandler<ConnectWaterHeaterCommand, Result<WaterHeaterSettingsResponse>>
{
    public async Task<Result<WaterHeaterSettingsResponse>> Handle(ConnectWaterHeaterCommand command, CancellationToken ct)
    {
        Result<WaterHeaterSettingsResponse> result = await ConnectAsync(command, ct);
        if (result.IsFailure)
        {
            LogMessages.WaterHeaterConnectRefused(logger, command.ActingUserId, command.LocationId,
                LogMessages.Reason(result.Error));
        }

        return result;
    }

    private async Task<Result<WaterHeaterSettingsResponse>> ConnectAsync(ConnectWaterHeaterCommand command, CancellationToken ct)
    {
        if (await locations.GetByIdAsync(command.LocationId, ct) is null)
        {
            return Result.Failure<WaterHeaterSettingsResponse>(Error.NotFound("Location.NotFound", "Location not found"));
        }

        if (!protector.IsConfigured)
        {
            return Result.Failure<WaterHeaterSettingsResponse>(WaterHeaterErrors.EncryptionNotConfigured);
        }

        WaterHeater? sameDevice = await heaters.GetByDeviceIdAsync(command.DeviceId, ct);
        if (sameDevice is not null && sameDevice.LocationId != command.LocationId)
        {
            return Result.Failure<WaterHeaterSettingsResponse>(WaterHeaterErrors.DeviceInUse);
        }

        var credentials = new WaterHeaterCredentials(command.LocationId, command.DeviceId, command.SubscriptionKey);
        Result<WaterHeaterReading> check = await client.GetStateAsync(credentials, ct);
        if (check.IsFailure)
        {
            return Result.Failure<WaterHeaterSettingsResponse>(check.Error);
        }

        string protectedKey = protector.Protect(command.SubscriptionKey, command.LocationId);
        WaterHeater? existing = await heaters.GetByLocationAsync(command.LocationId, ct);
        WaterHeater heater;
        if (existing is null)
        {
            heater = new WaterHeater(Guid.NewGuid(), command.LocationId, command.DeviceId, protectedKey);
            heaters.Insert(heater);
        }
        else
        {
            existing.Replace(command.DeviceId, protectedKey);
            heater = existing;
        }

        await heaters.SaveChangesAsync(ct);
        LogMessages.WaterHeaterConnected(logger, command.ActingUserId, command.LocationId, existing is not null);

        return Result.Success(WaterHeaterMapper.ToSettings(heater));
    }
}
