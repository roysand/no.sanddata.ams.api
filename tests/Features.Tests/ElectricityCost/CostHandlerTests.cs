using Application.Common.Interfaces.Repositories;
using Application.ElectricityCost;
using Application.WaterHeaters;
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

public class CostHandlerTests
{
    private static readonly DateTime Day = new(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc);

    private readonly ILocationRepository<Location> _locations = Substitute.For<ILocationRepository<Location>>();
    private readonly IConsumptionRepository _consumption = Substitute.For<IConsumptionRepository>();
    private readonly IElectricityPriceRepository<ElectricityPrice> _prices =
        Substitute.For<IElectricityPriceRepository<ElectricityPrice>>();
    private readonly IExchangeRateRepository<ExchangeRate> _rates = Substitute.For<IExchangeRateRepository<ExchangeRate>>();
    private readonly CostCalculator _calculator;
    private readonly Location _location = new(Guid.NewGuid(), "Home", "Addr", "SN1", "NO1", true, false);

    public CostHandlerTests()
    {
        _calculator = new CostCalculator(_prices, _rates, NorgesPrisRates.Seeded(), PriceRateStubs.Zero());
        _locations.IsUserAssociatedAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(true);
        _locations.GetByIdAsync(_location.Id, Arg.Any<CancellationToken>()).Returns(_location);
        _rates.GetByDateAsync("EURNOK", Arg.Any<DateOnly>(), Arg.Any<CancellationToken>())
            .Returns(call => new ExchangeRate(Guid.NewGuid(), "EURNOK", call.ArgAt<DateOnly>(1), 10m));
        _prices.ServeRangesFromSingleHours();
        _rates.ServeRangesFromSingleDates();
    }

    private void GivenPriceForHour(DateTime hour, decimal eurPerMwh) =>
        _prices.GetByRegionAndHourAsync("NO1", hour, Arg.Any<CancellationToken>())
            .Returns(new ElectricityPrice(Guid.NewGuid(), "NO1", hour, eurPerMwh));

    private readonly List<HourConsumption> _hours = [];

