using Application.ElectricityCost;
using Application.EvChargers;
using Domain.Common.Entities;

namespace Features.ElectricityCost.Services;

/// <summary>What the car's kWh so far cost (spec 019), and the rule that priced them.</summary>
/// <param name="Cost">NOK including VAT, not rounded; null when any of the car's hours can't be priced.</param>
/// <param name="PricedAt"><see cref="EvChargerCost.HousePrice"/> or <see cref="EvChargerCost.NorgesPrisPrice"/>.</param>
public sealed record EvChargerCostResult(decimal? Cost, string PricedAt);

/// <summary>
/// Prices the car charger's kWh of the bill's hours so far (spec 019). The cost is the car's own figure: no bill line,
/// part or total reads it. Which rule applies follows the location's "count the car with the house" setting.
/// </summary>
public static class EvChargerCost
{
    /// <summary>Counted with the house: the house's price per kWh in each hour (research R3).</summary>
    public const string HousePrice = "HousePrice";

    /// <summary>On its own agreement: the Norgespris price including VAT, energy only (research R2).</summary>
    public const string NorgesPrisPrice = "NorgesPrisPrice";

    /// <param name="soFar">The bill's hours so far, from the start of the month, with their charger parts.</param>
    /// <param name="withoutMeterReading">The charging hours of the month so far without a house reading.</param>
    public static async Task<EvChargerCostResult> PriceAsync(
        Location location,
        IReadOnlyList<HourCost> soFar,
        IReadOnlyList<EvChargerHour> withoutMeterReading,
        CostCalculator calculator,
        CancellationToken ct)
    {
        var car = soFar
            .Select(h => h.EvCharger)
            .OfType<EvChargerHour>()
            .Concat(withoutMeterReading)
            .OrderBy(h => h.HourStartUtc)
            .ToList();

        return location.IncludeEvChargerInTotals
            ? new EvChargerCostResult(await HousePriceCostAsync(location, soFar, car, calculator, ct), HousePrice)
            : new EvChargerCostResult(await SeparateAgreementCostAsync(location, car, calculator, ct), NorgesPrisPrice);
    }

    /// <summary>
    /// The car on its own agreement: each hour's kWh × the Norgespris price including VAT in effect in that hour. This
    /// is the garages' agreement at Hjemme today (shared by 12 garages, with Norgespris), and the one place to change
    /// when that agreement changes (spec 019, FR-005).
    /// </summary>
    private static async Task<decimal?> SeparateAgreementCostAsync(
        Location location, IReadOnlyList<EvChargerHour> car, CostCalculator calculator, CancellationToken ct)
    {
        decimal total = 0m;
        foreach (EvChargerHour hour in car)
        {
            if (await calculator.NorgesPrisPriceWithVatAsync(location, hour.HourStartUtc, ct) is not { } price)
            {
                return null;
            }

            total += hour.Kwh * price;
        }

        return total;
    }

    /// <summary>
    /// The car on the house's agreement: each hour's car kWh priced by the one calculation as house kWh used after the
    /// house's kWh of the same hour, with the month's running kWh of house and car before it (spec 018, R9). So the
    /// car's kWh past the Norgespris cap are priced on spot terms, split exactly as any hour is.
    /// </summary>
    private static async Task<decimal?> HousePriceCostAsync(
        Location location,
        IReadOnlyList<HourCost> soFar,
        IReadOnlyList<EvChargerHour> car,
        CostCalculator calculator,
        CancellationToken ct)
    {
        var house = soFar.ToDictionary(h => h.HourStart, h => h.ConsumptionKwh);
        var carKwh = car.ToDictionary(h => h.HourStartUtc, h => h.Kwh);
        decimal running = 0m;
        decimal total = 0m;
        foreach (DateTime hour in house.Keys.Union(carKwh.Keys).Order())
        {
            decimal houseKwh = house.GetValueOrDefault(hour);
            if (carKwh.TryGetValue(hour, out decimal kwh))
            {
                CostResult cost = await calculator.CalculateAsync(location, hour, kwh, running + houseKwh, ct);
                if (cost.Actual is null)
                {
                    return null;
                }

                total += cost.Actual.Cost;
            }

            running += houseKwh + carKwh.GetValueOrDefault(hour);
        }

        return total;
    }
}
