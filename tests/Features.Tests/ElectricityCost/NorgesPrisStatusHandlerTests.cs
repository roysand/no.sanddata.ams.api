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
/// The Norgespris limit status (spec 018, US1 and US3): which locations, the limit, the kWh so far with the running
/// hour and the car, the bill's projection, over the limit, the price including VAT and the reset time. Norgespris
/// 0.40, VAT 25 percent; holiday-home limit 1 000 kWh, home 5 000 kWh.
/// </summary>
public class NorgesPrisStatusHandlerTests
{
    private const string Now = "2026-12-10T12:30:00Z";

    private readonly Guid _userId = Guid.NewGuid();
    private readonly ILocationRepository<Location> _locations = Substitute.For<ILocationRepository<Location>>();
    private readonly IConsumptionRepository _consumption = Substitute.For<IConsumptionRepository>();
    private readonly IChargingSessionRepository _charging = Substitute.For<IChargingSessionRepository>();
    private readonly IElectricityPriceRepository<ElectricityPrice> _prices =
        Substitute.For<IElectricityPriceRepository<ElectricityPrice>>();
    private readonly IExchangeRateRepository<ExchangeRate> _fx = Substitute.For<IExchangeRateRepository<ExchangeRate>>();
    private readonly IPriceRateRepository _rates = PriceRateStubs.Repository(
        PriceRateStubs.National2026()
            .Append(PriceRateStubs.Rate(PriceRateKind.NorgesPrisCapHolidayHome, "2025-10-01", 1000m))
            .Append(PriceRateStubs.Rate(PriceRateKind.NorgesPrisCapHome, "2025-10-01", 5000m)),
        []);
    private readonly List<HourConsumption> _readings = [];
    private readonly List<(Guid Location, EvChargerHourEnergy Hour)> _car = [];
    private readonly List<LocationWithRole> _list = [];

