using Application.ElectricityCost;
using Features.ElectricityCost.Queries;
using Features.ElectricityCost.Services;

namespace Features.ElectricityCost.Mappers;

public static class CostMapper
{
    public static CurrentHourCostResponse ToCurrentHourResponse(
        DateTime hourStart, decimal consumptionKwh, CostResult cost) =>
        new(hourStart, consumptionKwh, cost.PricingModel.ToString(),
            ToCurrent(cost.Actual), ToCurrent(cost.Comparison));

    public static HourlyCostItemResponse ToHourlyResponse(HourCost hour) =>
        new(hour.HourStart, hour.ConsumptionKwh, hour.Cost.PricingModel.ToString(),
            ToHourly(hour.Cost.Actual), ToHourly(hour.Cost.Comparison));

    public static PagedHourlyCostResponse ToPagedHourlyResponse(
        IEnumerable<HourCost> hours, int page, int pageSize, int totalCount) =>
        new(hours.Select(ToHourlyResponse).ToList(), page, pageSize, totalCount);

    /// <summary>
    /// A day's cost for a model is null if any of its hours lacks price data - a partial sum would
    /// silently understate the cost.
    /// </summary>
    public static DailyCostItemResponse ToDailyResponse(DateOnly date, IReadOnlyList<HourCost> hours) =>
        new(date, hours.Sum(h => h.ConsumptionKwh), hours[0].Cost.PricingModel.ToString(),
            SumCost(hours.Select(h => h.Cost.Actual)),
            SumCost(hours.Select(h => h.Cost.Comparison)));

    private static DayCostResponse? SumCost(IEnumerable<RateCost?> costs)
    {
        var list = costs.ToList();
        return list.Any(c => c is null) ? null : new DayCostResponse(list.Sum(c => c!.Cost));
    }

    private static CurrentRateCostResponse? ToCurrent(RateCost? rate) =>
        rate is null ? null : new CurrentRateCostResponse(rate.RatePerKwh, rate.Cost);

    private static HourRateCostResponse? ToHourly(RateCost? rate) =>
        rate is null ? null : new HourRateCostResponse(rate.RatePerKwh, rate.Cost);
}
