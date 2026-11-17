using Application.Common.Interfaces.Repositories;
using Application.CQRS;
using Application.ElectricityCost;
using Domain.Common;
using Domain.Common.Entities;
using Features.Pricing.Commands;
using Features.Pricing.Logging;
using Features.Pricing.Mappers;
using Features.Pricing.Queries;
using Microsoft.Extensions.Logging;

namespace Features.Pricing.Handlers;

public class ListPriceRatesQueryHandler(
    IPriceRateRepository rates,
    TimeProvider timeProvider,
    ILogger<ListPriceRatesQueryHandler> logger)
    : IQueryHandler<ListPriceRatesQuery, Result<IReadOnlyList<PriceRateResponse>>>
{
    public async Task<Result<IReadOnlyList<PriceRateResponse>>> Handle(ListPriceRatesQuery query, CancellationToken ct)
    {
        IReadOnlyList<PriceRate> all = await rates.GetPriceRatesAsync(ct);
        DateOnly today = NorgesPrisSchedule.OsloDateOf(timeProvider.GetUtcNow().UtcDateTime);

        IReadOnlyList<PriceRateResponse> responses = PricingMapper.ToPriceRateResponses(all, today, query.Kind);
        LogMessages.Listed(logger, "price rates", query.ActingUserId, responses.Count);

        return Result.Success(responses);
    }
}

public class AddPriceRateCommandHandler(
    IPriceRateRepository rates,
    TimeProvider timeProvider,
    ILogger<AddPriceRateCommandHandler> logger)
    : ICommandHandler<AddPriceRateCommand, Result<PriceRateResponse>>
{
    public async Task<Result<PriceRateResponse>> Handle(AddPriceRateCommand command, CancellationToken ct)
    {
        IReadOnlyList<PriceRate> all = await rates.GetPriceRatesAsync(ct);
        DateOnly today = NorgesPrisSchedule.OsloDateOf(timeProvider.GetUtcNow().UtcDateTime);

        var existingStarts = all.Where(r => r.Kind == command.Kind).Select(r => r.ValidFrom).ToList();
        Error? refusal = DatedRatePolicy.Check(existingStarts, command.ValidFrom, command.ConfirmPastDate, today);
        if (refusal is not null)
        {
            return Refuse(command, refusal);
        }

        var rate = new PriceRate(Guid.NewGuid(), command.Kind, command.ValidFrom, command.Value, command.ActingUserId);

        // False when a concurrent request added the same date between the check above and the save.
        if (!await rates.TryInsertPriceRateAsync(rate, ct))
        {
            return Refuse(command, DatedRatePolicy.DateExists);
        }

        LogMessages.PriceRateAdded(
            logger, rate.Id, rate.Kind.ToString(), rate.ValidFrom, rate.Value, command.ActingUserId);

        IReadOnlyList<PriceRate> reloaded = await rates.GetPriceRatesAsync(ct);
        return Result.Success(PricingMapper.ToPriceRateResponses(reloaded, today, command.Kind).Single(r => r.Id == rate.Id));
    }

    private Result<PriceRateResponse> Refuse(AddPriceRateCommand command, Error error)
    {
        LogMessages.Refused(logger, error.Code.Replace("Pricing.", string.Empty), command.ActingUserId);
        return Result.Failure<PriceRateResponse>(error);
    }
}

public class DeletePriceRateCommandHandler(
    IPriceRateRepository rates,
    TimeProvider timeProvider,
    ILogger<DeletePriceRateCommandHandler> logger)
    : ICommandHandler<DeletePriceRateCommand, Result<PricingDeleted>>
{
    public async Task<Result<PricingDeleted>> Handle(DeletePriceRateCommand command, CancellationToken ct)
    {
        PriceRate? rate = await rates.GetPriceRateByIdAsync(command.Id, ct);
        if (rate is null)
        {
            LogMessages.Refused(logger, "NotFound", command.ActingUserId);
            return Result.Failure<PricingDeleted>(DatedRatePolicy.NotFound);
        }

        DateOnly today = NorgesPrisSchedule.OsloDateOf(timeProvider.GetUtcNow().UtcDateTime);
        if (DatedRatePolicy.HasStarted(rate.ValidFrom, today))
        {
            LogMessages.Refused(logger, "AlreadyStarted", command.ActingUserId);
            return Result.Failure<PricingDeleted>(DatedRatePolicy.AlreadyStarted);
        }

        rates.RemovePriceRate(rate);
        await rates.SaveChangesAsync(ct);

        LogMessages.PriceRateDeleted(logger, rate.Id, rate.Kind.ToString(), rate.ValidFrom, command.ActingUserId);

        return Result.Success(new PricingDeleted(rate.Id));
    }
}
