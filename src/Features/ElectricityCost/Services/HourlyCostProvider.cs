using Application.Common.Interfaces.Repositories;
using Application.ElectricityCost;
using Application.WaterHeaters;
using Domain.Common.Entities;

namespace Features.ElectricityCost.Services;

/// <param name="WaterHeater">The heater's part of the hour (spec 012), or null when the hour has no heater samples or
/// the provider was asked without <c>withWaterHeater</c>.</param>
public record HourCost(DateTime HourStart, decimal ConsumptionKwh, CostResult Cost, WaterHeaterHour? WaterHeater = null);

/// <summary>
/// Single source of hourly cost figures. Daily totals are sums of these, never an independent
/// calculation (FR-004). With <c>withWaterHeater</c>, each hour also carries the water heater's part, from one extra
/// query (spec 012, research R6); only the views that show the split ask for it.
/// </summary>
public class HourlyCostProvider(
    IConsumptionRepository consumptionRepository,
    CostCalculator costCalculator,
    IWaterHeaterPowerSampleRepository<WaterHeaterPowerSample> waterHeaterSamples,
    TimeProvider timeProvider)
{
    public async Task<IReadOnlyList<HourCost>> GetAsync(
        Location location, DateTime fromUtc, DateTime toUtc, CancellationToken ct, bool withWaterHeater = false)
    {
        // Read from the start of the Oslo month, so the Norgespris cap counts the kWh used before the range and an
        // hour has the same price whatever range it is asked for in.
        DateTime monthStart = CostTime.LocalMonthStartUtc(fromUtc);
        IReadOnlyList<HourConsumption> rows =
            await consumptionRepository.GetHourlyAsync(location.Id, null, monthStart, toUtc, ct);

        // One query for the spot prices and one for the exchange rates of the whole range, not one per hour.
        await costCalculator.PreloadAsync(location, fromUtc, toUtc, ct);

        // An hour is returned when it starts before 'to', so the heater is read to the end of that hour; holds are
        // still clipped at now, so the current hour covers the minutes so far.
        IReadOnlyDictionary<DateTime, WaterHeaterHourEnergy> heater = withWaterHeater
            ? (await waterHeaterSamples.GetHourlyEnergyAsync(
                location.Id, fromUtc, HourCeiling(toUtc), timeProvider.GetUtcNow().UtcDateTime, ct))
                .ToDictionary(e => e.HourStartUtc)
            : new Dictionary<DateTime, WaterHeaterHourEnergy>();

        var result = new List<HourCost>();
        decimal monthKwh = 0m;
        DateTime currentMonth = monthStart;
        foreach (IGrouping<DateTime, HourConsumption> hour in rows.GroupBy(r => r.BucketStart).OrderBy(g => g.Key))
        {
            var hourStart = DateTime.SpecifyKind(hour.Key, DateTimeKind.Utc);
            decimal kwh = hour.Sum(r => r.ConsumptionKwh);

            DateTime hourMonth = CostTime.LocalMonthStartUtc(hourStart);
            if (hourMonth != currentMonth)
            {
                currentMonth = hourMonth;
                monthKwh = 0m;
            }

            if (hourStart >= fromUtc)
            {
                CostResult cost = await costCalculator.CalculateAsync(location, hourStart, kwh, monthKwh, ct);
                WaterHeaterHour? part = heater.TryGetValue(hourStart, out WaterHeaterHourEnergy? energy)
                    ? WaterHeaterShare.ForHour(energy, kwh, cost.Actual?.Cost)
                    : null;
                result.Add(new HourCost(hourStart, kwh, cost, part));
            }

            monthKwh += kwh;
        }

        return result;
    }

    private static DateTime HourCeiling(DateTime utc)
    {
        var floor = new DateTime(utc.Year, utc.Month, utc.Day, utc.Hour, 0, 0, DateTimeKind.Utc);
        return floor == utc ? floor : floor.AddHours(1);
    }
}