    public NorgesPrisStatusHandlerTests()
    {
        _prices.GetByRegionAndHourAsync("NO1", Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns(call => new ElectricityPrice(Guid.NewGuid(), "NO1", call.ArgAt<DateTime>(1), 100m));
        _fx.GetByDateAsync("EURNOK", Arg.Any<DateOnly>(), Arg.Any<CancellationToken>())
            .Returns(call => new ExchangeRate(Guid.NewGuid(), "EURNOK", call.ArgAt<DateOnly>(1), 10m));
        _prices.ServeRangesFromSingleHours();
        _fx.ServeRangesFromSingleDates();

        _locations.GetForUserWithRoleAsync(_userId, Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult<IReadOnlyList<LocationWithRole>>(_list.ToList()));
        _locations.IsUserAssociatedAsync(_userId, Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(call => _list.Any(l => l.Location.Id == call.ArgAt<Guid>(1)));
        _locations.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(call => _list.Select(l => l.Location).FirstOrDefault(l => l.Id == call.ArgAt<Guid>(0)));
        _consumption.GetHourlyAsync(
                Arg.Any<Guid>(), null, Arg.Any<DateTime>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns(call => Task.FromResult<IReadOnlyList<HourConsumption>>(_readings
                .Where(r => r.LocationId == call.ArgAt<Guid>(0)
                            && r.BucketStart >= call.ArgAt<DateTime>(2) && r.BucketStart < call.ArgAt<DateTime>(3))
                .ToList()));
        _charging.GetHourlyEnergyAsync(Arg.Any<Guid>(), Arg.Any<DateTime>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns(call => Task.FromResult<IReadOnlyList<EvChargerHourEnergy>>(_car
                .Where(c => c.Location == call.ArgAt<Guid>(0)
                            && c.Hour.HourStartUtc >= call.ArgAt<DateTime>(1) && c.Hour.HourStartUtc < call.ArgAt<DateTime>(2))
                .Select(c => c.Hour)
                .ToList()));
    }

    private static DateTime Utc(string iso) =>
        DateTime.Parse(iso, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal);

    private Location Add(
        string name,
        bool norgesPris = true,
        bool holidayHome = false,
        bool active = true,
        LocationRole role = LocationRole.Owner,
        bool vatExempt = false)
    {
        var location = new Location(Guid.NewGuid(), name, "Addr", "SN-" + name, "NO1", active, norgesPris);
        location.SetPricing(null, 0.02m, false, vatExempt, holidayHome);
        _list.Add(new LocationWithRole(location, role));
        return location;
    }

    private void Reading(Location location, DateTime hour, decimal kwh) => _readings.Add(new HourConsumption
    {
        LocationId = location.Id, MeterId = Guid.Empty, BucketStart = hour, AvgPowerWatts = (double)(kwh * 1000m)
    });

    private void ReadingsEveryHour(Location location, DateTime from, DateTime to, decimal kwh)
    {
        for (DateTime hour = from; hour < to; hour = hour.AddHours(1))
        {
            Reading(location, hour, kwh);
        }
    }

    private CostCalculator Calculator() => new(_prices, _fx, NorgesPrisRates.Seeded(), _rates);

    private async Task<NorgesPrisStatusResponse> Status(string now = Now) =>
        (await new GetNorgesPrisStatusQueryHandler(
                _locations, _consumption, Calculator(), new FixedTimeProvider(now),
                Substitute.For<ILogger<GetNorgesPrisStatusQueryHandler>>(), _charging)
            .Handle(new GetNorgesPrisStatusQuery(_userId), default))
        .Value;

    private async Task<MonthlyBillResponse> Bill(Location location, string now = Now)
    {
        CostCalculator calculator = Calculator();
        return (await new GetMonthlyBillQueryHandler(
                _locations, _consumption, _rates,
                new HourlyCostProvider(_consumption, calculator,
                    Substitute.For<IWaterHeaterPowerSampleRepository<WaterHeaterPowerSample>>(),
                    new FixedTimeProvider(now), _charging),
                calculator, new FixedTimeProvider(now), Substitute.For<ILogger<GetMonthlyBillQueryHandler>>())
            .Handle(new GetMonthlyBillQuery(_userId, location.Id, null), default))
            .Value;
    }

    [Fact]
    public async Task OnlyActiveNorgesPrisLocationsOfTheList_AreListed_InItsOrder()
    {
        Location cabin = Add("Cabin", holidayHome: true);
        Add("Spot", norgesPris: false);
        Add("Off", active: false);
        Location home = Add("Home", role: LocationRole.Viewer);

        NorgesPrisStatusResponse status = await Status();

        Assert.Equal([cabin.Id, home.Id], status.Items.Select(i => i.LocationId));
        Assert.Equal(Utc(Now), status.AsOf);
    }

    [Fact]
    public async Task NoNorgesPrisLocations_GiveAnEmptyList()
    {
        Add("Spot", norgesPris: false);

        Assert.Empty((await Status()).Items);
    }

    [Fact]
    public async Task TheLimit_IsTheHolidayHomeOrTheHomeLimit()
    {
        Add("Cabin", holidayHome: true);
        Add("Home");

        NorgesPrisStatusResponse status = await Status();

        Assert.Equal([1000m, 5000m], status.Items.Select(i => i.LimitKwh));
    }

    [Fact]
    public async Task TheMonthAndTheReset_AreTheOsloMonth()
    {
        Add("Cabin");

        // 23:30 UTC on 30 November is 00:30 on 1 December in Oslo.
        NorgesPrisStatusItem item = Assert.Single((await Status("2026-11-30T23:30:00Z")).Items);

        Assert.Equal("2026-12", item.Month);
        Assert.Equal(Utc("2026-12-31T23:00:00Z"), item.ResetsAt);
    }

    [Fact]
    public async Task TheKwhSoFar_StartAtTheMonth_AndIncludeTheRunningHour()
    {
        Location cabin = Add("Cabin", holidayHome: true);
        Reading(cabin, Utc("2026-11-30T22:00:00Z"), 50m); // 23:00 on 30 November Oslo: last month
        Reading(cabin, Utc("2026-11-30T23:00:00Z"), 3m);
        Reading(cabin, Utc("2026-12-10T12:00:00Z"), 0.7m); // the running hour so far

        NorgesPrisStatusItem item = Assert.Single((await Status()).Items);

        Assert.Equal(3.7m, item.KwhSoFar);
    }

    [Theory]
    [InlineData(true, 13.7)]
    [InlineData(false, 3.7)]
    public async Task TheCar_CountsOnlyWhenCountedWithTheHouse(bool included, decimal expected)
    {
        Location cabin = Add("Cabin", holidayHome: true);
        cabin.SetIncludeEvCharger(included);
        Reading(cabin, Utc("2026-12-01T05:00:00Z"), 3.7m);
        _car.Add((cabin.Id, new EvChargerHourEnergy(Utc("2026-12-02T20:00:00Z"), 10m, false)));
        _car.Add((cabin.Id, new EvChargerHourEnergy(Utc("2026-11-30T20:00:00Z"), 99m, false))); // last month

        NorgesPrisStatusItem item = Assert.Single((await Status()).Items);

        Assert.Equal(expected, item.KwhSoFar);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task TheProjection_IsTheBillsProjectedTotal(bool carIncluded)
    {
        Location cabin = Add("Cabin", holidayHome: true);
        cabin.SetIncludeEvCharger(carIncluded);
        ReadingsEveryHour(cabin, Utc("2026-11-25T00:00:00Z"), Utc("2026-12-10T12:00:00Z"), 1.5m);
        Reading(cabin, Utc("2026-12-04T08:00:00Z"), 4m);
        Reading(cabin, Utc("2026-12-10T12:00:00Z"), 0.4m);
        _car.Add((cabin.Id, new EvChargerHourEnergy(Utc("2026-12-02T20:00:00Z"), 12.5m, false)));

        NorgesPrisStatusItem item = Assert.Single((await Status()).Items);
        MonthlyBillResponse bill = await Bill(cabin);

        Assert.Equal(bill.TotalKwh + bill.Projection!.Kwh, item.ProjectedKwh);
    }

    [Fact]
    public async Task WithoutReadings_TheKwhSoFarAre0_AndThereIsNoProjection()
    {
        Add("Cabin", holidayHome: true);

        NorgesPrisStatusItem item = Assert.Single((await Status()).Items);

        Assert.Equal(0m, item.KwhSoFar);
        Assert.Null(item.ProjectedKwh);
        Assert.False(item.OverLimit);
    }

    [Theory]
    [InlineData(999.9, false, 0)]
    [InlineData(1000, true, 0)]
    [InlineData(1043.62, true, 43.62)]
    public async Task OverTheLimit_IsAtOrAboveIt_AndSaysByHowMuch(decimal kwh, bool over, decimal by)
    {
        Location cabin = Add("Cabin", holidayHome: true);
        Reading(cabin, Utc("2026-12-01T05:00:00Z"), kwh);

        NorgesPrisStatusItem item = Assert.Single((await Status()).Items);

        Assert.Equal(over, item.OverLimit);
        Assert.Equal(by, item.OverLimitKwh);
    }

    [Fact]
    public async Task ThePrice_IsTheNorgesPrisRateWithVat()
    {
        Add("Cabin");
        Add("North", vatExempt: true);

        NorgesPrisStatusResponse status = await Status();

        Assert.Equal([0.50m, 0.40m], status.Items.Select(i => i.NorgesPrisPricePerKwh));
    }
}
