using Application.Common.Interfaces.Repositories;
using Application.CQRS;
using Application.ElectricityCost;
using Domain.Common;
using Domain.Common.Entities;
using Features.ElectricityCost.Logging;
using Features.ElectricityCost.Queries;
using Features.ElectricityCost.Services;
using Microsoft.Extensions.Logging;

namespace Features.ElectricityCost.Handlers;

/// <summary>
/// The monthly grid capacity charge of a location. A monthly figure: it is not spread into the hourly price, so the
/// daily totals stay the sum of their hours.
/// </summary>
public class GetCapacityChargeQueryHandler(
    ILocationRepository<Location> locationRepository,
    IConsumptionRepository consumptionRepository,
    IPriceRateRepository priceRates,
    TimeProvider timeProvider,
    ILogger<GetCapacityChargeQueryHandler> logger)
    : IQueryHandler<GetCapacityChargeQuery, Result<CapacityChargeResponse>>
{
    public async Task<Result<CapacityChargeResponse>> Handle(GetCapacityChargeQuery query, CancellationToken ct)
    {
        if (!await locationRepository.IsUserAssociatedAsync(query.UserId, query.LocationId, ct))
        {
            LogMessages.LocationAccessDenied(logger, query.UserId, query.LocationId);
            return Result.Failure<CapacityChargeResponse>(Error.NotFound("Location.NotFound", "Location not found"));
        }

        Location? location = await locationRepository.GetByIdAsync(query.LocationId, ct);
        if (location is null)
        {
            return Result.Failure<CapacityChargeResponse>(Error.NotFound("Location.NotFound", "Location not found"));
        }

        DateTime nowUtc = timeProvider.GetUtcNow().UtcDateTime;
        DateOnly anyDay = query.Month ?? DateOnly.FromDateTime(CostTime.LocalDateOf(nowUtc));
        DateOnly month = new(anyDay.Year, anyDay.Month, 1);
        DateTime fromUtc = CostTime.LocalDayStartUtc(month.ToDateTime(TimeOnly.MinValue));
        DateTime toUtc = CostTime.LocalDayStartUtc(month.AddMonths(1).ToDateTime(TimeOnly.MinValue));
        bool provisional = nowUtc < toUtc;

        IReadOnlyList<GridCapacityStep> table = location.GridTariffId is { } tariffId
            ? CapacityCharge.TableFor(await priceRates.GetCapacityStepsAsync(tariffId, ct), month)
            : [];

        IReadOnlyList<HourConsumption> rows =
            await consumptionRepository.GetHourlyAsync(location.Id, null, fromUtc, toUtc, ct);
        var hours = rows
            .GroupBy(r => DateTime.SpecifyKind(r.BucketStart, DateTimeKind.Utc))
            .Select(g => (g.Key, g.Sum(r => r.ConsumptionKwh)));

        decimal vat = location.VatExempt
            ? 0m
            : new PriceRateBook(await priceRates.GetPriceRatesAsync(ct), []).ValueOn(PriceRateKind.Vat, month) ?? 0m;

        CapacityChargeResult charge = CapacityCharge.Calculate(hours, table, vat);

        LogMessages.CostQueried(logger, query.UserId, query.LocationId);

        return Result.Success(new CapacityChargeResponse(
            month.ToString("yyyy-MM"),
            provisional,
            charge.Set,
            charge.Peaks.Select(p => new CapacityPeakResponse(p.Date, p.HourStartUtc, p.Kw)).ToList(),
            charge.AverageKw,
            charge.Step is { } step ? new CapacityStepRangeResponse(step.LowerKw, step.UpperKw) : null,
            charge.AmountExclVat,
            charge.Vat,
            charge.Amount,
            charge.KwToNextStep));
    }
}
