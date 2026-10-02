using Application.Common.Interfaces.Repositories;
using Application.CQRS;
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

        // Days are UTC calendar days; 'to' includes its whole day.
        DateTime lastDay = query.To is { } toValue ? CostTime.ToUtc(toValue).Date : DateTime.UtcNow.Date;
        DateTime firstDay = query.From is { } fromValue
            ? CostTime.ToUtc(fromValue).Date
            : lastDay.AddDays(-(DefaultDays - 1));

        IReadOnlyList<HourCost> hours =
            await hourlyCostProvider.GetAsync(location, DateTime.SpecifyKind(firstDay, DateTimeKind.Utc),
                DateTime.SpecifyKind(lastDay.AddDays(1), DateTimeKind.Utc), ct);

        List<DailyCostItemResponse> items = hours
            .GroupBy(h => h.HourStart.Date)
            .OrderBy(g => g.Key)
            .Select(g => CostMapper.ToDailyResponse(DateOnly.FromDateTime(g.Key), g.ToList()))
            .ToList();

        LogMessages.CostQueried(logger, query.UserId, query.LocationId);

        return Result.Success(new DailyCostResponse(items));
    }
}
