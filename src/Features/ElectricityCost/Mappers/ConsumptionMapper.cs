using Features.ElectricityCost.Queries;

namespace Features.ElectricityCost.Mappers;

public static class ConsumptionMapper
{
    public static ConsumptionResponse ToConsumptionResponse(
        string granularity, IEnumerable<(DateTime PeriodStart, decimal Kwh)> buckets) =>
        new(granularity,
            buckets.GroupBy(b => b.PeriodStart)
                .OrderBy(g => g.Key)
                .Select(g => new ConsumptionItemResponse(
                    DateTime.SpecifyKind(g.Key, DateTimeKind.Utc), g.Sum(b => b.Kwh)))
                .ToList());
}
