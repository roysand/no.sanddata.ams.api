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
/// The EV charger's part in the hourly, daily and current-hour figures (spec 016, US3): totalKwh adds the charger only
/// when the location counts it with the house, and no cost ever changes.
/// </summary>
public class EvChargerCostTests
{
    private static readonly DateTime Day = new(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc); // 02:00 in Oslo

    private readonly ILocationRepository<Location> _locations = Substitute.For<ILocationRepository<Location>>();
    private readonly IConsumptionRepository _consumption = Substitute.For<IConsumptionRepository>();
    private readonly IElectricityPriceRepository<ElectricityPrice> _prices =
        Substitute.For<IElectricityPriceRepository<ElectricityPrice>>();
    private readonly IExchangeRateRepository<ExchangeRate> _rates = Substitute.For<IExchangeRateRepository<ExchangeRate>>();
    private readonly IWaterHeaterPowerSampleRepository<WaterHeaterPowerSample> _samples =
        Substitute.For<IWaterHeaterPowerSampleRepository<WaterHeaterPowerSample>>();
    private readonly IChargingSessionRepository _charging = Substitute.For<IChargingSessionRepository>();
    private readonly CostCalculator _calculator;
    private readonly Location _location = new(Guid.NewGuid(), "Home", "Addr", "SN1", "NO1", true, false);
    private readonly List<HourConsumption> _hours = [];
    private readonly List<EvChargerHourEnergy> _chargerHours = [];

