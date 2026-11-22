using Application.Common.Interfaces.Repositories;
using Application.CQRS;
using Application.ElectricityCost;
using Application.EvChargers;
using Domain.Common;
using Domain.Common.Entities;
using Features.ElectricityCost.Logging;
using Features.ElectricityCost.Mappers;
using Features.ElectricityCost.Queries;
using Features.ElectricityCost.Services;
using Microsoft.Extensions.Logging;

namespace Features.ElectricityCost.Handlers;

public class GetCurrentHourCostQueryHandler(
    ILocationRepository<Location> locationRepository,
    IConsumptionRepository consumptionRepository,
    CostCalculator costCalculator,
    ILogger<GetCurrentHourCostQueryHandler> logger,
    IChargingSessionRepository? chargingSessions = null)
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
        DateTime monthStart = CostTime.LocalMonthStartUtc(hourStart);
        decimal monthKwhBefore = await consumptionRepository.GetConsumptionKwhAsync(
            query.LocationId, monthStart, hourStart, ct);

        // A car counted with the house counts toward the Norgespris cap (spec 018, FR-004).
        if (location.IncludeEvChargerInTotals && chargingSessions is not null)
        {
            monthKwhBefore += (await chargingSessions.GetHourlyEnergyAsync(location.Id, monthStart, hourStart, ct))
                .Sum(e => e.Kwh);
        }
        CostResult cost = await costCalculator.CalculateAsync(location, hourStart, kwh, monthKwhBefore, ct);

        // The charger only has data here when a session ended in this hour: Zaptec gives completed sessions only.
        EvChargerHourEnergy? charger = chargingSessions is null
            ? null
            : (await chargingSessions.GetHourlyEnergyAsync(location.Id, hourStart, hourStart.AddHours(1), ct))
                .SingleOrDefault();
        EvChargerHour? chargerPart = charger is null
            ? null
            : EvChargerShare.ForHour(charger, location.IncludeEvChargerInTotals);

        LogMessages.CostQueried(logger, query.UserId, query.LocationId);

        return Result.Success(CostMapper.ToCurrentHourResponse(hourStart, kwh, cost, chargerPart));
    }
}
