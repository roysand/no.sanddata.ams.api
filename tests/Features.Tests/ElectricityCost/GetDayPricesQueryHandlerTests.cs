using Application.Common.Interfaces.Repositories;
using Application.ElectricityCost;
using Domain.Common;
using Domain.Common.Entities;
using Features.ElectricityCost.Handlers;
using Features.ElectricityCost.Queries;
using Features.Tests.NorgesPris;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Features.Tests.ElectricityCost;

public class GetDayPricesQueryHandlerTests
{
    private readonly ILocationRepository<Location> _locations = Substitute.For<ILocationRepository<Location>>();
    private readonly IElectricityPriceRepository<ElectricityPrice> _prices =
        Substitute.For<IElectricityPriceRepository<ElectricityPrice>>();
    private readonly IExchangeRateRepository<ExchangeRate> _rates = Substitute.For<IExchangeRateRepository<ExchangeRate>>();
    private readonly Location _spot = new(Guid.NewGuid(), "Home", "Addr", "SN1", "NO1", true, false);
    private readonly Location _norgesPris = new(Guid.NewGuid(), "Cabin", "Addr", "SN2", "NO1", true, true);

    public GetDayPricesQueryHandlerTests()
    {
        _locations.IsUserAssociatedAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(true);
        _locations.GetByIdAsync(_spot.Id, Arg.Any<CancellationToken>()).Returns(_spot);
        _locations.GetByIdAsync(_norgesPris.Id, Arg.Any<CancellationToken>()).Returns(_norgesPris);
        _rates.GetByDateAsync("EURNOK", Arg.Any<DateOnly>(), Arg.Any<CancellationToken>())
            .Returns(call => new ExchangeRate(Guid.NewGuid(), "EURNOK", call.ArgAt<DateOnly>(1), 10m));
    }

    private GetDayPricesQueryHandler Handler() => new(
        _locations,
        new CostCalculator(_prices, _rates, NorgesPrisRates.Seeded()),
        Substitute.For<ILogger<GetDayPricesQueryHandler>>());

    [Theory]
    [InlineData("2026-10-01", 24)]
    [InlineData("2026-03-29", 23)] // clocks go forward
    [InlineData("2026-10-25", 25)] // clocks go back
    public async Task Handle_ReturnsOneItemPerRealHourOfTheOsloDay(string date, int expectedHours)
    {
        Result<DayPricesResponse> result = await Handler().Handle(
            new GetDayPricesQuery(Guid.NewGuid(), _spot.Id, DateTime.Parse(date)), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(expectedHours, result.Value.Items.Count);
        Assert.Equal("Spot", result.Value.PricingModel);
        Assert.Equal("NO1", result.Value.Zone);
    }

    [Fact]
    public async Task Handle_RateIsTheCostCalculatorRate()
    {
        DateTime hour = new(2026, 9, 30, 22, 0, 0, DateTimeKind.Utc); // 2026-10-01 00:00 in Oslo
        _prices.GetByRegionAndHourAsync("NO1", hour, Arg.Any<CancellationToken>())
            .Returns(new ElectricityPrice(Guid.NewGuid(), "NO1", hour, 50m));

        Result<DayPricesResponse> result = await Handler().Handle(
            new GetDayPricesQuery(Guid.NewGuid(), _spot.Id, DateTime.Parse("2026-10-01")), CancellationToken.None);

        HourPriceResponse first = result.Value.Items[0];
        Assert.Equal(hour, first.HourStart);
        Assert.NotNull(first.RatePerKwh);
        Assert.True(first.RatePerKwh > 0);
    }

    [Fact]
    public async Task Handle_MissingPrice_GivesNullRate()
    {
        Result<DayPricesResponse> result = await Handler().Handle(
            new GetDayPricesQuery(Guid.NewGuid(), _spot.Id, DateTime.Parse("2026-10-01")), CancellationToken.None);

        Assert.All(result.Value.Items, item => Assert.Null(item.RatePerKwh));
    }

    [Fact]
    public async Task Handle_NorgesPris_HasTheSameFlatRateEveryHour()
    {
        Result<DayPricesResponse> result = await Handler().Handle(
            new GetDayPricesQuery(Guid.NewGuid(), _norgesPris.Id, DateTime.Parse("2026-10-01")), CancellationToken.None);

        Assert.Equal("NorgesPris", result.Value.PricingModel);
        Assert.Single(result.Value.Items.Select(i => i.RatePerKwh).Distinct());
        Assert.All(result.Value.Items, item => Assert.NotNull(item.RatePerKwh));
    }

    [Fact]
    public async Task Handle_NorgesPris_StillReturnsTheSpotRateForEachHour()
    {
        DateTime hour = new(2026, 9, 30, 22, 0, 0, DateTimeKind.Utc); // 2026-10-01 00:00 in Oslo
        _prices.GetByRegionAndHourAsync("NO1", hour, Arg.Any<CancellationToken>())
            .Returns(new ElectricityPrice(Guid.NewGuid(), "NO1", hour, 50m));

        Result<DayPricesResponse> result = await Handler().Handle(
            new GetDayPricesQuery(Guid.NewGuid(), _norgesPris.Id, DateTime.Parse("2026-10-01")), CancellationToken.None);

        HourPriceResponse first = result.Value.Items[0];
        Assert.NotEqual(first.RatePerKwh, first.SpotRatePerKwh); // flat plan rate versus market rate
        Assert.True(first.SpotRatePerKwh > 0);
        Assert.Null(result.Value.Items[1].SpotRatePerKwh); // no price published for the next hour
    }

    [Fact]
    public async Task Handle_Spot_HasTheSameSpotRateAsPlanRate()
    {
        DateTime hour = new(2026, 9, 30, 22, 0, 0, DateTimeKind.Utc);
        _prices.GetByRegionAndHourAsync("NO1", hour, Arg.Any<CancellationToken>())
            .Returns(new ElectricityPrice(Guid.NewGuid(), "NO1", hour, 50m));

        Result<DayPricesResponse> result = await Handler().Handle(
            new GetDayPricesQuery(Guid.NewGuid(), _spot.Id, DateTime.Parse("2026-10-01")), CancellationToken.None);

        Assert.Equal(result.Value.Items[0].RatePerKwh, result.Value.Items[0].SpotRatePerKwh);
    }

    [Fact]
    public async Task Handle_InaccessibleLocation_ReturnsNotFound()
    {
        _locations.IsUserAssociatedAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(false);

        Result<DayPricesResponse> result = await Handler().Handle(
            new GetDayPricesQuery(Guid.NewGuid(), _spot.Id, null), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("Location.NotFound", result.Error.Code);
    }
}
