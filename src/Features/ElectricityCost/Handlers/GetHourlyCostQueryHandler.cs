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

public class GetHourlyCostQueryHandler(
    ILocationRepository<Location> locationRepository,
    HourlyCostProvider hourlyCostProvider,
    ILogger<GetHourlyCostQueryHandler> logger)
    : IQueryHandler<GetHourlyCostQuery, Result<PagedHourlyCostResponse>>
{
    private static readonly TimeSpan DefaultWindow = TimeSpan.FromHours(24);

    public async Task<Result<PagedHourlyCostResponse>> Handle(GetHourlyCostQuery query, CancellationToken ct)
    {
        if (!await locationRepository.IsUserAssociatedAsync(query.UserId, query.LocationId, ct))
        {
            LogMessages.LocationAccessDenied(logger, query.UserId, query.LocationId);
            return Result.Failure<PagedHourlyCostResponse>(Error.NotFound("Location.NotFound", "Location not found"));
        }

        Location? location = await locationRepository.GetByIdAsync(query.LocationId, ct);
        if (location is null)
        {
            return Result.Failure<PagedHourlyCostResponse>(Error.NotFound("Location.NotFound", "Location not found"));
        }

        DateTime to = query.To is { } toValue ? CostTime.ToUtc(toValue) : DateTime.UtcNow;
        DateTime from = query.From is { } fromValue ? CostTime.ToUtc(fromValue) : to - DefaultWindow;

        IReadOnlyList<HourCost> hours = await hourlyCostProvider.GetAsync(location, from, to, ct);
        IEnumerable<HourCost> page = hours.Skip((query.Page - 1) * query.PageSize).Take(query.PageSize);

        LogMessages.CostQueried(logger, query.UserId, query.LocationId);

        return Result.Success(CostMapper.ToPagedHourlyResponse(page, query.Page, query.PageSize, hours.Count));
    }
}