    private void AddHour(DateTime hour, double avgWatts, decimal? eurPerMwh)
    {
        _hours.Add(new HourConsumption { LocationId = _location.Id, BucketStart = hour, AvgPowerWatts = avgWatts });
        if (eurPerMwh is { } price)
        {
            GivenPriceForHour(hour, price);
        }

        _consumption.GetHourlyAsync(_location.Id, null, Arg.Any<DateTime>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns(_hours.ToList());
    }

    private readonly IWaterHeaterPowerSampleRepository<WaterHeaterPowerSample> _samples =
        Substitute.For<IWaterHeaterPowerSampleRepository<WaterHeaterPowerSample>>();

    private HourlyCostProvider Provider() =>
        new(_consumption, _calculator, _samples, new FixedTimeProvider("2026-10-02T12:00:00Z"));

    [Fact]
    public async Task CurrentHour_UnknownLocation_ReturnsLocationNotFound()
    {
        _locations.IsUserAssociatedAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(false);
        var handler = new GetCurrentHourCostQueryHandler(_locations, _consumption, _calculator,
            Substitute.For<ILogger<GetCurrentHourCostQueryHandler>>());

        Result<CurrentHourCostResponse> result =
            await handler.Handle(new GetCurrentHourCostQuery(Guid.NewGuid(), _location.Id), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("Location.NotFound", result.Error.Code);
    }

    [Fact]
    public async Task CurrentHour_ReturnsConsumptionAndBothCosts()
    {
        _consumption.GetHourConsumptionKwhAsync(_location.Id, Arg.Any<DateTime>(), Arg.Any<CancellationToken>()).Returns(2m);
        _prices.GetByRegionAndHourAsync("NO1", Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns(call => new ElectricityPrice(Guid.NewGuid(), "NO1", call.ArgAt<DateTime>(1), 100m));
        var handler = new GetCurrentHourCostQueryHandler(_locations, _consumption, _calculator,
            Substitute.For<ILogger<GetCurrentHourCostQueryHandler>>());

        Result<CurrentHourCostResponse> result =
            await handler.Handle(new GetCurrentHourCostQuery(Guid.NewGuid(), _location.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(2m, result.Value.ConsumptionKwhSoFar);
        Assert.Equal(2m, result.Value.Actual!.CostSoFar); // 0.1 EUR/kWh * 10 NOK/EUR * 2 kWh
        Assert.Equal(0.8m, result.Value.Comparison!.CostSoFar);
        Assert.Equal(0, result.Value.HourStart.Minute);
    }

    [Fact]
    public async Task Hourly_UnknownLocation_ReturnsLocationNotFound()
    {
        _locations.IsUserAssociatedAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(false);
        var handler = new GetHourlyCostQueryHandler(_locations, Provider(), Substitute.For<ILogger<GetHourlyCostQueryHandler>>());

        Result<PagedHourlyCostResponse> result = await handler.Handle(
            new GetHourlyCostQuery(Guid.NewGuid(), _location.Id, null, null, 1, 10), CancellationToken.None);

        Assert.Equal("Location.NotFound", result.Error.Code);
    }

    [Fact]
    public async Task Hourly_SumsMetersPerHourAndPages()
    {
        AddHour(Day, 1000, 100m);
        _hours.Add(new HourConsumption { LocationId = _location.Id, MeterId = Guid.NewGuid(), BucketStart = Day, AvgPowerWatts = 1000 });
        AddHour(Day.AddHours(1), 2000, 100m);
        var handler = new GetHourlyCostQueryHandler(_locations, Provider(), Substitute.For<ILogger<GetHourlyCostQueryHandler>>());

        Result<PagedHourlyCostResponse> result = await handler.Handle(
            new GetHourlyCostQuery(Guid.NewGuid(), _location.Id, Day, Day.AddHours(2), 1, 1), CancellationToken.None);

        Assert.Equal(2, result.Value.TotalCount);
        Assert.Single(result.Value.Items);
        Assert.Equal(2m, result.Value.Items[0].ConsumptionKwh); // two meters x 1 kW in the first hour
    }

    [Fact]
    public async Task Daily_EqualsSumOfHourlyCosts()
    {
        AddHour(Day.AddHours(10), 1000, 100m); // 1 kWh * 1.0 NOK
        AddHour(Day.AddHours(11), 2000, 200m); // 2 kWh * 2.0 NOK
        var handler = new GetDailyCostQueryHandler(_locations, Provider(), Substitute.For<ILogger<GetDailyCostQueryHandler>>());

        Result<DailyCostResponse> result = await handler.Handle(
            new GetDailyCostQuery(Guid.NewGuid(), _location.Id, Day, Day), CancellationToken.None);

        DailyCostItemResponse item = Assert.Single(result.Value.Items);
        Assert.Equal(3m, item.ConsumptionKwh);
        Assert.Equal(5m, item.Actual!.Cost);
        Assert.Equal(1.2m, item.Comparison!.Cost);
    }

    [Fact]
    public async Task Daily_AnyHourWithoutPrice_MakesSpotCostNullButKeepsComparison()
    {
        AddHour(Day.AddHours(10), 1000, 100m);
        AddHour(Day.AddHours(11), 1000, null);
        var handler = new GetDailyCostQueryHandler(_locations, Provider(), Substitute.For<ILogger<GetDailyCostQueryHandler>>());

        Result<DailyCostResponse> result = await handler.Handle(
            new GetDailyCostQuery(Guid.NewGuid(), _location.Id, Day, Day), CancellationToken.None);

        DailyCostItemResponse item = Assert.Single(result.Value.Items);
        Assert.Null(item.Actual);
        Assert.NotNull(item.Comparison);
    }

    [Fact]
    public async Task Daily_GroupsByOsloLocalDay_NotUtcDay()
    {
        // 2026-10-01 is CEST (UTC+2): local midnight 2 October is 22:00 UTC on 1 October.
        AddHour(new DateTime(2026, 10, 1, 21, 0, 0, DateTimeKind.Utc), 1000, 100m); // 23:00 local, 1 October
        AddHour(new DateTime(2026, 10, 1, 22, 0, 0, DateTimeKind.Utc), 1000, 100m); // 00:00 local, 2 October
        var handler = new GetDailyCostQueryHandler(_locations, Provider(), Substitute.For<ILogger<GetDailyCostQueryHandler>>());

        Result<DailyCostResponse> result = await handler.Handle(
            new GetDailyCostQuery(Guid.NewGuid(), _location.Id, new DateTime(2026, 10, 1), new DateTime(2026, 10, 2)),
            CancellationToken.None);

        Assert.Equal([new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 2)], result.Value.Items.Select(i => i.Date));
    }

    [Fact]
    public async Task Daily_RequestsLocalDayBoundariesAsUtcRange()
    {
        var handler = new GetDailyCostQueryHandler(_locations, Provider(), Substitute.For<ILogger<GetDailyCostQueryHandler>>());

        await handler.Handle(
            new GetDailyCostQuery(Guid.NewGuid(), _location.Id, new DateTime(2026, 10, 1), new DateTime(2026, 10, 1)),
            CancellationToken.None);

        // CEST: 1 October local = 30 September 22:00 UTC to 1 October 22:00 UTC.
        await _consumption.Received().GetHourlyAsync(_location.Id, null,
            new DateTime(2026, 9, 30, 22, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 10, 1, 22, 0, 0, DateTimeKind.Utc), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Daily_UnknownLocation_ReturnsLocationNotFound()
    {
        _locations.IsUserAssociatedAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(false);
        var handler = new GetDailyCostQueryHandler(_locations, Provider(), Substitute.For<ILogger<GetDailyCostQueryHandler>>());

        Result<DailyCostResponse> result =
            await handler.Handle(new GetDailyCostQuery(Guid.NewGuid(), _location.Id, null, null), CancellationToken.None);

        Assert.Equal("Location.NotFound", result.Error.Code);
    }

    // ---- the water heater's part (spec 012) ----------------------------------------------------------------------

    private void GivenHeaterEnergy(params WaterHeaterHourEnergy[] rows) =>
        _samples.GetHourlyEnergyAsync(_location.Id, Arg.Any<DateTime>(), Arg.Any<DateTime>(), Arg.Any<DateTime>(),
                Arg.Any<CancellationToken>())
            .Returns(rows);

    private Task<Result<PagedHourlyCostResponse>> Hourly() =>
        new GetHourlyCostQueryHandler(_locations, Provider(), Substitute.For<ILogger<GetHourlyCostQueryHandler>>())
            .Handle(new GetHourlyCostQuery(Guid.NewGuid(), _location.Id, Day, Day.AddDays(1), 1, 100),
                CancellationToken.None);

    private Task<Result<DailyCostResponse>> Daily() =>
        new GetDailyCostQueryHandler(_locations, Provider(), Substitute.For<ILogger<GetDailyCostQueryHandler>>())
            .Handle(new GetDailyCostQuery(Guid.NewGuid(), _location.Id, Day, Day), CancellationToken.None);

    [Fact]
    public async Task Hourly_AnHourWithHeaterSamples_CarriesItsShare()
    {
        AddHour(Day.AddHours(10), 2000, 100m); // 2 kWh at 1.0 NOK = 2.00
        AddHour(Day.AddHours(11), 1000, 100m);
        GivenHeaterEnergy(new WaterHeaterHourEnergy(Day.AddHours(10), 1.5m, 3600m));

        Result<PagedHourlyCostResponse> result = await Hourly();

        HourlyCostItemResponse heated = result.Value.Items.Single(i => i.HourStart == Day.AddHours(10));
        Assert.NotNull(heated.WaterHeater);
        Assert.Equal(1.5m, heated.WaterHeater.Kwh);
        Assert.Equal(heated.Actual!.Cost * 1.5m / 2m, heated.WaterHeater.Cost);
        Assert.Equal(60, heated.WaterHeater.SampledMinutes);
        Assert.False(heated.WaterHeater.Capped);
        Assert.Null(result.Value.Items.Single(i => i.HourStart == Day.AddHours(11)).WaterHeater);
    }

    [Fact]
    public async Task Hourly_TheExistingFields_AreTheSameWithAndWithoutHeaterSamples()
    {
        AddHour(Day.AddHours(10), 2000, 100m);
        HourlyCostItemResponse without = (await Hourly()).Value.Items.Single();
        GivenHeaterEnergy(new WaterHeaterHourEnergy(Day.AddHours(10), 1.5m, 3600m));

        HourlyCostItemResponse with = (await Hourly()).Value.Items.Single();

        Assert.Null(without.WaterHeater);
        Assert.Equal(without, with with { WaterHeater = null });
    }

    [Fact]
    public async Task Hourly_AskForTheHeaterUntilTheEndOfTheLastHourAndClipAtNow()
    {
        AddHour(Day.AddHours(10), 2000, 100m);

        await new GetHourlyCostQueryHandler(_locations, Provider(), Substitute.For<ILogger<GetHourlyCostQueryHandler>>())
            .Handle(new GetHourlyCostQuery(Guid.NewGuid(), _location.Id, Day, Day.AddHours(10).AddMinutes(30), 1, 10),
                CancellationToken.None);

        await _samples.Received(1).GetHourlyEnergyAsync(
            _location.Id, Day, Day.AddHours(11), new DateTime(2026, 10, 2, 12, 0, 0, DateTimeKind.Utc),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Daily_TheHeaterPart_IsTheSumOfItsHours()
    {
        AddHour(Day.AddHours(10), 1000, 100m);
        AddHour(Day.AddHours(11), 2000, 200m);
        AddHour(Day.AddHours(12), 1000, 100m);
        GivenHeaterEnergy(
            new WaterHeaterHourEnergy(Day.AddHours(10), 0.5m, 3600m),
            new WaterHeaterHourEnergy(Day.AddHours(11), 3.0m, 1800m)); // more than the meter's 2 kWh: capped

        PagedHourlyCostResponse hours = (await Hourly()).Value;
        DailyCostItemResponse day = Assert.Single((await Daily()).Value.Items);

        Assert.NotNull(day.WaterHeater);
        Assert.Equal(hours.Items.Sum(i => i.WaterHeater?.Kwh ?? 0m), day.WaterHeater.Kwh);
        Assert.Equal(hours.Items.Sum(i => i.WaterHeater?.Cost ?? 0m), day.WaterHeater.Cost);
        Assert.Equal(2.5m, day.WaterHeater.Kwh);
        Assert.Equal(90, day.WaterHeater.SampledMinutes);
        Assert.True(day.WaterHeater.Capped);
    }

    [Fact]
    public async Task Daily_ASampledHourWithoutPrice_MakesTheHeaterCostNull()
    {
        AddHour(Day.AddHours(10), 1000, 100m);
        AddHour(Day.AddHours(11), 1000, null);
        GivenHeaterEnergy(new WaterHeaterHourEnergy(Day.AddHours(11), 0.5m, 3600m));

        DailyCostItemResponse day = Assert.Single((await Daily()).Value.Items);

        Assert.NotNull(day.WaterHeater);
        Assert.Equal(0.5m, day.WaterHeater.Kwh);
        Assert.Null(day.WaterHeater.Cost);
    }

    [Fact]
    public async Task Daily_ADayWithoutHeaterSamples_HasNoHeaterPart()
    {
        AddHour(Day.AddHours(10), 1000, 100m);

        DailyCostItemResponse day = Assert.Single((await Daily()).Value.Items);

        Assert.Null(day.WaterHeater);
    }

    [Fact]
    public async Task ALocationTheUserCannotSee_NeverReadsTheHeaterSamples()
    {
        _locations.IsUserAssociatedAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(false);

        Assert.Equal("Location.NotFound", (await Hourly()).Error.Code);
        Assert.Equal("Location.NotFound", (await Daily()).Error.Code);
        await _samples.DidNotReceiveWithAnyArgs().GetHourlyEnergyAsync(default, default, default, default, default);
    }

    [Fact]
    public async Task TheProviderWithoutTheFlag_NeverReadsHeaterSamples()
    {
        // The Norgespris saving calls the provider without the flag and keeps its query count (research R6).
        AddHour(Day.AddHours(10), 2000, 100m);
        GivenHeaterEnergy(new WaterHeaterHourEnergy(Day.AddHours(10), 1.5m, 3600m));

        IReadOnlyList<HourCost> hours = await Provider().GetAsync(_location, Day, Day.AddDays(1), CancellationToken.None);

        Assert.Null(Assert.Single(hours).WaterHeater);
        await _samples.DidNotReceiveWithAnyArgs().GetHourlyEnergyAsync(default, default, default, default, default);
    }
}
