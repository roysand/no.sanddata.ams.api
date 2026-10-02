using Application.Common.Interfaces.External;
using Application.Common.Interfaces.Repositories;
using Domain.Common.Entities;
using Features.ElectricityCost.Logging;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Features.ElectricityCost.Services;

/// <summary>
/// Periodically fetches day-ahead spot prices (per distinct Location.Zone) and the EUR/NOK rate.
/// Idempotent: rows that already exist are skipped, so every cycle can safely re-fetch today and tomorrow.
/// </summary>
public class PriceFetchService(
    IServiceScopeFactory scopeFactory,
    IConfiguration configuration,
    ILogger<PriceFetchService> logger) : BackgroundService
{
    private const string EurNok = "EURNOK";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        double hours = configuration.GetValue("PriceFetch:IntervalHours", 6.0);
        using var timer = new PeriodicTimer(TimeSpan.FromHours(hours));

        do
        {
            try
            {
                await FetchAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                LogMessages.PriceFetchFailed(logger, "Unexpected", ex);
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task FetchAsync(CancellationToken ct)
    {
        await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();
        IServiceProvider sp = scope.ServiceProvider;
        var locations = sp.GetRequiredService<ILocationRepository<Location>>();
        var prices = sp.GetRequiredService<IElectricityPriceRepository<ElectricityPrice>>();
        var rates = sp.GetRequiredService<IExchangeRateRepository<ExchangeRate>>();
        var spotClient = sp.GetRequiredService<ISpotPriceClient>();
        var fxClient = sp.GetRequiredService<IExchangeRateClient>();

        DateTime today = DateTime.UtcNow.Date;
        DateTime to = today.AddDays(2);

        IEnumerable<string> zones = (await locations.AllAsync(ct))
            .Where(l => l is not null)
            .Select(l => l!.Zone)
            .Distinct();

        foreach (string zone in zones)
        {
            try
            {
                IReadOnlyList<SpotPrice> fetched = await spotClient.GetPricesAsync(zone, today, to, ct);
                IReadOnlyList<ElectricityPrice> existing = await prices.GetByRegionAndHourRangeAsync(zone, today, to, ct);
                var known = existing.Select(p => p.HourStartUtc).ToHashSet();

                int added = 0;
                foreach (SpotPrice price in fetched.Where(p => !known.Contains(p.HourStartUtc)))
                {
                    prices.Insert(new ElectricityPrice(Guid.NewGuid(), zone, price.HourStartUtc, price.PriceEurPerMwh));
                    added++;
                }

                if (added > 0)
                {
                    await prices.SaveChangesAsync(ct);
                    LogMessages.SpotPricesStored(logger, zone, added);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                LogMessages.PriceFetchFailed(logger, "SpotPrices", ex);
            }
        }

        var date = DateOnly.FromDateTime(today);
        try
        {
            if (await rates.GetByDateAsync(EurNok, date, ct) is null
                && await fxClient.GetRateAsync(date, ct) is { } rate)
            {
                rates.Insert(new ExchangeRate(Guid.NewGuid(), EurNok, date, rate));
                await rates.SaveChangesAsync(ct);
                LogMessages.ExchangeRateStored(logger, date);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogMessages.PriceFetchFailed(logger, "ExchangeRate", ex);
        }
    }
}
