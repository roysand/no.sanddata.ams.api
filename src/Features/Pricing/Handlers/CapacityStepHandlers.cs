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

public class ListCapacityStepsQueryHandler(
    IPriceRateRepository rates,
    TimeProvider timeProvider,
    ILogger<ListCapacityStepsQueryHandler> logger)
    : IQueryHandler<ListCapacityStepsQuery, Result<IReadOnlyList<CapacityStepTableResponse>>>
{
    public async Task<Result<IReadOnlyList<CapacityStepTableResponse>>> Handle(
        ListCapacityStepsQuery query, CancellationToken ct)
    {
        if (await rates.GetGridTariffByIdAsync(query.GridTariffId, ct) is null)
        {
            LogMessages.Refused(logger, "TariffNotFound", query.ActingUserId);
            return Result.Failure<IReadOnlyList<CapacityStepTableResponse>>(
                Error.NotFound("Pricing.TariffNotFound", "Grid tariff not found"));
        }

        IReadOnlyList<GridCapacityStep> steps = await rates.GetCapacityStepsAsync(query.GridTariffId, ct);
        DateOnly today = NorgesPrisSchedule.OsloDateOf(timeProvider.GetUtcNow().UtcDateTime);
        IReadOnlyList<CapacityStepTableResponse> tables = PricingMapper.ToCapacityStepTables(steps, today);
        LogMessages.Listed(logger, "capacity step tables", query.ActingUserId, tables.Count);

        return Result.Success(tables);
    }
}

public class AddCapacityStepsCommandHandler(
    IPriceRateRepository rates,
    TimeProvider timeProvider,
    ILogger<AddCapacityStepsCommandHandler> logger)
    : ICommandHandler<AddCapacityStepsCommand, Result<CapacityStepTableResponse>>
{
    public async Task<Result<CapacityStepTableResponse>> Handle(AddCapacityStepsCommand command, CancellationToken ct)
    {
        if (await rates.GetGridTariffByIdAsync(command.GridTariffId, ct) is null)
        {
            return Refuse(command, Error.NotFound("Pricing.TariffNotFound", "Grid tariff not found"));
        }

        IReadOnlyList<GridCapacityStep> existing = await rates.GetCapacityStepsAsync(command.GridTariffId, ct);
        DateOnly today = NorgesPrisSchedule.OsloDateOf(timeProvider.GetUtcNow().UtcDateTime);

        // The rules apply per table: one start date per table.
        var existingStarts = existing.Select(s => s.ValidFrom).Distinct().ToList();
        Error? refusal = DatedRatePolicy.Check(existingStarts, command.ValidFrom, command.ConfirmPastDate, today);
        if (refusal is not null)
        {
            return Refuse(command, refusal);
        }

        var steps = command.Steps
            .Select(s => new GridCapacityStep(
                Guid.NewGuid(), command.GridTariffId, command.ValidFrom, s.LowerKw, s.UpperKw, s.MonthlyAmount,
                command.ActingUserId))
            .ToList();

        // False when a concurrent request added a table with the same date between the check above and the save.
        if (!await rates.TryInsertCapacityStepsAsync(steps, ct))
        {
            return Refuse(command, DatedRatePolicy.DateExists);
        }

        LogMessages.CapacityStepsAdded(
            logger, command.GridTariffId, command.ValidFrom, steps.Count, command.ActingUserId);

        IReadOnlyList<GridCapacityStep> reloaded = await rates.GetCapacityStepsAsync(command.GridTariffId, ct);
        return Result.Success(
            PricingMapper.ToCapacityStepTables(reloaded, today).Single(t => t.ValidFrom == command.ValidFrom));
    }

    private Result<CapacityStepTableResponse> Refuse(AddCapacityStepsCommand command, Error error)
    {
        LogMessages.Refused(logger, error.Code.Replace("Pricing.", string.Empty), command.ActingUserId);
        return Result.Failure<CapacityStepTableResponse>(error);
    }
}

public class DeleteCapacityStepsCommandHandler(
    IPriceRateRepository rates,
    TimeProvider timeProvider,
    ILogger<DeleteCapacityStepsCommandHandler> logger)
    : ICommandHandler<DeleteCapacityStepsCommand, Result<CapacityStepsDeleted>>
{
    public async Task<Result<CapacityStepsDeleted>> Handle(DeleteCapacityStepsCommand command, CancellationToken ct)
    {
        IReadOnlyList<GridCapacityStep> table =
            await rates.GetCapacityStepTableAsync(command.GridTariffId, command.ValidFrom, ct);
        if (table.Count == 0)
        {
            LogMessages.Refused(logger, "NotFound", command.ActingUserId);
            return Result.Failure<CapacityStepsDeleted>(DatedRatePolicy.NotFound);
        }

        DateOnly today = NorgesPrisSchedule.OsloDateOf(timeProvider.GetUtcNow().UtcDateTime);
        if (DatedRatePolicy.HasStarted(command.ValidFrom, today))
        {
            LogMessages.Refused(logger, "AlreadyStarted", command.ActingUserId);
            return Result.Failure<CapacityStepsDeleted>(DatedRatePolicy.AlreadyStarted);
        }

        rates.RemoveCapacitySteps(table);
        await rates.SaveChangesAsync(ct);

        LogMessages.CapacityStepsDeleted(logger, command.GridTariffId, command.ValidFrom, command.ActingUserId);

        return Result.Success(new CapacityStepsDeleted(command.GridTariffId, command.ValidFrom));
    }
}
