using Application.Common.Interfaces.External;
using Application.Common.Interfaces.Repositories;
using Application.Common.Interfaces.Security;
using Application.WaterHeaters;
using Domain.Common;
using Domain.Common.Entities;
using Features.WaterHeaters.Logging;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Features.WaterHeaters.Services;

/// <summary>
/// Samples the power of every connected water heater at an active location about once a minute (spec 011, US3).
/// OSO has no history, so these samples are the only record of the heater's use. A failed sample stores nothing (a
/// gap, never 0 kW), never stops the other heaters or the host, and is logged at a limited rate.
/// </summary>
public class WaterHeaterSamplingService(
    IServiceScopeFactory scopeFactory,
    IConfiguration configuration,
    TimeProvider timeProvider,
    ILogger<WaterHeaterSamplingService> logger) : BackgroundService
{
    private static readonly Error Unexpected = Error.Problem("WaterHeater.Unexpected", "Unexpected sampling failure");

    private readonly SampleFailureTracker _tracker = new();

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        int seconds = Math.Max(1, configuration.GetValue("WaterHeater:SampleIntervalSeconds", 60));
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(seconds), timeProvider);

        do
        {
            // Last-resort guard only: an exception escaping a BackgroundService stops the whole API host.
            try
            {
                await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();
                await SampleOnceAsync(scope.ServiceProvider, stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // The exception type only: a message could carry a URL with the device id.
                LogMessages.WaterHeaterSamplingCycleFailed(logger, ex.GetType().Name);
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    /// <summary>One sampling cycle over every heater at an active location. Public for tests.</summary>
    public async Task SampleOnceAsync(IServiceProvider services, CancellationToken ct)
    {
        IWaterHeaterRepository<WaterHeater> heaters =
            services.GetRequiredService<IWaterHeaterRepository<WaterHeater>>();
        IWaterHeaterPowerSampleRepository<WaterHeaterPowerSample> samples =
            services.GetRequiredService<IWaterHeaterPowerSampleRepository<WaterHeaterPowerSample>>();
        IWaterHeaterClient client = services.GetRequiredService<IWaterHeaterClient>();
        ISecretProtector protector = services.GetRequiredService<ISecretProtector>();

        int inserted = 0;
        foreach (WaterHeater heater in await heaters.GetForActiveLocationsAsync(ct))
        {
            Result<decimal> power;
            try
            {
                power = await ReadPowerAsync(heater, client, protector, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                power = Result.Failure<decimal>(Unexpected);
            }

            DateTime now = TruncateToSecond(timeProvider.GetUtcNow().UtcDateTime);
            if (power.IsSuccess)
            {
                samples.Insert(new WaterHeaterPowerSample(Guid.NewGuid(), heater.LocationId, now, power.Value));
                inserted++;
                Log(_tracker.Succeeded(heater.LocationId, now), heater.LocationId, null);
            }
            else
            {
                Log(_tracker.Failed(heater.LocationId, now), heater.LocationId, power.Error);
            }
        }

        if (inserted > 0)
        {
            await samples.SaveChangesAsync(ct);
        }
    }

    private static async Task<Result<decimal>> ReadPowerAsync(
        WaterHeater heater, IWaterHeaterClient client, ISecretProtector protector, CancellationToken ct)
    {
        Result<string> key = protector.Unprotect(heater.ProtectedSubscriptionKey, heater.LocationId);
        if (key.IsFailure)
        {
            return Result.Failure<decimal>(key.Error);
        }

        return await client.GetActualLoadAsync(new WaterHeaterCredentials(heater.LocationId, heater.DeviceId, key.Value), ct);
    }

    private void Log(SampleLogDecision decision, Guid locationId, Error? error)
    {
        string reason = error is null ? string.Empty : LogMessages.Reason(error);
        int minutes = (int)decision.Duration.TotalMinutes;
        switch (decision.Kind)
        {
            case SampleLogKind.LogStart:
                LogMessages.WaterHeaterSampleFailing(logger, locationId, reason);
                break;
            case SampleLogKind.LogStillFailing:
                LogMessages.WaterHeaterSampleStillFailing(logger, locationId, reason, minutes);
                break;
            case SampleLogKind.LogRecovered:
                LogMessages.WaterHeaterSampleRecovered(logger, locationId, minutes);
                break;
        }
    }

    private static DateTime TruncateToSecond(DateTime utc) =>
        new(utc.Ticks - utc.Ticks % TimeSpan.TicksPerSecond, DateTimeKind.Utc);
}
