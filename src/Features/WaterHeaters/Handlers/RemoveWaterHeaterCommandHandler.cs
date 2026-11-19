using Application.Common.Interfaces.Repositories;
using Application.CQRS;
using Application.WaterHeaters;
using Domain.Common;
using Domain.Common.Entities;
using Features.WaterHeaters.Commands;
using Features.WaterHeaters.Logging;
using Microsoft.Extensions.Logging;

namespace Features.WaterHeaters.Handlers;

/// <summary>Deletes the connection (and with it the key and device id). Stored power samples are kept.</summary>
public class RemoveWaterHeaterCommandHandler(
    ILocationRepository<Location> locations,
    IWaterHeaterRepository<WaterHeater> heaters,
    ILogger<RemoveWaterHeaterCommandHandler> logger)
    : ICommandHandler<RemoveWaterHeaterCommand, Result<WaterHeaterRemoved>>
{
    public async Task<Result<WaterHeaterRemoved>> Handle(RemoveWaterHeaterCommand command, CancellationToken ct)
    {
        if (await locations.GetByIdAsync(command.LocationId, ct) is null)
        {
            return Result.Failure<WaterHeaterRemoved>(Error.NotFound("Location.NotFound", "Location not found"));
        }

        WaterHeater? heater = await heaters.GetByLocationAsync(command.LocationId, ct);
        if (heater is null)
        {
            return Result.Failure<WaterHeaterRemoved>(WaterHeaterErrors.NotConnected);
        }

        // GenericEfRepository.Delete saves immediately.
        heaters.Delete(heater);
        LogMessages.WaterHeaterRemoved(logger, command.ActingUserId, command.LocationId);

        return Result.Success(new WaterHeaterRemoved(command.LocationId));
    }
}
