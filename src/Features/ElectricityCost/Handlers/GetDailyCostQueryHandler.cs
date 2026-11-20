using Application.Common.Interfaces.Repositories;
using Application.CQRS;
using Application.EvChargers;
using Domain.Common;
using Domain.Common.Entities;
using Features.ElectricityCost.Logging;
using Features.ElectricityCost.Mappers;
using Features.ElectricityCost.Queries;
using Features.ElectricityCost.Services;
using Microsoft.Extensions.Logging;

namespace Features.ElectricityCost.Handlers;

public class GetDailyCostQueryHandler(
    ILocationRepository<Location> locationRepository,
    HourlyCostProvider hourlyCostProvider,
    ILogger<GetDailyCostQueryHandler> logger)
    : IQueryHandler<GetDailyCostQuery, Result<DailyCostResponse>>
{
    private const int DefaultDays = 7;

    public async Task<Result<DailyCostResponse>> Handle(GetDailyCostQuery query, CancellationToken ct)
    {
        if (!await locationRepository.IsUserAssociatedAsync(query.UserId, query.LocationId, ct))
        {
            LogMessages.LocationAccessDenied(logger, query.UserId, query.LocationId);
            return Result.Failure<DailyCostResponse>(Error.NotFound("Location.NotFound", "Location not found"));
        }

        Location? location = await locationRepository.GetByIdAsync(query.LocationId, ct);
        if (location is null)
        {
            return Result.Failure<DailyCostResponse>(Error.NotFound("Location.NotFound", "Location not found"));
        }

        // Days are Oslo calendar days (local midnight to local midnight); 'to' includes its whole day.
        DateTime lastDay = query.To is { } toValue ? CostTime.LocalDate(toValue) : CostTime.LocalDateOf(DateTime.UtcNow);
        DateTime firstDay = query.From is { } fromValue
            ? CostTime.LocalDate(fromValue)
            : lastDay.AddDays(-(DefaultDays - 1));

        HourlyCosts costs = await hourlyCostProvider.GetWithEvChargerAsync(
            location, CostTime.LocalDayStartUtc(firstDay), CostTime.LocalDayStartUtc(lastDay.AddDays(1)), ct,
            withWaterHeater: true);

        // Charging hours without a meter reading count in their day's charger kWh (spec 016, research R7). A day with
        // no meter reading at all has no item, as before.
        var unmatchedByDay = costs.UnmatchedChargerHours
            .GroupBy(h => CostTime.LocalDateOf(h.HourStartUtc))
            .ToDictionary(g => g.Key, g => (IReadOnlyCollection<EvChargerHour>)g.ToList());

        var items = costs.Hours
            .GroupBy(h => CostTime.LocalDateOf(h.HourStart))
            .OrderBy(g => g.Key)
            .Select(g => CostMapper.ToDailyResponse(
                DateOnly.FromDateTime(g.Key), g.ToList(), unmatchedByDay.GetValueOrDefault(g.Key)))
            .ToList();

        LogMessages.CostQueried(logger, query.UserId, query.LocationId);

        return Result.Success(new DailyCostResponse(items));
    }
}
