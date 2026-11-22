using Application.Common.Interfaces.Repositories;
using Application.ElectricityCost;
using Application.EvChargers;
using Application.WaterHeaters;
using Domain.Common.Entities;

namespace Features.ElectricityCost.Services;

/// <param name="WaterHeater">The heater's part of the hour (spec 012), or null when the hour has no heater samples or
/// the provider was asked without <c>withWaterHeater</c>.</param>
/// <param name="EvCharger">The EV charger's kWh in the hour (spec 016), or null when the hour has no charging data or
/// the provider was asked without the charger.</param>
public record HourCost(
    DateTime HourStart,
    decimal ConsumptionKwh,
    CostResult Cost,
    WaterHeaterHour? WaterHeater = null,
    EvChargerHour? EvCharger = null);

/// <summary>
/// The hours with a meter reading, and the charger's hours that had none (spec 016, research R7): those are counted in
/// the day's and month's charger kWh, but never added to the hourly list, so they are never priced.
/// </summary>
public record HourlyCosts(IReadOnlyList<HourCost> Hours, IReadOnlyList<EvChargerHour> UnmatchedChargerHours);

/// <summary>
/// Single source of hourly cost figures. Daily totals are sums of these, never an independent
/// calculation (FR-004). With <c>withWaterHeater</c>, each hour also carries the water heater's part, from one extra
/// query (spec 012, research R6); only the views that show the split ask for it. <see cref="GetWithEvChargerAsync"/>
/// also attaches the EV charger's kWh from one more query (spec 016, research R8).
/// </summary>
/// <param name="chargingSessions">
/// The charger's hourly kWh. Optional so the tests that never look at the charger can leave it out; the DI container
/// always passes the registered repository.
/// </param>
public class HourlyCostProvider(
    IConsumptionRepository consumptionRepository,
    CostCalculator costCalculator,
    IWaterHeaterPowerSampleRepository<WaterHeaterPowerSample> waterHeaterSamples,
    TimeProvider timeProvider,
    IChargingSessionRepository? chargingSessions = null)
{
    public async Task<IReadOnlyList<HourCost>> GetAsync(
        Location location, DateTime fromUtc, DateTime toUtc, CancellationToken ct, bool withWaterHeater = false) =>
        (await GetCoreAsync(location, fromUtc, toUtc, ct, withWaterHeater, withEvCharger: false)).Hours;

    /// <summary>
    /// The hours with the EV charger's part attached, and the charger's hours that had no meter reading. Costs are the
    /// same as <see cref="GetAsync"/>: the charger is never priced, though its kWh count toward the Norgespris cap when
    /// the location counts it with the house (spec 018, FR-004), in both methods.
    /// </summary>
    public Task<HourlyCosts> GetWithEvChargerAsync(
        Location location, DateTime fromUtc, DateTime toUtc, CancellationToken ct, bool withWaterHeater = false) =>
        GetCoreAsync(location, fromUtc, toUtc, ct, withWaterHeater, withEvCharger: true);

    private async Task<HourlyCosts> GetCoreAsync(
        Location location, DateTime fromUtc, DateTime toUtc, CancellationToken ct, bool withWaterHeater, bool withEvCharger)
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

        // With the car counted with the house, its kWh count toward the Norgespris cap (spec 018, FR-004), so they are
        // read from the start of the month like the meter's; only the hours from 'from' are attached to the result.
        bool included = location.IncludeEvChargerInTotals;
        bool carInCap = included && chargingSessions is not null;
        IReadOnlyList<EvChargerHourEnergy> chargerHours = (withEvCharger || carInCap) && chargingSessions is not null
            ? await chargingSessions.GetHourlyEnergyAsync(
                location.Id, carInCap ? monthStart : fromUtc, HourCeiling(toUtc), ct)
            : [];
        IReadOnlyDictionary<DateTime, EvChargerHourEnergy> charger = withEvCharger
            ? chargerHours.Where(e => e.HourStartUtc >= fromUtc).ToDictionary(e => e.HourStartUtc)
            : new Dictionary<DateTime, EvChargerHourEnergy>();
        IReadOnlyList<EvChargerHourEnergy> carForCap = carInCap ? chargerHours : [];
        int nextCar = 0;

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

            // The car's kWh of the earlier hours of the same month; within an hour the car counts after the house.
            for (; nextCar < carForCap.Count && carForCap[nextCar].HourStartUtc < hourStart; nextCar++)
            {
                if (carForCap[nextCar].HourStartUtc >= currentMonth)
                {
                    monthKwh += carForCap[nextCar].Kwh;
                }
            }

            if (hourStart >= fromUtc)
            {
                CostResult cost = await costCalculator.CalculateAsync(location, hourStart, kwh, monthKwh, ct);
                WaterHeaterHour? part = heater.TryGetValue(hourStart, out WaterHeaterHourEnergy? energy)
                    ? WaterHeaterShare.ForHour(energy, kwh, cost.Actual?.Cost)
                    : null;
                EvChargerHour? chargerPart = charger.TryGetValue(hourStart, out EvChargerHourEnergy? chargerEnergy)
                    ? EvChargerShare.ForHour(chargerEnergy, included)
                    : null;
                result.Add(new HourCost(hourStart, kwh, cost, part, chargerPart));
            }

            monthKwh += kwh;
        }

        var matched = result.Select(h => h.HourStart).ToHashSet();
        IReadOnlyList<EvChargerHour> unmatched = charger.Values
            .Where(e => !matched.Contains(e.HourStartUtc))
            .OrderBy(e => e.HourStartUtc)
            .Select(e => EvChargerShare.ForHour(e, included))
            .ToList();

        return new HourlyCosts(result, unmatched);
    }

    private static DateTime HourCeiling(DateTime utc)
    {
        var floor = new DateTime(utc.Year, utc.Month, utc.Day, utc.Hour, 0, 0, DateTimeKind.Utc);
        return floor == utc ? floor : floor.AddHours(1);
    }
}
