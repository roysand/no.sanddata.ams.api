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
        IReadOnlyList<HourConsumption> rows =
            await consumptionRepository.GetHourlyAsync(location.Id, null, fromUtc, toUtc, ct);

        var result = new List<HourCost>();
        foreach (IGrouping<DateTime, HourConsumption> hour in rows.GroupBy(r => r.BucketStart).OrderBy(g => g.Key))
        {
            var hourStart = DateTime.SpecifyKind(hour.Key, DateTimeKind.Utc);
            decimal kwh = hour.Sum(r => r.ConsumptionKwh);
            result.Add(new HourCost(hourStart, kwh, await costCalculator.CalculateAsync(location, hourStart, kwh, ct)));
        }

        return result;
    }
}