    public EvChargerCostTests()
    {
        _calculator = new CostCalculator(_prices, _rates, NorgesPrisRates.Seeded(), PriceRateStubs.Zero());
        _locations.IsUserAssociatedAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(true);
        _locations.GetByIdAsync(_location.Id, Arg.Any<CancellationToken>()).Returns(_location);
        _rates.GetByDateAsync("EURNOK", Arg.Any<DateOnly>(), Arg.Any<CancellationToken>())
            .Returns(call => new ExchangeRate(Guid.NewGuid(), "EURNOK", call.ArgAt<DateOnly>(1), 10m));
        _prices.GetByRegionAndHourAsync("NO1", Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns(call => new ElectricityPrice(Guid.NewGuid(), "NO1", call.ArgAt<DateTime>(1), 100m));
        _prices.ServeRangesFromSingleHours();
        _rates.ServeRangesFromSingleDates();
        _consumption.GetHourlyAsync(_location.Id, null, Arg.Any<DateTime>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns(_ => _hours.ToList());
        _charging.GetHourlyEnergyAsync(_location.Id, Arg.Any<DateTime>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns(call => _chargerHours
                .Where(h => h.HourStartUtc >= call.ArgAt<DateTime>(1) && h.HourStartUtc < call.ArgAt<DateTime>(2))
                .ToList());
    }

    private void Meter(DateTime hour, decimal kwh) =>
        _hours.Add(new HourConsumption { LocationId = _location.Id, BucketStart = hour, AvgPowerWatts = (double)(kwh * 1000m) });

    private void Charging(DateTime hour, decimal kwh, bool estimated = false) =>
        _chargerHours.Add(new EvChargerHourEnergy(hour, kwh, estimated));

    private HourlyCostProvider Provider() =>
        new(_consumption, _calculator, _samples, new FixedTimeProvider("2026-10-02T12:00:00Z"), _charging);

    private async Task<IReadOnlyList<HourlyCostItemResponse>> Hourly() =>
        (await new GetHourlyCostQueryHandler(_locations, Provider(), Substitute.For<ILogger<GetHourlyCostQueryHandler>>())
            .Handle(new GetHourlyCostQuery(Guid.NewGuid(), _location.Id, Day, Day.AddHours(3), 1, 100), default))
        .Value.Items;

    private async Task<IReadOnlyList<DailyCostItemResponse>> Daily() =>
        (await new GetDailyCostQueryHandler(_locations, Provider(), Substitute.For<ILogger<GetDailyCostQueryHandler>>())
            .Handle(new GetDailyCostQuery(Guid.NewGuid(), _location.Id, Day, Day), default))
        .Value.Items;

    [Fact]
    public async Task Hourly_Included_AddsTheChargerToTotalKwhOnly()
    {
        _location.SetIncludeEvCharger(true);
        Meter(Day, 1.5m);
        Meter(Day.AddHours(1), 2m);
        Charging(Day, 3.2m);

        IReadOnlyList<HourlyCostItemResponse> items = await Hourly();

        Assert.Equal(1.5m, items[0].ConsumptionKwh);
        Assert.Equal(4.7m, items[0].TotalKwh);
        Assert.Equal(new EvChargerHourResponse(3.2m, false, true), items[0].EvCharger);
        Assert.Null(items[1].EvCharger);
        Assert.Equal(2m, items[1].TotalKwh);
    }

    [Fact]
    public async Task Hourly_NotIncluded_KeepsTotalKwhAtTheMeterButShowsThePart()
    {
        Meter(Day, 1.5m);
        Charging(Day, 3.2m, estimated: true);

        HourlyCostItemResponse item = Assert.Single(await Hourly());

        Assert.Equal(1.5m, item.TotalKwh);
        Assert.Equal(new EvChargerHourResponse(3.2m, true, false), item.EvCharger);
    }

    [Fact]
    public async Task Hourly_TheSettingNeverChangesACost()
    {
        Meter(Day, 1.5m);
        Meter(Day.AddHours(1), 2m);
        Charging(Day, 3.2m);
        Charging(Day.AddHours(1), 4.4m);

        IReadOnlyList<HourlyCostItemResponse> off = await Hourly();
        _location.SetIncludeEvCharger(true);
        IReadOnlyList<HourlyCostItemResponse> on = await Hourly();

        Assert.Equal(off.Select(i => i.Actual!.Cost), on.Select(i => i.Actual!.Cost));
        Assert.Equal(off.Select(i => i.Comparison!.Cost), on.Select(i => i.Comparison!.Cost));
        Assert.Equal(off.Select(i => i.ConsumptionKwh), on.Select(i => i.ConsumptionKwh));
        Assert.Equal(off.Select(i => i with { TotalKwh = 0m, EvCharger = null }),
            on.Select(i => i with { TotalKwh = 0m, EvCharger = null }));
    }

    [Fact]
    public async Task Hourly_WithoutChargingData_HasNoPart()
    {
        Meter(Day, 1.5m);

        HourlyCostItemResponse item = Assert.Single(await Hourly());

        Assert.Null(item.EvCharger);
        Assert.Equal(1.5m, item.TotalKwh);
    }

    [Fact]
    public async Task Daily_SumsTheHours_AndCountsChargingWithoutAMeterReading()
    {
        _location.SetIncludeEvCharger(true);
        Meter(Day, 1m);
        Meter(Day.AddHours(1), 1m);
        Charging(Day, 3m);
        Charging(Day.AddHours(2), 4m, estimated: true); // no meter reading in this hour

        DailyCostItemResponse day = Assert.Single(await Daily());

        Assert.Equal(2m, day.ConsumptionKwh);
        Assert.Equal(new EvChargerDayResponse(7m, true, true, 1), day.EvCharger);
        Assert.Equal(9m, day.TotalKwh);
    }

    [Fact]
    public async Task Daily_NotIncluded_KeepsTheTotalAtTheMeter()
    {
        Meter(Day, 1m);
        Charging(Day, 3m);

        DailyCostItemResponse day = Assert.Single(await Daily());

        Assert.Equal(1m, day.TotalKwh);
        Assert.False(day.EvCharger!.Included);
    }

    [Fact]
    public async Task Daily_TheCostIsTheSameWithTheSettingOnOrOff()
    {
        Meter(Day, 1m);
        Charging(Day, 3m);
        Charging(Day.AddHours(2), 4m);

        DailyCostItemResponse off = Assert.Single(await Daily());
        _location.SetIncludeEvCharger(true);
        DailyCostItemResponse on = Assert.Single(await Daily());

        Assert.Equal(off.Actual, on.Actual);
        Assert.Equal(off.Comparison, on.Comparison);
    }

    [Fact]
    public async Task CurrentHour_CarriesTheChargerWhenASessionEndedInIt()
    {
        _location.SetIncludeEvCharger(true);
        _consumption.GetHourConsumptionKwhAsync(_location.Id, Arg.Any<DateTime>(), Arg.Any<CancellationToken>()).Returns(2m);
        _charging.GetHourlyEnergyAsync(_location.Id, Arg.Any<DateTime>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns(call => [new EvChargerHourEnergy(call.ArgAt<DateTime>(1), 1.25m, false)]);
        var handler = new GetCurrentHourCostQueryHandler(_locations, _consumption, _calculator,
            Substitute.For<ILogger<GetCurrentHourCostQueryHandler>>(), _charging);

        Result<CurrentHourCostResponse> result =
            await handler.Handle(new GetCurrentHourCostQuery(Guid.NewGuid(), _location.Id), default);

        Assert.Equal(2m, result.Value.ConsumptionKwhSoFar);
        Assert.Equal(3.25m, result.Value.TotalKwhSoFar);
        Assert.Equal(new EvChargerHourResponse(1.25m, false, true), result.Value.EvCharger);
        Assert.Equal(2m, result.Value.Actual!.CostSoFar);
    }
}
