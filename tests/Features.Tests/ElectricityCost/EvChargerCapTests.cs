using System.Globalization;
using Application.Common.Interfaces.Repositories;
using Application.ElectricityCost;
using Application.EvChargers;
using Domain.Common;
using Domain.Common.Entities;
using Features.ElectricityCost.Handlers;
using Features.ElectricityCost.Queries;
using Features.ElectricityCost.Services;
using Features.Tests.NorgesPris;
using Features.Tests.Pricing;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Features.Tests.ElectricityCost;

/// <summary>
/// The car in the Norgespris cap (spec 018, FR-004): with "count the car with the house" on, the charger's kWh of the
/// earlier hours of the month count toward the cap; off, they do not. A holiday home with a 1 000 kWh cap, Norgespris
/// 0.40 and spot 1.00 per kWh, no taxes or VAT: 990 kWh of meter and 20 kWh of car before an hour put that hour on spot
/// terms only when the car is counted.
/// </summary>
public class EvChargerCapTests
{
    private static readonly DateTime MonthStart = Utc("2026-11-30T23:00:00Z"); // 1 December 00:00 Oslo
    private static readonly DateTime BigHour = Utc("2026-12-01T05:00:00Z");
    private static readonly DateTime CarHour = Utc("2026-12-01T06:00:00Z");
    private static readonly DateTime Priced = Utc("2026-12-01T09:00:00Z");

    private readonly ILocationRepository<Location> _locations = Substitute.For<ILocationRepository<Location>>();
    private readonly IConsumptionRepository _consumption = Substitute.For<IConsumptionRepository>();
    private readonly IElectricityPriceRepository<ElectricityPrice> _prices =
        Substitute.For<IElectricityPriceRepository<ElectricityPrice>>();
    private readonly IExchangeRateRepository<ExchangeRate> _fx = Substitute.For<IExchangeRateRepository<ExchangeRate>>();
    private readonly IWaterHeaterPowerSampleRepository<WaterHeaterPowerSample> _samples =
        Substitute.For<IWaterHeaterPowerSampleRepository<WaterHeaterPowerSample>>();
    private readonly IChargingSessionRepository _charging = Substitute.For<IChargingSessionRepository>();
    private readonly Location _cabin = new(Guid.NewGuid(), "Cabin", "Addr", "SN1", "NO1", true, true);
    private readonly List<HourConsumption> _meter = [];
    private readonly List<EvChargerHourEnergy> _car = [];

