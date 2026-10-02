using Application.Common.Interfaces.Repositories;
using Application.CQRS;
using Application.ElectricityCost;
using Domain.Common;
using Domain.Common.Entities;
using Features.ElectricityCost.Logging;
using Features.ElectricityCost.Mappers;
using Features.ElectricityCost.Queries;
using Microsoft.Extensions.Logging;

namespace Features.ElectricityCost.Handlers;

public class GetCurrentHourCostQueryHandler(
    ILocationRepository<Location> locationRepository,
    IConsumptionRepository consumptionRepository,
    CostCalculator costCalculator,
    ILogger<GetCurrentHourCostQueryHandler> logger)
    : IQueryHandler<GetCurrentHourCostQuery, Result<CurrentHourCostResponse>>
{
    public async Task<Result<CurrentHourCostResponse>> Handle(GetCurrentHourCostQuery query, CancellationToken ct)
    {
        if (!await locationRepository.IsUserAssociatedAsync(query.UserId, query.LocationId, ct))
        {
            LogMessages.LocationAccessDenied(logger, query.UserId, query.LocationId);
            return Result.Failure<CurrentHourCostResponse>(Error.NotFound("Location.NotFound", "Location not found"));
        }

        Location? location = await locationRepository.GetByIdAsync(query.LocationId, ct);
        if (location is null)
        {
            return Result.Failure<CurrentHourCostResponse>(Error.NotFound("Location.NotFound", "Location not found"));
        }

        DateTime now = DateTime.UtcNow;
        var hourStart = new DateTime(now.Year, now.Month, now.Day, now.Hour, 0, 0, DateTimeKind.Utc);

        decimal kwh = await consumptionRepository.GetHourConsumptionKwhAsync(query.LocationId, hourStart, ct);
        CostResult cost = await costCalculator.CalculateAsync(location, hourStart, kwh, ct);

        LogMessages.CostQueried(logger, query.UserId, query.LocationId);

        return Result.Success(CostMapper.ToCurrentHourResponse(hourStart, kwh, cost));
    }
}
