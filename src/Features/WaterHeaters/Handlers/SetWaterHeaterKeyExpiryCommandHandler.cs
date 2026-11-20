using Application.Common.Interfaces.Repositories;
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
/// Changes only the OSO key's expiry date (spec 015, FR-004), for example after a typo or when OSO extends the key. The
/// key is not needed and OSO is not called.
/// </summary>
public class SetWaterHeaterKeyExpiryCommandHandler(
    IWaterHeaterRepository<WaterHeater> heaters,
    TimeProvider clock,
    ILogger<SetWaterHeaterKeyExpiryCommandHandler> logger)
    : ICommandHandler<SetWaterHeaterKeyExpiryCommand, Result<WaterHeaterSettingsResponse>>
{
    public async Task<Result<WaterHeaterSettingsResponse>> Handle(SetWaterHeaterKeyExpiryCommand command, CancellationToken ct)
    {
        WaterHeater? heater = await heaters.GetByLocationAsync(command.LocationId, ct);
        if (heater is null)
        {
            return Result.Failure<WaterHeaterSettingsResponse>(WaterHeaterErrors.NotConnected);
        }

        if (!WaterHeaterKeyExpiry.IsInRange(command.SubscriptionKeyExpiresOn, WaterHeaterKeyExpiry.Today(clock)))
        {
            return Result.Failure<WaterHeaterSettingsResponse>(WaterHeaterErrors.KeyExpiryOutOfRange);
        }

        heater.SetKeyExpiry(command.SubscriptionKeyExpiresOn);
        await heaters.SaveChangesAsync(ct);
        LogMessages.WaterHeaterKeyExpiryChanged(logger, command.ActingUserId, command.LocationId,
            command.SubscriptionKeyExpiresOn);

        return Result.Success(WaterHeaterMapper.ToSettings(heater));
    }
}
