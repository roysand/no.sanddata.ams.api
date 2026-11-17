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

public class ListGridTariffsQueryHandler(
    IPriceRateRepository rates,
    TimeProvider timeProvider,
    ILogger<ListGridTariffsQueryHandler> logger)
    : IQueryHandler<ListGridTariffsQuery, Result<IReadOnlyList<GridTariffResponse>>>
{
    public async Task<Result<IReadOnlyList<GridTariffResponse>>> Handle(ListGridTariffsQuery query, CancellationToken ct)
    {
        IReadOnlyList<GridTariff> tariffs = await rates.GetGridTariffsAsync(ct);
        IReadOnlyList<GridTariffRate> allRates = await rates.GetGridTariffRatesAsync(ct);
        DateOnly today = NorgesPrisSchedule.OsloDateOf(timeProvider.GetUtcNow().UtcDateTime);

        var responses = tariffs
            .Select(t => PricingMapper.ToGridTariffResponse(t, allRates.Where(r => r.GridTariffId == t.Id), today))
            .ToList();
        LogMessages.Listed(logger, "grid tariffs", query.ActingUserId, responses.Count);

        return Result.Success<IReadOnlyList<GridTariffResponse>>(responses);
    }
}

public class CreateGridTariffCommandHandler(
    IPriceRateRepository rates,
    ILogger<CreateGridTariffCommandHandler> logger)
    : ICommandHandler<CreateGridTariffCommand, Result<GridTariffResponse>>
{
    public async Task<Result<GridTariffResponse>> Handle(CreateGridTariffCommand command, CancellationToken ct)
    {
        string name = command.Name.Trim();
        if (await rates.GridTariffNameExistsAsync(name, ct))
        {
            LogMessages.Refused(logger, "TariffNameExists", command.ActingUserId);
            return Result.Failure<GridTariffResponse>(Error.Conflict(
                "Pricing.TariffNameExists", "Another grid tariff already has this name"));
        }

        var tariff = new GridTariff(Guid.NewGuid(), name);
        rates.InsertGridTariff(tariff);
        await rates.SaveChangesAsync(ct);

        LogMessages.GridTariffCreated(logger, tariff.Id, tariff.Name, command.ActingUserId);

        return Result.Success(new GridTariffResponse(tariff.Id, tariff.Name, []));
    }
}

public class AddGridTariffRateCommandHandler(
    IPriceRateRepository rates,
    TimeProvider timeProvider,
    ILogger<AddGridTariffRateCommandHandler> logger)
    : ICommandHandler<AddGridTariffRateCommand, Result<GridTariffRateResponse>>
{
    public async Task<Result<GridTariffRateResponse>> Handle(AddGridTariffRateCommand command, CancellationToken ct)
    {
        if (await rates.GetGridTariffByIdAsync(command.GridTariffId, ct) is null)
        {
            return Refuse(command, Error.NotFound("Pricing.TariffNotFound", "Grid tariff not found"));
        }

        IReadOnlyList<GridTariffRate> all = await rates.GetGridTariffRatesAsync(ct);
        DateOnly today = NorgesPrisSchedule.OsloDateOf(timeProvider.GetUtcNow().UtcDateTime);

        var existingStarts = all
            .Where(r => r.GridTariffId == command.GridTariffId)
            .Select(r => r.ValidFrom)
            .ToList();
        Error? refusal = DatedRatePolicy.Check(existingStarts, command.ValidFrom, command.ConfirmPastDate, today);
        if (refusal is not null)
        {
            return Refuse(command, refusal);
        }

        var rate = new GridTariffRate(
            Guid.NewGuid(), command.GridTariffId, command.ValidFrom, command.DayRatePerKwh, command.NightRatePerKwh,
            command.ActingUserId);

        // False when a concurrent request added the same date between the check above and the save.
        if (!await rates.TryInsertGridTariffRateAsync(rate, ct))
        {
            return Refuse(command, DatedRatePolicy.DateExists);
        }

        LogMessages.GridTariffRateAdded(
            logger, rate.Id, rate.GridTariffId, rate.ValidFrom, rate.DayRatePerKwh, rate.NightRatePerKwh,
            command.ActingUserId);

        IReadOnlyList<GridTariffRate> reloaded = await rates.GetGridTariffRatesAsync(ct);
        return Result.Success(PricingMapper.ToGridTariffRateResponse(
            rate, reloaded.Where(r => r.GridTariffId == command.GridTariffId), today));
    }

    private Result<GridTariffRateResponse> Refuse(AddGridTariffRateCommand command, Error error)
    {
        LogMessages.Refused(logger, error.Code.Replace("Pricing.", string.Empty), command.ActingUserId);
        return Result.Failure<GridTariffRateResponse>(error);
    }
}

public class DeleteGridTariffRateCommandHandler(
    IPriceRateRepository rates,
    TimeProvider timeProvider,
    ILogger<DeleteGridTariffRateCommandHandler> logger)
    : ICommandHandler<DeleteGridTariffRateCommand, Result<PricingDeleted>>
{
    public async Task<Result<PricingDeleted>> Handle(DeleteGridTariffRateCommand command, CancellationToken ct)
    {
        GridTariffRate? rate = await rates.GetGridTariffRateByIdAsync(command.RateId, ct);

        // A rate that belongs to another tariff is not found under this one.
        if (rate is null || rate.GridTariffId != command.GridTariffId)
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

        rates.RemoveGridTariffRate(rate);
        await rates.SaveChangesAsync(ct);

        LogMessages.GridTariffRateDeleted(logger, rate.Id, rate.GridTariffId, rate.ValidFrom, command.ActingUserId);

        return Result.Success(new PricingDeleted(rate.Id));
    }
}
