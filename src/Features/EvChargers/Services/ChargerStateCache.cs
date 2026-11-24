using Application.Common.Interfaces.External;
using Application.EvChargers;
using Domain.Common;
using Features.EvChargers.Logging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Features.EvChargers.Services;

/// <summary>
/// One Zaptec state read per charger, shared by everyone who asks (spec 021, research R3): kept 3 minutes after a good
/// read and 1 minute after a failed one, so the app can ask as often as it likes without adding calls to Zaptec, and
/// nobody asking means no calls at all. Callers who ask while a read is running wait for that read.
/// - In memory: production is one container, and the answer is lost on restart. Nothing is stored (FR-004).
/// - The shared read never uses a caller's token, so one browser leaving doesn't cancel it for the others; the client
///   gives up after 3 seconds anyway.
/// - The client is a transient typed HttpClient client, so it is resolved per read rather than held by this singleton.
/// </summary>
public sealed class ChargerStateCache(
    IServiceScopeFactory scopes,
    TimeProvider timeProvider,
    ILogger<ChargerStateCache> logger)
{
    public static readonly TimeSpan GoodFor = TimeSpan.FromMinutes(3);
    public static readonly TimeSpan FailedFor = TimeSpan.FromMinutes(1);

    private readonly object _lock = new();
    private readonly Dictionary<Guid, Entry> _entries = [];

    /// <summary>The charger's values and when Zaptec was read, or the (shared) failure.</summary>
    public async Task<Result<(ZaptecChargerState Values, DateTime ReadAtUtc)>> GetAsync(
        Guid chargerId, Guid locationId, CancellationToken ct)
    {
        Entry entry;
        lock (_lock)
        {
            if (!_entries.TryGetValue(chargerId, out Entry? found)
                || found.Read.IsCompleted && timeProvider.GetUtcNow().UtcDateTime >= found.ExpiresAtUtc)
            {
                found = new Entry();
                Entry started = found;
                // Task.Run keeps the client's synchronous start outside the lock.
                found.Read = Task.Run(() => ReadAsync(started, chargerId, locationId), CancellationToken.None);
                _entries[chargerId] = found;
            }

            entry = found;
        }

        Result<ZaptecChargerState> result = await entry.Read.WaitAsync(ct);
        return result.IsFailure
            ? Result.Failure<(ZaptecChargerState, DateTime)>(result.Error)
            : Result.Success((result.Value, entry.ReadAtUtc));
    }

    private async Task<Result<ZaptecChargerState>> ReadAsync(Entry entry, Guid chargerId, Guid locationId)
    {
        long started = timeProvider.GetTimestamp();
        Result<ZaptecChargerState> result;
        try
        {
            await using AsyncServiceScope scope = scopes.CreateAsyncScope();
            IEvChargerClient client = scope.ServiceProvider.GetRequiredService<IEvChargerClient>();
            result = await client.GetStateAsync(chargerId, CancellationToken.None);
        }
        catch (Exception)
        {
            // Last resort: the client returns results for every failure it knows. A faulted task is never kept.
            result = Result.Failure<ZaptecChargerState>(EvChargerErrors.Unavailable);
        }

        DateTime now = timeProvider.GetUtcNow().UtcDateTime;
        entry.ReadAtUtc = now;
        entry.ExpiresAtUtc = now + (result.IsSuccess ? GoodFor : FailedFor);
        if (result.IsSuccess)
        {
            long elapsedMs = (long)timeProvider.GetElapsedTime(started).TotalMilliseconds;
            LogMessages.EvChargerStateRead(logger, locationId, elapsedMs);
        }
        else
        {
            LogMessages.EvChargerStateFailed(logger, locationId, LogMessages.Reason(result.Error));
        }

        return result;
    }

    /// <summary>Its fields are set before <see cref="Read"/> completes, so a caller that awaited it can read them.
    /// </summary>
    private sealed class Entry
    {
        public Task<Result<ZaptecChargerState>> Read { get; set; } = null!;
        public DateTime ReadAtUtc { get; set; }
        public DateTime ExpiresAtUtc { get; set; }
    }
}
