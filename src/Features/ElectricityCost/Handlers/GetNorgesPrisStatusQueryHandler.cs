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

/// <summary>
/// The Norgespris limit status of every active Norgespris location in the user's location list (spec 018). The kWh
/// are counted as the cost calculation counts them for the cap, and the projection is the bill's, through the same
/// code; nothing is priced, so it costs two queries per location (research R11).
/// </summary>
public class GetNorgesPrisStatusQueryHandler(
    ILocationRepository<Location> locationRepository,
    IConsumptionRepository consumptionRepository,
    CostCalculator costCalculator,
    TimeProvider timeProvider,
    ILogger<GetNorgesPrisStatusQueryHandler> logger,
    IChargingSessionRepository? chargingSessions = null)
    : IQueryHandler<GetNorgesPrisStatusQuery, Result<NorgesPrisStatusResponse>>
{
    public async Task<Result<NorgesPrisStatusResponse>> Handle(GetNorgesPrisStatusQuery query, CancellationToken ct)
    {
        DateTime nowUtc = timeProvider.GetUtcNow().UtcDateTime;
        var currentHourStartUtc = new DateTime(
            nowUtc.Year, nowUtc.Month, nowUtc.Day, nowUtc.Hour, 0, 0, DateTimeKind.Utc);
        DateTime today = CostTime.LocalDateOf(nowUtc);
        var month = new DateOnly(today.Year, today.Month, 1);
        DateTime monthStartUtc = CostTime.LocalDayStartUtc(month.ToDateTime(TimeOnly.MinValue));
        DateTime monthEndUtc = CostTime.LocalDayStartUtc(month.AddMonths(1).ToDateTime(TimeOnly.MinValue));
        DateTime windowStartUtc = currentHourStartUtc.AddHours(-ConsumptionProjection.WindowHours);
        DateTime readFromUtc = windowStartUtc < monthStartUtc ? windowStartUtc : monthStartUtc;

        // The same list as GET /api/locations, so the warnings match the locations the user can switch to (R2).
        IReadOnlyList<LocationWithRole> locations = await locationRepository.GetForUserWithRoleAsync(query.UserId, ct);

        var items = new List<NorgesPrisStatusItem>();
        foreach (Location location in locations.Select(l => l.Location)
                     .Where(l => l.IsActive && l.HasNorgesPriceAgreement))
        {
            IReadOnlyList<HourConsumption> rows =
                await consumptionRepository.GetHourlyAsync(location.Id, null, readFromUtc, nowUtc, ct);
            var inMonth = rows
                .Select(r => (Hour: DateTime.SpecifyKind(r.BucketStart, DateTimeKind.Utc), r.ConsumptionKwh))
                .Where(r => r.Hour >= monthStartUtc)
                .ToList();

            // The car counts toward the cap when it is counted with the house (FR-004, R4).
            IReadOnlyList<EvChargerHourEnergy> car = location.IncludeEvChargerInTotals && chargingSessions is not null
                ? await chargingSessions.GetHourlyEnergyAsync(location.Id, monthStartUtc, nowUtc, ct)
                : [];

            // So far: up to now, the running hour's readings included (R4).
            decimal kwhSoFar = inMonth.Sum(r => r.ConsumptionKwh) + car.Sum(e => e.Kwh);

            // Projected: the bill's figure, from the kWh before the current hour plus the projected rest (R5).
            ProjectionResult projection =
                ConsumptionProjection.ProjectRestOfMonth(rows, currentHourStartUtc, monthEndUtc);
            decimal? projectedKwh = projection.BasedOnHours == 0
                ? null
                : inMonth.Where(r => r.Hour < currentHourStartUtc).Sum(r => r.ConsumptionKwh)
                  + car.Where(e => e.HourStartUtc < currentHourStartUtc).Sum(e => e.Kwh)
                  + projection.Hours.Sum(h => h.Kwh);

            items.Add(NorgesPrisStatusMapper.ToItem(
                location.Id,
                month,
                await costCalculator.NorgesPrisCapAsync(location, nowUtc, ct),
                kwhSoFar,
                projectedKwh,
                await costCalculator.NorgesPrisPriceWithVatAsync(location, nowUtc, ct),
                monthEndUtc));
        }

        LogMessages.NorgesPrisStatusQueried(logger, query.UserId, items.Count);

        return Result.Success(new NorgesPrisStatusResponse(nowUtc, items));
    }
}
