using Application.Common.Interfaces.External;
using Application.Common.Interfaces.Repositories;
using Domain.Common;
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
/// Expected failures are returned as <see cref="Result"/> values and logged; they never escape a cycle.
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
            // Last-resort guard only: an unhandled exception in a BackgroundService stops the whole API host.
            // Expected failures (HTTP, parsing, missing data) are Results and never reach this catch.
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

        IEnumerable<string> zones = (await locations.FindAsync(_ => true, ct, noTrack: true))
            .Where(l => l is not null)
            .Select(l => l!.Zone)
            .Distinct();

        foreach (string zone in zones)
        {
            Result<int> stored = await StoreSpotPricesAsync(zone, today, to, spotClient, prices, ct);
            LogIfFailed(stored);
        }

        Result exchangeRate = await StoreExchangeRateAsync(DateOnly.FromDateTime(today), rates, fxClient, ct);
        LogIfFailed(exchangeRate);
    }

    private async Task<Result<int>> StoreSpotPricesAsync(
        string zone,
        DateTime from,
        DateTime to,
        ISpotPriceClient spotClient,
        IElectricityPriceRepository<ElectricityPrice> prices,
        CancellationToken ct)
    {
        Result<IReadOnlyList<SpotPrice>> fetched = await spotClient.GetPricesAsync(zone, from, to, ct);
        if (fetched.IsFailure)
        {
            return Result.Failure<int>(fetched.Error);
        }

        if (fetched.Value.Count == 0)
        {
            return Result.Success(0);
        }

        // ENTSO-E returns whole local (CET/CEST) days, so the fetched hours start before 'today'
        // in UTC - look up existing rows over the fetched span, not the requested one.
        DateTime fetchedFrom = fetched.Value.Min(p => p.HourStartUtc);
        DateTime fetchedTo = fetched.Value.Max(p => p.HourStartUtc).AddHours(1);
        IReadOnlyList<ElectricityPrice> existing =
            await prices.GetByRegionAndHourRangeAsync(zone, fetchedFrom, fetchedTo, ct);
        var known = existing.Select(p => p.HourStartUtc).ToHashSet();

        int added = 0;
        foreach (SpotPrice price in fetched.Value.Where(p => !known.Contains(p.HourStartUtc)))
        {
            prices.Insert(new ElectricityPrice(Guid.NewGuid(), zone, price.HourStartUtc, price.PriceEurPerMwh));
            added++;
        }

        if (added > 0)
        {
            await prices.SaveChangesAsync(ct);
            LogMessages.SpotPricesStored(logger, zone, added);
        }

        return Result.Success(added);
    }

    private async Task<Result> StoreExchangeRateAsync(
        DateOnly date,
        IExchangeRateRepository<ExchangeRate> rates,
        IExchangeRateClient fxClient,
        CancellationToken ct)
    {
        if (await rates.GetByDateAsync(EurNok, date, ct) is not null)
        {
            return Result.Success();
        }

        Result<decimal> fetched = await fxClient.GetRateAsync(date, ct);
        if (fetched.IsFailure)
        {
            return Result.Failure(fetched.Error);
        }

        rates.Insert(new ExchangeRate(Guid.NewGuid(), EurNok, date, fetched.Value));
        await rates.SaveChangesAsync(ct);
        LogMessages.ExchangeRateStored(logger, date);
        return Result.Success();
    }

    // NotFound means "not published / no data yet" - expected on a normal cycle, and the client already logged it.
    private void LogIfFailed(Result result)
    {
        if (result.IsFailure && result.Error.Type != ErrorType.NotFound)
        {
            LogMessages.PriceFetchFailed(logger, result.Error.Code, null);
        }
    }
}
