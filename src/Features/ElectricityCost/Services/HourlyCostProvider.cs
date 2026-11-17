using Application.Common.Interfaces.Repositories;
using Application.ElectricityCost;
using Domain.Common.Entities;

namespace Features.ElectricityCost.Services;

public record HourCost(DateTime HourStart, decimal ConsumptionKwh, CostResult Cost);

/// <summary>
/// Single source of hourly cost figures. Daily totals are sums of these, never an independent
/// calculation (FR-004).
/// </summary>
public class HourlyCostProvider(IConsumptionRepository consumptionRepository, CostCalculator costCalculator)
{
    public async Task<IReadOnlyList<HourCost>> GetAsync(
        Location location, DateTime fromUtc, DateTime toUtc, CancellationToken ct)
    {
        // Read from the start of the Oslo month, so the Norgespris cap counts the kWh used before the range and an
        // hour has the same price whatever range it is asked for in.
        DateTime monthStart = CostTime.LocalMonthStartUtc(fromUtc);
        IReadOnlyList<HourConsumption> rows =
            await consumptionRepository.GetHourlyAsync(location.Id, null, monthStart, toUtc, ct);

        // One query for the spot prices and one for the exchange rates of the whole range, not one per hour.
        await costCalculator.PreloadAsync(location, fromUtc, toUtc, ct);

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
                result.Add(new HourCost(hourStart, kwh, cost));
            }

            monthKwh += kwh;
        }

        return result;
    }
}
