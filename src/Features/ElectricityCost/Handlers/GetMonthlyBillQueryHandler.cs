using Application.Common.Interfaces.Repositories;
using Application.CQRS;
using Application.ElectricityCost;
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
/// part (Nettleie). Every hour is priced by the one calculation, so the bill always agrees with the hourly costs.
/// </summary>
public class GetMonthlyBillQueryHandler(
    ILocationRepository<Location> locationRepository,
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

        IReadOnlyList<HourCost> soFar = await hourlyCostProvider.GetAsync(location, monthStartUtc, soFarEndUtc, ct);

        PriceRateBook book = await costCalculator.RateBookAsync(ct);
        decimal vat = location.VatExempt ? 0m : book.ValueOn(PriceRateKind.Vat, month) ?? 0m;
        IReadOnlyList<GridCapacityStep> table = location.GridTariffId is { } tariffId
            ? CapacityCharge.TableFor(await priceRates.GetCapacityStepsAsync(tariffId, ct), month)
            : [];
        CapacityChargeResult capacity = CapacityCharge.Calculate(
            soFar.Select(h => (h.HourStart, h.ConsumptionKwh)), table, vat);

        MonthlyBill bill = MonthlyBillBuilder.Build(location, soFar, [], capacity, vat);
        var hours = new BillHoursResponse(
            (int)(monthEndUtc - monthStartUtc).TotalHours,
            (int)(soFarEndUtc - monthStartUtc).TotalHours,
            bill.HoursWithReadings,
            bill.HoursPriced);

        LogMessages.CostQueried(logger, query.UserId, query.LocationId);

        return Result.Success(BillMapper.ToResponse(month, ended, bill, hours, null, capacity));
    }
}