    public EvChargerCapTests()
    {
        _cabin.SetPricing(null, 0m, false, false, true);
        _locations.IsUserAssociatedAsync(Arg.Any<Guid>(), _cabin.Id, Arg.Any<CancellationToken>()).Returns(true);
        _locations.GetByIdAsync(_cabin.Id, Arg.Any<CancellationToken>()).Returns(_cabin);
        _prices.GetByRegionAndHourAsync("NO1", Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns(call => new ElectricityPrice(Guid.NewGuid(), "NO1", call.ArgAt<DateTime>(1), 100m));
        _fx.GetByDateAsync("EURNOK", Arg.Any<DateOnly>(), Arg.Any<CancellationToken>())
            .Returns(call => new ExchangeRate(Guid.NewGuid(), "EURNOK", call.ArgAt<DateOnly>(1), 10m));
        _prices.ServeRangesFromSingleHours();
        _fx.ServeRangesFromSingleDates();

        _consumption.GetHourlyAsync(
                _cabin.Id, null, Arg.Any<DateTime>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns(call => Task.FromResult<IReadOnlyList<HourConsumption>>(InRange(call.ArgAt<DateTime>(2),
                call.ArgAt<DateTime>(3)).ToList()));
        _consumption.GetConsumptionKwhAsync(
                _cabin.Id, Arg.Any<DateTime>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns(call => InRange(call.ArgAt<DateTime>(1), call.ArgAt<DateTime>(2)).Sum(h => h.ConsumptionKwh));
        _consumption.GetHourConsumptionKwhAsync(_cabin.Id, Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns(call => InRange(call.ArgAt<DateTime>(1), call.ArgAt<DateTime>(1).AddHours(1))
                .Sum(h => h.ConsumptionKwh));
        _charging.GetHourlyEnergyAsync(_cabin.Id, Arg.Any<DateTime>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns(call => Task.FromResult<IReadOnlyList<EvChargerHourEnergy>>(_car
                .Where(h => h.HourStartUtc >= call.ArgAt<DateTime>(1) && h.HourStartUtc < call.ArgAt<DateTime>(2))
                .ToList()));
    }

    private static DateTime Utc(string iso) =>
        DateTime.Parse(iso, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal);

    private IEnumerable<HourConsumption> InRange(DateTime from, DateTime to) =>
        _meter.Where(r => r.BucketStart >= from && r.BucketStart < to);

    private void Meter(DateTime hour, decimal kwh) => _meter.Add(new HourConsumption
    {
        LocationId = _cabin.Id, MeterId = Guid.Empty, BucketStart = hour, AvgPowerWatts = (double)(kwh * 1000m)
    });

    private CostCalculator Calculator() => new(
        _prices,
        _fx,
        NorgesPrisRates.Seeded(),
        PriceRateStubs.Repository(
        [
            PriceRateStubs.Rate(PriceRateKind.Vat, "2020-01-01", 0m),
            PriceRateStubs.Rate(PriceRateKind.ElectricityTax, "2020-01-01", 0m),
            PriceRateStubs.Rate(PriceRateKind.EnovaFee, "2020-01-01", 0m),
            PriceRateStubs.Rate(PriceRateKind.NorgesPrisCapHolidayHome, "2025-10-01", 1000m)
        ], []));

    private HourlyCostProvider Provider() =>
        new(_consumption, Calculator(), _samples, new FixedTimeProvider("2026-12-02T12:00:00Z"), _charging);

    /// <summary>990 kWh of meter, then 20 kWh of car with no meter reading, then 1 kWh in the priced hour.</summary>
    private void PassTheCapWithTheCar()
    {
        Meter(BigHour, 990m);
        _car.Add(new EvChargerHourEnergy(CarHour, 20m, false));
        Meter(Priced, 1m);
    }

    [Theory]
    [InlineData(true, 1.00)]
    [InlineData(false, 0.40)]
    public async Task HourlyCosts_CountTheCarTowardTheCapOnlyWhenIncluded(bool included, decimal expectedRate)
    {
        _cabin.SetIncludeEvCharger(included);
        PassTheCapWithTheCar();

        IReadOnlyList<HourCost> month = await Provider().GetAsync(_cabin, MonthStart, Priced.AddHours(1), default);
        IReadOnlyList<HourCost> alone = await Provider().GetAsync(_cabin, Priced, Priced.AddHours(1), default);

        Assert.Equal(expectedRate, month.Single(h => h.HourStart == Priced).Cost.Actual!.RatePerKwh);
        Assert.Equal(expectedRate, Assert.Single(alone).Cost.Actual!.RatePerKwh);
    }

    [Fact]
    public async Task HourlyCosts_TheCarOfAnHourCountsAfterTheHouseOfThatHour()
    {
        _cabin.SetIncludeEvCharger(true);
        Meter(BigHour, 999m);
        _car.Add(new EvChargerHourEnergy(Priced, 20m, false));
        Meter(Priced, 1m);
        Meter(Priced.AddHours(1), 1m);

        IReadOnlyList<HourCost> hours = await Provider().GetAsync(_cabin, MonthStart, Priced.AddHours(2), default);

        Assert.Equal(0.40m, hours.Single(h => h.HourStart == Priced).Cost.Actual!.RatePerKwh);
        Assert.Equal(1.00m, hours.Single(h => h.HourStart == Priced.AddHours(1)).Cost.Actual!.RatePerKwh);
    }

    [Fact]
    public async Task HourlyCosts_TheCarOfThePreviousMonthDoesNotCount()
    {
        _cabin.SetIncludeEvCharger(true);
        _car.Add(new EvChargerHourEnergy(MonthStart.AddHours(-1), 2000m, false));
        Meter(Priced, 1m);

        IReadOnlyList<HourCost> hours =
            await Provider().GetAsync(_cabin, MonthStart.AddHours(-1), Priced.AddHours(1), default);

        Assert.Equal(0.40m, hours.Single(h => h.HourStart == Priced).Cost.Actual!.RatePerKwh);
    }

    [Theory]
    [InlineData(true, 1.00)]
    [InlineData(false, 0.40)]
    public async Task DayPrices_CountTheCarTowardTheCapOnlyWhenIncluded(bool included, decimal expectedRate)
    {
        _cabin.SetIncludeEvCharger(included);
        PassTheCapWithTheCar();
        var handler = new GetDayPricesQueryHandler(
            _locations, _consumption, Calculator(), Substitute.For<ILogger<GetDayPricesQueryHandler>>(), _charging);

        // The day after: the car is before the day. The day itself: the car is an hour of the day.
        Result<DayPricesResponse> nextDay = await handler.Handle(
            new GetDayPricesQuery(Guid.NewGuid(), _cabin.Id, new DateTime(2026, 12, 2)), default);
        Result<DayPricesResponse> sameDay = await handler.Handle(
            new GetDayPricesQuery(Guid.NewGuid(), _cabin.Id, new DateTime(2026, 12, 1)), default);

        Assert.All(nextDay.Value.Items, i => Assert.Equal(expectedRate, i.RatePerKwh));
        Assert.Equal(0.40m, sameDay.Value.Items.Single(i => i.HourStart == CarHour).RatePerKwh);
        Assert.Equal(expectedRate, sameDay.Value.Items.Single(i => i.HourStart == CarHour.AddHours(1)).RatePerKwh);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task CurrentHour_CountsTheCarTowardTheCapOnlyWhenIncluded(bool included)
    {
        // This handler reads the real clock, so the hours are placed relative to now.
        DateTime now = DateTime.UtcNow;
        var hourStart = new DateTime(now.Year, now.Month, now.Day, now.Hour, 0, 0, DateTimeKind.Utc);
        var oslo = TimeZoneInfo.FindSystemTimeZoneById("Europe/Oslo");
        DateTime local = TimeZoneInfo.ConvertTimeFromUtc(hourStart, oslo);
        DateTime monthStart = TimeZoneInfo.ConvertTimeToUtc(new DateTime(local.Year, local.Month, 1), oslo);
        _cabin.SetIncludeEvCharger(included);
        _meter.Add(new HourConsumption
        {
            LocationId = _cabin.Id, MeterId = Guid.Empty, BucketStart = monthStart, AvgPowerWatts = 990_000d
        });
        _car.Add(new EvChargerHourEnergy(monthStart, 20m, false));
        Meter(hourStart, 1m);
        var handler = new GetCurrentHourCostQueryHandler(
            _locations, _consumption, Calculator(), Substitute.For<ILogger<GetCurrentHourCostQueryHandler>>(),
            _charging);

        Result<CurrentHourCostResponse> result =
            await handler.Handle(new GetCurrentHourCostQuery(Guid.NewGuid(), _cabin.Id), default);

        // In the first hour of a month there is no earlier hour, so nothing is before the current hour.
        bool passed = included && monthStart < hourStart;
        Assert.Equal(passed ? 1.00m : 0.40m, result.Value.Actual!.RatePerKwh);
    }
}
