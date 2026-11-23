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
/// The car's own cost on the bill (spec 019). A holiday home with a 1 000 kWh Norgespris limit, markup 0.02, grid
/// 0.30 by day and 0.20 by night, 2026 taxes (0.0813) and VAT 25 percent; spot 1.00 and Norgespris 0.40 per kWh.
/// By hand, a weekday day hour costs (0.40 + 0.02 + 0.30 + 0.0813) × 1.25 = 1.001625 per kWh on Norgespris terms and
/// (1.00 + 0.02 + 0.30 + 0.0813) × 1.25 = 1.751625 on spot terms.
/// </summary>
public class EvChargerCostBillTests
{
    private const string Now = "2026-12-10T12:30:00Z";
    private const decimal NorgesPrisHour = 1.001625m;
    private const decimal SpotHour = 1.751625m;
    private static readonly Guid Tariff = Guid.NewGuid();
    private static readonly DateTime CarHour = Utc("2026-12-02T20:00:00Z"); // Wednesday 21:00 Oslo, a day hour

    private readonly ILocationRepository<Location> _locations = Substitute.For<ILocationRepository<Location>>();
    private readonly IConsumptionRepository _consumption = Substitute.For<IConsumptionRepository>();
    private readonly IChargingSessionRepository _charging = Substitute.For<IChargingSessionRepository>();
    private readonly IElectricityPriceRepository<ElectricityPrice> _prices =
        Substitute.For<IElectricityPriceRepository<ElectricityPrice>>();
    private readonly IExchangeRateRepository<ExchangeRate> _fx = Substitute.For<IExchangeRateRepository<ExchangeRate>>();
    private readonly IPriceRateRepository _rates;
    private readonly List<HourConsumption> _readings = [];
    private readonly List<EvChargerHourEnergy> _car = [];
    private Func<DateTime, decimal?> _spot = _ => 100m;
    private INorgesPrisRateRepository _norgesPris = NorgesPrisRates.Seeded();
    private Location _location;

