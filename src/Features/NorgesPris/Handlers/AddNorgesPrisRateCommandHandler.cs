using Application.Common.Interfaces.Repositories;
using Application.CQRS;
using Application.ElectricityCost;
using Domain.Common;
using Domain.Common.Entities;
using Features.NorgesPris.Commands;
using Features.NorgesPris.Logging;
using Features.NorgesPris.Mappers;
using Features.NorgesPris.Queries;
using Microsoft.Extensions.Logging;

namespace Features.NorgesPris.Handlers;

public class AddNorgesPrisRateCommandHandler(
    INorgesPrisRateRepository rates,
    TimeProvider timeProvider,
    ILogger<AddNorgesPrisRateCommandHandler> logger)
    : ICommandHandler<AddNorgesPrisRateCommand, Result<NorgesPrisRateResponse>>
{
    public async Task<Result<NorgesPrisRateResponse>> Handle(AddNorgesPrisRateCommand command, CancellationToken ct)
    {
        if (await rates.DateExistsAsync(command.ValidFrom, ct))
        {
            return Refuse(command, "DateExists", DateExistsError);
        }

        DateOnly today = NorgesPrisSchedule.OsloDateOf(timeProvider.GetUtcNow().UtcDateTime);
        if (command.ValidFrom <= today)
        {
            // A past date may only correct the newest rate: anything older would silently rewrite history that has
            // already been shown.
            IReadOnlyList<NorgesPrisRate> existing = await rates.GetAllAsync(ct);
            if (existing.Count > 0 && command.ValidFrom < existing[^1].ValidFrom)
            {
                return Refuse(command, "NotNewest", Error.Conflict(
                    "NorgesPris.NotNewest",
                    "A rate that starts today or earlier must start after every existing rate"));
            }

            if (!command.ConfirmPastDate)
            {
                return Refuse(command, "ConfirmationRequired", Error.Conflict(
                    "NorgesPris.ConfirmationRequired",
                    "This rate starts today or earlier, so costs shown for past hours will be recalculated. " +
                    "Confirm to add it"));
            }
        }

        var rate = new NorgesPrisRate(
            Guid.NewGuid(), command.ValidFrom, command.RatePerKwh, command.TaxPerKwh, command.ActingUserId);

        // False when a concurrent request added the same date between the check above and the save.
        if (!await rates.TryInsertAsync(rate, ct))
        {
            return Refuse(command, "DateExists", DateExistsError);
        }

        LogMessages.RateAdded(logger, rate.Id, rate.ValidFrom, rate.RatePerKwh, rate.TaxPerKwh, command.ActingUserId);

        IReadOnlyList<NorgesPrisRate> all = await rates.GetAllAsync(ct);
        return Result.Success(NorgesPrisMapper.ToResponses(all, today).Single(r => r.Id == rate.Id));
    }

    private static Error DateExistsError { get; } =
        Error.Conflict("NorgesPris.DateExists", "Another Norgespris rate already starts on this date");

    private Result<NorgesPrisRateResponse> Refuse(AddNorgesPrisRateCommand command, string reason, Error error)
    {
        LogMessages.RateRefused(logger, reason, command.ActingUserId);
        return Result.Failure<NorgesPrisRateResponse>(error);
    }
}
