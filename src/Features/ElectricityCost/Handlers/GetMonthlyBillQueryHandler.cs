using Application.Common.Interfaces.Repositories;
using Application.CQRS;
using Application.ElectricityCost;
using Application.EvChargers;
using Application.WaterHeaters;
using Domain.Common;
using Domain.Common.Entities;
using Features.ElectricityCost.Logging;
using Features.ElectricityCost.Mappers;
using Features.ElectricityCost.Queries;
using Features.ElectricityCost.Services;
using Microsoft.Extensions.Logging;

namespace Features.ElectricityCost.Handlers;

/// <summary>
/// The bill of a location for an Oslo calendar month, as the invoice shows it: the supplier part (Strøm) and the grid
/// part (Nettleie). Every hour is priced by the one calculation, so the bill always agrees with the hourly costs. For
/// the current month the rest of the month is projected from the last 7 days and priced the same way.
/// </summary>
public class GetMonthlyBillQueryHandler(
    ILocationRepository<Location> locationRepository,
    IConsumptionRepository consumptionRepository,
    IPriceRateRepository priceRates,
    HourlyCostProvider hourlyCostProvider,
    CostCalculator costCalculator,
    TimeProvider timeProvider,
    ILogger<GetMonthlyBillQueryHandler> logger)
    : IQueryHandler<GetMonthlyBillQuery, Result<MonthlyBillResponse>>
{
    public async Task<Result<MonthlyBillResponse>> Handle(GetMonthlyBillQuery query, CancellationToken ct)
    {
        if (!await locationRepository.IsUserAssociatedAsync(query.UserId, query.LocationId, ct))
        {
            LogMessages.LocationAccessDenied(logger, query.UserId, query.LocationId);
            return Result.Failure<MonthlyBillResponse>(Error.NotFound("Location.NotFound", "Location not found"));
        }

        Location? location = await locationRepository.GetByIdAsync(query.LocationId, ct);
        if (location is null)
        {
            return Result.Failure<MonthlyBillResponse>(Error.NotFound("Location.NotFound", "Location not found"));
        }

        DateTime nowUtc = timeProvider.GetUtcNow().UtcDateTime;
        DateOnly anyDay = query.Month ?? DateOnly.FromDateTime(CostTime.LocalDateOf(nowUtc));
        DateOnly month = new(anyDay.Year, anyDay.Month, 1);
        DateTime monthStartUtc = CostTime.LocalDayStartUtc(month.ToDateTime(TimeOnly.MinValue));
        DateTime monthEndUtc = CostTime.LocalDayStartUtc(month.AddMonths(1).ToDateTime(TimeOnly.MinValue));
        if (monthStartUtc > nowUtc)
        {
            return Result.Failure<MonthlyBillResponse>(
                Error.Validation("Bill.MonthNotStarted", "The month has not started"));
        }

        // The current hour has partial readings only, so it belongs to the projection, not to the hours so far.
        var currentHourStartUtc = new DateTime(
            nowUtc.Year, nowUtc.Month, nowUtc.Day, nowUtc.Hour, 0, 0, DateTimeKind.Utc);
        bool ended = nowUtc >= monthEndUtc;
        DateTime soFarEndUtc = ended ? monthEndUtc : currentHourStartUtc;

        // Only the hours so far carry the water heater's part; it is never projected (spec 012, FR-006).
        // The EV charger's kWh is attached the same way, and never priced (spec 016).
        HourlyCosts costs = await hourlyCostProvider.GetWithEvChargerAsync(
            location, monthStartUtc, soFarEndUtc, ct, withWaterHeater: true);
        IReadOnlyList<HourCost> soFar = costs.Hours;

        PriceRateBook book = await costCalculator.RateBookAsync(ct);
        decimal vat = location.VatExempt ? 0m : book.ValueOn(PriceRateKind.Vat, month) ?? 0m;
        IReadOnlyList<GridCapacityStep> table = location.GridTariffId is { } tariffId
            ? CapacityCharge.TableFor(await priceRates.GetCapacityStepsAsync(tariffId, ct), month)
            : [];
        CapacityChargeResult capacity = CapacityCharge.Calculate(
            soFar.Select(h => (h.HourStart, h.ConsumptionKwh)), table, vat);

        // The car counts toward the Norgespris cap when it is counted with the house (spec 018, FR-004), so the
        // projection continues from the meter's kWh plus the car's.
        decimal meterKwhSoFar = soFar.Sum(h => h.ConsumptionKwh);
        EvChargerTotal? evCharger = EvChargerShare.Sum(soFar.Select(h => h.EvCharger), costs.UnmatchedChargerHours);
        (IReadOnlyList<HourCost> projected, BillProjectionResponse? projection) = ended
            ? ([], null)
            : await ProjectAsync(
                location, currentHourStartUtc, monthEndUtc, EvChargerShare.TotalKwh(meterKwhSoFar, evCharger), ct);

        MonthlyBill bill = MonthlyBillBuilder.Build(location, soFar, projected, capacity, vat);
        var hours = new BillHoursResponse(
            (int)(monthEndUtc - monthStartUtc).TotalHours,
            (int)(soFarEndUtc - monthStartUtc).TotalHours,
            bill.HoursWithReadings,
            bill.HoursPriced);

        LogMessages.CostQueried(logger, query.UserId, query.LocationId);

        return Result.Success(BillMapper.ToResponse(
            month, ended, bill, hours, projection, capacity, WaterHeaterShare.Sum(soFar.Select(h => h.WaterHeater)),
            evCharger,
            meterKwhSoFar));
    }

    /// <summary>
    /// The current hour and the rest of the month: the kWh from the profile of the 168 hours before the current hour,
    /// priced by the calculator with the published spot price where there is one and otherwise the plain average spot
    /// price of those 168 hours. The running month kWh continues from the hours so far (with the car's kWh when counted
    /// with the house), so the Norgespris cap applies.
    /// </summary>
    private async Task<(IReadOnlyList<HourCost>, BillProjectionResponse)> ProjectAsync(
        Location location, DateTime fromUtc, DateTime monthEndUtc, decimal monthKwhSoFar, CancellationToken ct)
    {
        DateTime windowStartUtc = fromUtc.AddHours(-ConsumptionProjection.WindowHours);
        IReadOnlyList<HourConsumption> rows =
            await consumptionRepository.GetHourlyAsync(location.Id, null, windowStartUtc, fromUtc, ct);
        ProjectionResult projection = ConsumptionProjection.ProjectRestOfMonth(rows, fromUtc, monthEndUtc);

        // One query for the prices of the window and the rest of the month, not one per hour.
        await costCalculator.PreloadAsync(location, windowStartUtc, monthEndUtc, ct);
        var windowPrices = new List<decimal>();
        for (DateTime hour = windowStartUtc; hour < fromUtc; hour = hour.AddHours(1))
        {
            if (await costCalculator.SpotEnergyAsync(location, hour, ct) is { } price)
            {
                windowPrices.Add(price);
            }
        }

        decimal? assumedSpot = windowPrices.Count == 0 ? null : windowPrices.Average();

        var projected = new List<HourCost>();
        decimal monthKwh = monthKwhSoFar;
        foreach ((DateTime hour, decimal kwh) in projection.Hours)
        {
            CostResult cost = await costCalculator.CalculateAsync(location, hour, kwh, monthKwh, ct, assumedSpot);
            projected.Add(new HourCost(hour, kwh, cost));
            monthKwh += kwh;
        }

        return (projected, new BillProjectionResponse(
            fromUtc,
            projection.Hours.Count,
            projected.Sum(h => h.ConsumptionKwh),
            projection.BasedOnHours,
            projection.BasedOnHours / 24m,
            assumedSpot));
    }
}
