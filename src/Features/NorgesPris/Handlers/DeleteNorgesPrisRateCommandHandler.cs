using Application.Common.Interfaces.Repositories;
using Application.CQRS;
using Application.ElectricityCost;
using Domain.Common;
using Domain.Common.Entities;
using Features.NorgesPris.Commands;
using Features.NorgesPris.Logging;
using Microsoft.Extensions.Logging;

namespace Features.NorgesPris.Handlers;

public class DeleteNorgesPrisRateCommandHandler(
    INorgesPrisRateRepository rates,
    TimeProvider timeProvider,
    ILogger<DeleteNorgesPrisRateCommandHandler> logger)
    : ICommandHandler<DeleteNorgesPrisRateCommand, Result<NorgesPrisRateDeleted>>
{
    public async Task<Result<NorgesPrisRateDeleted>> Handle(DeleteNorgesPrisRateCommand command, CancellationToken ct)
    {
        NorgesPrisRate? rate = await rates.GetByIdAsync(command.Id, ct);
        if (rate is null)
        {
            LogMessages.RateRefused(logger, "NotFound", command.ActingUserId);
            return Result.Failure<NorgesPrisRateDeleted>(
                Error.NotFound("NorgesPris.NotFound", "Norgespris rate not found"));
        }

        // A rate that has started (today's included) has priced hours already; only upcoming rates can go. This also
        // means the last remaining rate can never be deleted, because the seeded rate started in 2020.
        DateOnly today = NorgesPrisSchedule.OsloDateOf(timeProvider.GetUtcNow().UtcDateTime);
        if (rate.ValidFrom <= today)
        {
            LogMessages.RateRefused(logger, "AlreadyStarted", command.ActingUserId);
            return Result.Failure<NorgesPrisRateDeleted>(Error.Conflict(
                "NorgesPris.AlreadyStarted", "A rate that has already started cannot be deleted"));
        }

        rates.Remove(rate);
        await rates.SaveChangesAsync(ct);

        LogMessages.RateDeleted(logger, rate.Id, rate.ValidFrom, command.ActingUserId);

        return Result.Success(new NorgesPrisRateDeleted(rate.Id));
    }
}
