using Application.Common.Interfaces.External;
using Application.Common.Interfaces.Repositories;
using Application.EvChargers;
using Domain.Common;
using Domain.Common.Entities;
using Features.EvChargers.Logging;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Features.EvChargers.Services;

/// <summary>
/// Reads the completed charging sessions of every connected charger at an active location, every 15 minutes by default
/// (spec 016, US2, research R3), and stores them with their kWh per hour. Each read covers the time since the last
/// successful one, minus 24 hours for sessions a charger uploads late; the first goes back to the start of the
/// previous Oslo month. A failed read stores and deletes nothing and leaves the watermark, so the next success fills
/// the gap. A failure never stops the other chargers or the host, and is logged at a limited rate.
/// </summary>
public class EvChargerReadService(
    IServiceScopeFactory scopeFactory,
    IConfiguration configuration,
    TimeProvider timeProvider,
    ILogger<EvChargerReadService> logger) : BackgroundService
{
    public static readonly TimeSpan Overlap = TimeSpan.FromHours(24);

    private static readonly TimeZoneInfo Oslo = TimeZoneInfo.FindSystemTimeZoneById("Europe/Oslo");
    private static readonly Error Unexpected = Error.Problem("EvCharger.Unexpected", "Unexpected read failure");

    private readonly ReadFailureTracker _tracker = new();
    private bool _notConfiguredLogged;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        int minutes = Math.Max(1, configuration.GetValue("EvCharger:ReadIntervalMinutes", 15));
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(minutes), timeProvider);

        do
        {
            // Last-resort guard only: an exception escaping a BackgroundService stops the whole API host.
            try
            {
                await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();
                await ReadOnceAsync(scope.ServiceProvider, stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // The exception type only: a message could carry a URL or a response.
                LogMessages.EvChargerReadCycleFailed(logger, ex.GetType().Name);
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    /// <summary>One read cycle over every charger at an active location. Public for tests.</summary>
    public async Task ReadOnceAsync(IServiceProvider services, CancellationToken ct)
    {
        IEvChargerClient client = services.GetRequiredService<IEvChargerClient>();
        if (!client.IsConfigured)
        {
            if (!_notConfiguredLogged)
            {
                LogMessages.EvChargerNotConfigured(logger);
                _notConfiguredLogged = true;
            }

            return;
        }

        IEvChargerRepository<EvCharger> chargers = services.GetRequiredService<IEvChargerRepository<EvCharger>>();
        IChargingSessionRepository sessions = services.GetRequiredService<IChargingSessionRepository>();

        foreach (EvCharger charger in await chargers.GetForActiveLocationsAsync(ct))
        {
            DateTime now = timeProvider.GetUtcNow().UtcDateTime;
            Result<int> stored;
            try
            {
                stored = await ReadChargerAsync(charger, client, sessions, now, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                stored = Result.Failure<int>(Unexpected);
            }

            if (stored.IsSuccess)
            {
                LogMessages.EvChargerSessionsStored(logger, charger.LocationId, stored.Value);
                Log(_tracker.Succeeded(charger.LocationId, now), charger.LocationId, null);
            }
            else
            {
                Log(_tracker.Failed(charger.LocationId, now), charger.LocationId, stored.Error);
            }
        }
    }

    /// <summary>The window a read of this charger covers: [from, now).</summary>
    public static DateTime ReadFrom(EvCharger charger, DateTime nowUtc) =>
        charger.ReadUpToUtc is { } readUpTo ? readUpTo - Overlap : PreviousOsloMonthStartUtc(nowUtc);

    private static async Task<Result<int>> ReadChargerAsync(
        EvCharger charger, IEvChargerClient client, IChargingSessionRepository sessions, DateTime now, CancellationToken ct)
    {
        Result<IReadOnlyList<ZaptecSession>> read =
            await client.GetCompletedSessionsAsync(charger.ChargerId, ReadFrom(charger, now), now, ct);
        if (read.IsFailure)
        {
            return Result.Failure<int>(read.Error);
        }

        var toStore = read.Value
            .Where(s => s.EndUtc >= s.StartUtc)
            .Select(s => new ChargingSessionToStore(
                s, s.Discard ? [] : ChargingEnergySplitter.Split(s.StartUtc, s.EndUtc, s.Kwh, s.Details)))
            .ToList();

        await sessions.UpsertAsync(charger.LocationId, charger.ChargerId, toStore, ct);
        charger.MarkReadUpTo(now);
        // One save for the sessions and the watermark: they share the scope's DbContext.
        await sessions.SaveChangesAsync(ct);

        return Result.Success(toStore.Count(s => !s.Session.Discard));
    }

    private static DateTime PreviousOsloMonthStartUtc(DateTime nowUtc)
    {
        DateTime local = TimeZoneInfo.ConvertTimeFromUtc(nowUtc, Oslo);
        DateTime previous = new DateTime(local.Year, local.Month, 1, 0, 0, 0, DateTimeKind.Unspecified).AddMonths(-1);
        return DateTime.SpecifyKind(TimeZoneInfo.ConvertTimeToUtc(previous, Oslo), DateTimeKind.Utc);
    }

    private void Log(ReadLogDecision decision, Guid locationId, Error? error)
    {
        string reason = error is null ? string.Empty : LogMessages.Reason(error);
        int minutes = (int)decision.Duration.TotalMinutes;
        switch (decision.Kind)
        {
            case ReadLogKind.LogStart:
                LogMessages.EvChargerReadFailing(logger, locationId, reason);
                break;
            case ReadLogKind.LogStillFailing:
                LogMessages.EvChargerReadStillFailing(logger, locationId, reason, minutes);
                break;
            case ReadLogKind.LogRecovered:
                LogMessages.EvChargerReadRecovered(logger, locationId, minutes);
                break;
        }
    }
}
