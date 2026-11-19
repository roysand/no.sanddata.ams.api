namespace Features.WaterHeaters.Services;

public enum SampleLogKind
{
    None,
    LogStart,
    LogStillFailing,
    LogRecovered
}

/// <param name="Kind">What to log, if anything.</param>
/// <param name="Duration">How long the location has been failing (or failed, on recovery).</param>
public readonly record struct SampleLogDecision(SampleLogKind Kind, TimeSpan Duration);

/// <summary>
/// Decides when a sampling failure is logged (spec 011, FR-014): once when failing starts, at most once an hour while
/// it goes on, and once when it recovers. Pure apart from its in-memory state; a restart forgets it, so the next
/// failure is logged again. Used by one sampler at a time.
/// </summary>
public sealed class SampleFailureTracker
{
    public static readonly TimeSpan RepeatEvery = TimeSpan.FromHours(1);

    private readonly Dictionary<Guid, (DateTime FailingSince, DateTime LastLoggedUtc)> _failing = [];

    public SampleLogDecision Failed(Guid locationId, DateTime nowUtc)
    {
        if (!_failing.TryGetValue(locationId, out (DateTime FailingSince, DateTime LastLoggedUtc) state))
        {
            _failing[locationId] = (nowUtc, nowUtc);
            return new SampleLogDecision(SampleLogKind.LogStart, TimeSpan.Zero);
        }

        if (nowUtc - state.LastLoggedUtc < RepeatEvery)
        {
            return new SampleLogDecision(SampleLogKind.None, nowUtc - state.FailingSince);
        }

        _failing[locationId] = (state.FailingSince, nowUtc);
        return new SampleLogDecision(SampleLogKind.LogStillFailing, nowUtc - state.FailingSince);
    }

    public SampleLogDecision Succeeded(Guid locationId, DateTime nowUtc)
    {
        if (!_failing.Remove(locationId, out (DateTime FailingSince, DateTime LastLoggedUtc) state))
        {
            return new SampleLogDecision(SampleLogKind.None, TimeSpan.Zero);
        }

        return new SampleLogDecision(SampleLogKind.LogRecovered, nowUtc - state.FailingSince);
    }
}
