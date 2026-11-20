using Application.Common.Interfaces.External;
using Application.Common.Interfaces.Repositories;
using Application.CQRS;
using Application.EvChargers;
using Domain.Common;
using Domain.Common.Entities;
using Features.EvChargers.Commands;
using Features.EvChargers.Logging;
using Features.EvChargers.Mappers;
using Features.EvChargers.Queries;
using Microsoft.Extensions.Logging;

namespace Features.EvChargers.Handlers;

/// <summary>
/// Checks with Zaptec that the server's sign-in can see the charger before anything is saved, then stores its ids and
/// names. Connecting another charger replaces the old one and reads its history back from the start.
/// </summary>
public class ConnectEvChargerCommandHandler(
    ILocationRepository<Location> locations,
    IEvChargerRepository<EvCharger> chargers,
    IEvChargerClient client,
    ILogger<ConnectEvChargerCommandHandler> logger)
    : ICommandHandler<ConnectEvChargerCommand, Result<EvChargerSettingsResponse>>
{
    public async Task<Result<EvChargerSettingsResponse>> Handle(ConnectEvChargerCommand command, CancellationToken ct)
    {
        Result<EvChargerSettingsResponse> result = await ConnectAsync(command, ct);
        if (result.IsFailure)
        {
            LogMessages.EvChargerConnectRefused(logger, command.ActingUserId, command.LocationId,
                LogMessages.Reason(result.Error));
        }

        return result;
    }

    private async Task<Result<EvChargerSettingsResponse>> ConnectAsync(ConnectEvChargerCommand command, CancellationToken ct)
    {
        Location? location = await locations.GetByIdAsync(command.LocationId, ct);
        if (location is null)
        {
            return Result.Failure<EvChargerSettingsResponse>(Error.NotFound("Location.NotFound", "Location not found"));
        }

        EvCharger? sameCharger = await chargers.GetByChargerIdAsync(command.ChargerId, ct);
        if (sameCharger is not null && sameCharger.LocationId != command.LocationId)
        {
            return Result.Failure<EvChargerSettingsResponse>(EvChargerErrors.ChargerInUse);
        }

        Result<IReadOnlyList<ZaptecCharger>> visible = await client.GetChargersAsync(ct);
        if (visible.IsFailure)
        {
            return Result.Failure<EvChargerSettingsResponse>(visible.Error);
        }

        ZaptecCharger? found = visible.Value.FirstOrDefault(c => c.Id == command.ChargerId);
        if (found is null)
        {
            return Result.Failure<EvChargerSettingsResponse>(EvChargerErrors.UnknownCharger);
        }

        EvCharger? existing = await chargers.GetByLocationAsync(command.LocationId, ct);
        EvCharger charger;
        if (existing is null)
        {
            charger = new EvCharger(
                Guid.NewGuid(), command.LocationId, found.Id, found.Name, found.InstallationId, found.InstallationName);
            chargers.Insert(charger);
        }
        else
        {
            if (existing.ChargerId == found.Id)
            {
                // The same charger again: refresh the names, keep the watermark.
                DateTime? readUpTo = existing.ReadUpToUtc;
                existing.Replace(found.Id, found.Name, found.InstallationId, found.InstallationName);
                if (readUpTo is { } kept)
                {
                    existing.MarkReadUpTo(kept);
                }
            }
            else
            {
                existing.Replace(found.Id, found.Name, found.InstallationId, found.InstallationName);
            }

            charger = existing;
        }

        await chargers.SaveChangesAsync(ct);
        LogMessages.EvChargerConnected(logger, command.ActingUserId, command.LocationId, existing is not null);

        return Result.Success(EvChargerMapper.ToSettings(charger, location.IncludeEvChargerInTotals));
    }
}