    public EvChargerCostBillTests()
    {
        _rates = PriceRateStubs.Repository(
            PriceRateStubs.National2026().Append(
                PriceRateStubs.Rate(PriceRateKind.NorgesPrisCapHolidayHome, "2025-10-01", 1000m)),
            [PriceRateStubs.Grid(Tariff, "2026-01-01", 0.30m, 0.20m)]);
        _rates.GetCapacityStepsAsync(Tariff, Arg.Any<CancellationToken>())
            .Returns([new GridCapacityStep(Guid.NewGuid(), Tariff, new DateOnly(2026, 1, 1), 0m, null, 100m, Guid.Empty)]);
        _prices.GetByRegionAndHourAsync("NO1", Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns(call => _spot(call.ArgAt<DateTime>(1)) is { } eur
                ? new ElectricityPrice(Guid.NewGuid(), "NO1", call.ArgAt<DateTime>(1), eur)
                : null);
        _fx.GetByDateAsync("EURNOK", Arg.Any<DateOnly>(), Arg.Any<CancellationToken>())
            .Returns(call => new ExchangeRate(Guid.NewGuid(), "EURNOK", call.ArgAt<DateOnly>(1), 10m));
        _prices.ServeRangesFromSingleHours();
        _fx.ServeRangesFromSingleDates();

        _consumption.GetHourlyAsync(
                Arg.Any<Guid>(), Arg.Any<Guid?>(), Arg.Any<DateTime>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns(call => Task.FromResult<IReadOnlyList<HourConsumption>>(InRange(call.ArgAt<DateTime>(2),
                call.ArgAt<DateTime>(3)).ToList()));
        _consumption.GetConsumptionKwhAsync(
                Arg.Any<Guid>(), Arg.Any<DateTime>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns(call => InRange(call.ArgAt<DateTime>(1), call.ArgAt<DateTime>(2)).Sum(h => h.ConsumptionKwh));
        _charging.GetHourlyEnergyAsync(Arg.Any<Guid>(), Arg.Any<DateTime>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns(call => Task.FromResult<IReadOnlyList<EvChargerHourEnergy>>(_car
                .Where(h => h.HourStartUtc >= call.ArgAt<DateTime>(1) && h.HourStartUtc < call.ArgAt<DateTime>(2))
                .ToList()));

        _location = Use(norgesPris: true);
    }

    private static DateTime Utc(string iso) =>
        DateTime.Parse(iso, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal);

    private IEnumerable<HourConsumption> InRange(DateTime from, DateTime to) =>
        _readings.Where(r => r.BucketStart >= from && r.BucketStart < to);

    private Location Use(bool norgesPris, bool vatExempt = false, bool included = false)
    {
        var location = new Location(Guid.NewGuid(), "Hjemme", "Addr", "SN1", "NO1", true, norgesPris);
        location.SetPricing(Tariff, 0.02m, false, vatExempt, true);
        location.SetSupplierMonthlyFee(27.20m);
        location.SetIncludeEvCharger(included);
        _locations.IsUserAssociatedAsync(Arg.Any<Guid>(), location.Id, Arg.Any<CancellationToken>()).Returns(true);
        _locations.GetByIdAsync(location.Id, Arg.Any<CancellationToken>()).Returns(location);
        _location = location;
        return location;
    }

    private void Reading(DateTime hour, decimal kwh) => _readings.Add(new HourConsumption
    {
        LocationId = _location.Id, MeterId = Guid.Empty, BucketStart = hour, AvgPowerWatts = (double)(kwh * 1000m)
    });

    private void Car(DateTime hour, decimal kwh) => _car.Add(new EvChargerHourEnergy(hour, kwh, false));

    private CostCalculator Calculator() => new(_prices, _fx, _norgesPris, _rates);

    private async Task<Result<MonthlyBillResponse>> BillResult(Guid? locationId = null)
    {
        CostCalculator calculator = Calculator();
        return await new GetMonthlyBillQueryHandler(
                _locations, _consumption, _rates,
                new HourlyCostProvider(_consumption, calculator,
                    Substitute.For<IWaterHeaterPowerSampleRepository<WaterHeaterPowerSample>>(),
                    new FixedTimeProvider(Now), _charging),
                calculator, new FixedTimeProvider(Now), Substitute.For<ILogger<GetMonthlyBillQueryHandler>>())
            .Handle(new GetMonthlyBillQuery(Guid.NewGuid(), locationId ?? _location.Id, null), default);
    }

    private async Task<EvChargerBillResponse> CarPart() => (await BillResult()).Value.EvCharger!;

    // ---- US1: not counted with the house -------------------------------------------------------------------------

    [Fact]
    public async Task NotIncluded_IsTheKwhAtTheNorgesPrisPriceWithVat()
    {
        Car(CarHour, 4.4m);
        Car(CarHour.AddHours(1), 4.4m);

        EvChargerBillResponse car = await CarPart();

        Assert.Equal(8.8m * 0.40m * 1.25m, car.Cost);
        Assert.Equal("NorgesPrisPrice", car.PricedAt);
    }

    [Fact]
    public async Task NotIncluded_HasNoVatForAnExemptLocation()
    {
        Use(norgesPris: true, vatExempt: true);
        Car(CarHour, 10m);

        Assert.Equal(4.00m, (await CarPart()).Cost);
    }

    [Fact]
    public async Task NotIncluded_PricesEachHourAtTheRateInEffectThen()
    {
        _norgesPris = NorgesPrisRates.Repository(
            NorgesPrisRates.Rate("2020-01-01", 0.40m), NorgesPrisRates.Rate("2026-12-05", 0.48m));
        Car(CarHour, 4m); // 2 December
        Car(Utc("2026-12-06T10:00:00Z"), 4m);

        Assert.Equal(4m * 0.50m + 4m * 0.60m, (await CarPart()).Cost);
    }

    [Fact]
    public async Task NotIncluded_WithoutARateInEffect_HasNoCost_ButTheRestOfThePart()
    {
        _norgesPris = NorgesPrisRates.Repository(NorgesPrisRates.Rate("2026-12-05", 0.48m));
        Car(CarHour, 4m);

        EvChargerBillResponse car = await CarPart();

        Assert.Null(car.Cost);
        Assert.Equal("NorgesPrisPrice", car.PricedAt);
        Assert.Equal(4m, car.Kwh);
    }

    [Fact]
    public async Task NotIncluded_TheHousesModelAndLimitDoNotMatter()
    {
        Car(CarHour, 4m);
        Reading(Utc("2026-12-01T05:00:00Z"), 2000m); // the Norgespris house is far over its limit
        decimal? overLimit = (await CarPart()).Cost;

        Use(norgesPris: false);
        decimal? spotHouse = (await CarPart()).Cost;

        Assert.Equal(2.00m, overLimit);
        Assert.Equal(overLimit, spotHouse);
    }

    // ---- US2: counted with the house ------------------------------------------------------------------------------

    [Fact]
    public async Task Included_WithinTheLimit_IsTheKwhAtThePricesEndpointsRate()
    {
        Use(norgesPris: true, included: true);
        for (DateTime hour = Utc("2026-11-30T23:00:00Z"); hour < Utc("2026-12-10T12:00:00Z"); hour = hour.AddHours(1))
        {
            Reading(hour, 1m);
        }

        Car(CarHour, 4.4m);
        Car(Utc("2026-12-03T02:00:00Z"), 3m); // 03:00 Oslo, a night hour

        decimal? cost = (await CarPart()).Cost;

        var prices = new GetDayPricesQueryHandler(
            _locations, _consumption, Calculator(), Substitute.For<ILogger<GetDayPricesQueryHandler>>(), _charging);
        decimal expected = 0m;
        foreach (EvChargerHourEnergy hour in _car)
        {
            DayPricesResponse day = (await prices.Handle(new GetDayPricesQuery(Guid.NewGuid(), _location.Id,
                TimeZoneInfo.ConvertTimeFromUtc(hour.HourStartUtc, TimeZoneInfo.FindSystemTimeZoneById("Europe/Oslo"))
                    .Date), default)).Value;
            expected += hour.Kwh * day.Items.Single(i => i.HourStart == hour.HourStartUtc).RatePerKwh!.Value;
        }

        Assert.Equal(expected, cost);
        Assert.Equal(4.4m * NorgesPrisHour + 3m * (0.40m + 0.02m + 0.20m + 0.0813m) * 1.25m, cost);
    }

    [Fact]
    public async Task Included_TheCarPastTheLimit_IsPricedOnSpotTerms_SplitInTheHour()
    {
        Use(norgesPris: true, included: true);
        Reading(Utc("2026-12-01T05:00:00Z"), 990m);
        Car(CarHour, 20m); // no house reading in this hour

        EvChargerBillResponse car = await CarPart();

        Assert.Equal(10m * NorgesPrisHour + 10m * SpotHour, car.Cost);
        Assert.Equal("HousePrice", car.PricedAt);
        Assert.Equal(1, car.HoursWithoutMeterReading);
    }

    [Fact]
    public async Task Included_TheCarOfAnHourComesAfterTheHouseOfThatHour()
    {
        Use(norgesPris: true, included: true);
        Reading(Utc("2026-12-01T05:00:00Z"), 990m);
        Reading(CarHour, 5m);
        Car(CarHour, 20m);

        Assert.Equal(5m * NorgesPrisHour + 15m * SpotHour, (await CarPart()).Cost);
    }

    [Fact]
    public async Task Included_EarlierCarHoursCountTowardTheLimit()
    {
        Use(norgesPris: true, included: true);
        Reading(Utc("2026-12-01T05:00:00Z"), 990m);
        Car(Utc("2026-12-01T20:00:00Z"), 10m); // reaches the limit
        Car(CarHour, 4m);

        Assert.Equal(10m * NorgesPrisHour + 4m * SpotHour, (await CarPart()).Cost);
    }

    [Fact]
    public async Task Included_WithoutASpotPrice_HasNoCost_ButSaysTheRule()
    {
        Use(norgesPris: false, included: true);
        _spot = hour => hour == CarHour ? null : 100m;
        Car(CarHour, 4m);

        EvChargerBillResponse car = await CarPart();

        Assert.Null(car.Cost);
        Assert.Equal("HousePrice", car.PricedAt);
    }

    // ---- US4 and the unchanged bill -------------------------------------------------------------------------------

    [Fact]
    public async Task AnyoneWhoMayReadTheBill_GetsTheCost_AndNoAccessIsStillNotFound()
    {
        Car(CarHour, 4m);
        var other = Guid.NewGuid();
        _locations.IsUserAssociatedAsync(Arg.Any<Guid>(), other, Arg.Any<CancellationToken>()).Returns(false);

        Assert.NotNull((await CarPart()).Cost);
        Result<MonthlyBillResponse> denied = await BillResult(other);
        Assert.False(denied.IsSuccess);
        Assert.Equal(ErrorType.NotFound, denied.Error.Type);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TheCost_IsAddedToNoSum(bool included)
    {
        Use(norgesPris: true, included: included);
        for (DateTime hour = Utc("2026-11-30T23:00:00Z"); hour < Utc("2026-12-10T12:00:00Z"); hour = hour.AddHours(1))
        {
            Reading(hour, 2m);
        }

        Car(CarHour, 40m);

        MonthlyBillResponse bill = (await BillResult()).Value;

        Assert.NotNull(bill.EvCharger!.Cost);
        Assert.Equal(bill.Strom.Amount + bill.Nettleie.Amount, bill.Total.Amount);
        Assert.Equal(bill.Strom.Lines.Sum(l => l.Amount ?? 0m), bill.Strom.Amount);
        Assert.Equal(bill.Nettleie.Lines.Sum(l => l.Amount ?? 0m), bill.Nettleie.Amount);
    }
}
