using Application.Common.Interfaces.Repositories;
using Application.CQRS;
using Domain.Common;
using Domain.Common.Entities;
using Features.ElectricityCost.Logging;
using Features.ElectricityCost.Queries;
using Features.ElectricityCost.Services;
using Microsoft.Extensions.Logging;

namespace Features.ElectricityCost.Handlers;

/// <summary>
/// What Norgespris has saved compared with spot terms for today, the last 7 days, the month so far and the year so
/// far. One pass over the hours from the earliest period start, priced by the same calculation as every other cost.
/// </summary>
public class GetNorgesPrisSavingQueryHandler(
    ILocationRepository<Location> locationRepository,
    HourlyCostProvider hourlyCostProvider,
    TimeProvider timeProvider,
    ILogger<GetNorgesPrisSavingQueryHandler> logger)
    : IQueryHandler<GetNorgesPrisSavingQuery, Result<NorgesPrisSavingResponse>>
{
    public async Task<Result<NorgesPrisSavingResponse>> Handle(GetNorgesPrisSavingQuery query, CancellationToken ct)
    {
        if (!await locationRepository.IsUserAssociatedAsync(query.UserId, query.LocationId, ct))
        {
            LogMessages.LocationAccessDenied(logger, query.UserId, query.LocationId);
            return Result.Failure<NorgesPrisSavingResponse>(Error.NotFound("Location.NotFound", "Location not found"));
        }

        Location? location = await locationRepository.GetByIdAsync(query.LocationId, ct);
        if (location is null)
        {
            return Result.Failure<NorgesPrisSavingResponse>(Error.NotFound("Location.NotFound", "Location not found"));
        }

        DateTime today = CostTime.LocalDateOf(timeProvider.GetUtcNow().UtcDateTime);
        IReadOnlyList<HourCost> hours = await hourlyCostProvider.GetAsync(
            location,
            CostTime.LocalDayStartUtc(SavingCalculator.EarliestStart(today)),
            CostTime.LocalDayStartUtc(today.AddDays(1)),
            ct);

        LogMessages.CostQueried(logger, query.UserId, query.LocationId);

        return Result.Success(SavingCalculator.Calculate(hours, today));
    }
}
