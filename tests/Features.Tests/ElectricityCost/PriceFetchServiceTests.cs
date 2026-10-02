using Application.Common.Interfaces.External;
using Application.Common.Interfaces.Repositories;
using Domain.Common.Entities;
using Features.ElectricityCost.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Features.Tests.ElectricityCost;

public class PriceFetchServiceTests
{
    private readonly ILocationRepository<Location> _locations = Substitute.For<ILocationRepository<Location>>();
    private readonly IElectricityPriceRepository<ElectricityPrice> _prices =
        Substitute.For<IElectricityPriceRepository<ElectricityPrice>>();
    private readonly IExchangeRateRepository<ExchangeRate> _rates = Substitute.For<IExchangeRateRepository<ExchangeRate>>();
    private readonly ISpotPriceClient _spotClient = Substitute.For<ISpotPriceClient>();
    private readonly IExchangeRateClient _fxClient = Substitute.For<IExchangeRateClient>();

    private PriceFetchService CreateService()
    {
        var services = new ServiceCollection();
        services.AddSingleton(_locations);
        services.AddSingleton(_prices);
        services.AddSingleton(_rates);
        services.AddSingleton(_spotClient);
        services.AddSingleton(_fxClient);
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["PriceFetch:IntervalHours"] = "6" })
            .Build();

        return new PriceFetchService(
            services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(),
            configuration, NullLogger<PriceFetchService>.Instance);
    }

    private async Task RunOneCycleAsync()
    {
        PriceFetchService service = CreateService();
        await service.StartAsync(CancellationToken.None);
        await Task.Delay(300); // first cycle runs immediately on start
        await service.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task Cycle_InsertsOnlyHoursThatAreNotAlreadyStored_EvenWhenFetchedSpanStartsBeforeToday()
    {
        // ENTSO-E returns whole local days, so the first fetched hour is 22:00 UTC the evening before.
        DateTime first = DateTime.UtcNow.Date.AddHours(-2);
        _locations.FindAsync(Arg.Any<System.Linq.Expressions.Expression<Func<Location, bool>>>(), Arg.Any<CancellationToken>(), Arg.Any<bool>())
            .Returns([new Location(Guid.NewGuid(), "Home", "A", "SN", "NO1", true, false)]);
        _spotClient.GetPricesAsync("NO1", Arg.Any<DateTime>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns([new SpotPrice(first, 1m), new SpotPrice(first.AddHours(1), 2m), new SpotPrice(first.AddHours(2), 3m)]);
        // The first two hours were stored by an earlier cycle - they lie before 'today' in UTC.
        _prices.GetByRegionAndHourRangeAsync("NO1", first, first.AddHours(3), Arg.Any<CancellationToken>())
            .Returns([
                new ElectricityPrice(Guid.NewGuid(), "NO1", first, 1m),
                new ElectricityPrice(Guid.NewGuid(), "NO1", first.AddHours(1), 2m)
            ]);

        await RunOneCycleAsync();

        _prices.Received(1).Insert(Arg.Is<ElectricityPrice>(p => p.HourStartUtc == first.AddHours(2)));
        _prices.DidNotReceive().Insert(Arg.Is<ElectricityPrice>(p => p.HourStartUtc == first));
        _prices.DidNotReceive().Insert(Arg.Is<ElectricityPrice>(p => p.HourStartUtc == first.AddHours(1)));
    }

    [Fact]
    public async Task Cycle_FetchesOncePerDistinctZone()
    {
        _locations.FindAsync(Arg.Any<System.Linq.Expressions.Expression<Func<Location, bool>>>(), Arg.Any<CancellationToken>(), Arg.Any<bool>()).Returns([
            new Location(Guid.NewGuid(), "A", "A", "SN1", "NO1", true, false),
            new Location(Guid.NewGuid(), "B", "B", "SN2", "NO1", true, false),
            new Location(Guid.NewGuid(), "C", "C", "SN3", "NO3", true, false)
        ]);
        _spotClient.GetPricesAsync(Arg.Any<string>(), Arg.Any<DateTime>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns([]);

        await RunOneCycleAsync();

        await _spotClient.Received(1).GetPricesAsync("NO1", Arg.Any<DateTime>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>());
        await _spotClient.Received(1).GetPricesAsync("NO3", Arg.Any<DateTime>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Cycle_ExchangeRateAlreadyStored_IsNotFetchedAgain()
    {
        _locations.FindAsync(Arg.Any<System.Linq.Expressions.Expression<Func<Location, bool>>>(), Arg.Any<CancellationToken>(), Arg.Any<bool>()).Returns([]);
        _rates.GetByDateAsync("EURNOK", Arg.Any<DateOnly>(), Arg.Any<CancellationToken>())
            .Returns(new ExchangeRate(Guid.NewGuid(), "EURNOK", DateOnly.FromDateTime(DateTime.UtcNow), 10m));

        await RunOneCycleAsync();

        await _fxClient.DidNotReceive().GetRateAsync(Arg.Any<DateOnly>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Cycle_SpotClientThrows_StillFetchesExchangeRate()
    {
        _locations.FindAsync(Arg.Any<System.Linq.Expressions.Expression<Func<Location, bool>>>(), Arg.Any<CancellationToken>(), Arg.Any<bool>())
            .Returns([new Location(Guid.NewGuid(), "Home", "A", "SN", "NO1", true, false)]);
        _spotClient.GetPricesAsync(Arg.Any<string>(), Arg.Any<DateTime>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns<IReadOnlyList<SpotPrice>>(_ => throw new HttpRequestException("boom"));
        _fxClient.GetRateAsync(Arg.Any<DateOnly>(), Arg.Any<CancellationToken>()).Returns(10m);

        await RunOneCycleAsync();

        _rates.Received(1).Insert(Arg.Any<ExchangeRate>());
    }
}
