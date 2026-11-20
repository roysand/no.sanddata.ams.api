using Application.ElectricityCost;
using Application.EvChargers;
using Application.WaterHeaters;
using Features.ElectricityCost.Queries;
using Features.ElectricityCost.Services;

namespace Features.ElectricityCost.Mappers;

public static class CostMapper
{
    public static CurrentHourCostResponse ToCurrentHourResponse(
        DateTime hourStart, decimal consumptionKwh, CostResult cost, EvChargerHour? evCharger = null) =>
        new(hourStart, consumptionKwh, cost.PricingModel.ToString(),
            ToCurrent(cost.Actual), ToCurrent(cost.Comparison),
            EvChargerShare.TotalKwh(consumptionKwh, evCharger), ToEvCharger(evCharger));

    public static HourlyCostItemResponse ToHourlyResponse(HourCost hour) =>
        new(hour.HourStart, hour.ConsumptionKwh, hour.Cost.PricingModel.ToString(),
            ToHourly(hour.Cost.Actual), ToHourly(hour.Cost.Comparison), ToWaterHeater(hour.WaterHeater),
            EvChargerShare.TotalKwh(hour.ConsumptionKwh, hour.EvCharger), ToEvCharger(hour.EvCharger));

    public static PagedHourlyCostResponse ToPagedHourlyResponse(
        IEnumerable<HourCost> hours, int page, int pageSize, int totalCount) =>
        new(hours.Select(ToHourlyResponse).ToList(), page, pageSize, totalCount);

    /// <summary>
    /// A day's cost for a model is null if any of its hours lacks price data - a partial sum would
    /// silently understate the cost. The day's charger kWh (spec 016) also counts the charging hours that had no meter
    /// reading, given in <paramref name="chargerHoursWithoutMeterReading"/>.
    /// </summary>
    public static DailyCostItemResponse ToDailyResponse(
        DateOnly date, IReadOnlyList<HourCost> hours, IReadOnlyCollection<EvChargerHour>? chargerHoursWithoutMeterReading = null)
    {
        decimal kwh = hours.Sum(h => h.ConsumptionKwh);
        EvChargerTotal? charger = EvChargerShare.Sum(hours.Select(h => h.EvCharger), chargerHoursWithoutMeterReading ?? []);
        return new DailyCostItemResponse(date, kwh, hours[0].Cost.PricingModel.ToString(),
            SumCost(hours.Select(h => h.Cost.Actual)),
            SumCost(hours.Select(h => h.Cost.Comparison)),
            ToWaterHeater(WaterHeaterShare.Sum(hours.Select(h => h.WaterHeater))),
            EvChargerShare.TotalKwh(kwh, charger),
            charger is null
                ? null
                : new EvChargerDayResponse(charger.Kwh, charger.Estimated, charger.Included, charger.HoursWithoutMeterReading));
    }

    private static EvChargerHourResponse? ToEvCharger(EvChargerHour? part) =>
        part is null ? null : new EvChargerHourResponse(part.Kwh, part.Estimated, part.Included);

    private static WaterHeaterHourResponse? ToWaterHeater(WaterHeaterHour? part) =>
        part is null ? null : new WaterHeaterHourResponse(part.Kwh, part.Cost, part.SampledMinutes, part.Capped);

    private static WaterHeaterDayResponse? ToWaterHeater(WaterHeaterTotal? total) =>
        total is null ? null : new WaterHeaterDayResponse(total.Kwh, total.Cost, total.SampledMinutes, total.Capped);

    private static DayCostResponse? SumCost(IEnumerable<RateCost?> costs)
    {
        var list = costs.ToList();
        return list.Any(c => c is null) ? null : new DayCostResponse(list.Sum(c => c!.Cost));
    }

    private static CurrentRateCostResponse? ToCurrent(RateCost? rate) =>
        rate is null
            ? null
            : new CurrentRateCostResponse(rate.RatePerKwh, rate.Cost, ToParts(rate.Breakdown), ToCap(rate.Cap));

    private static HourRateCostResponse? ToHourly(RateCost? rate) =>
        rate is null
            ? null
            : new HourRateCostResponse(rate.RatePerKwh, rate.Cost, ToParts(rate.Breakdown), ToCap(rate.Cap));

    private static CapResponse? ToCap(CapSplit? cap) =>
        cap is null ? null : new CapResponse(cap.KwhWithinCap, cap.KwhAboveCap, ToParts(cap.AboveCapBreakdown));

    private static PriceParts ToParts(PriceBreakdown b) =>
        new(b.Energy, b.Markup, b.Grid, b.Taxes, b.Vat, b.Total, b.GridSet);
}
