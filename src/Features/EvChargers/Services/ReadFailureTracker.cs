namespace Features.EvChargers.Services;

public enum ReadLogKind
{
    None,
    LogStart,
    LogStillFailing,
    LogRecovered
}

/// <param name="Kind">What to log, if anything.</param>
/// <param name="Duration">How long the location has been failing (or failed, on recovery).</param>
public readonly record struct ReadLogDecision(ReadLogKind Kind, TimeSpan Duration);

/// <summary>
/// Decides when a charger read failure is logged (spec 016, FR-009): once when failing starts, at most once an hour
/// while it goes on, and once when it recovers. A copy of the water heater's <c>SampleFailureTracker</c> on purpose:
/// slices do not share code (Principle II). Pure apart from its in-memory state; a restart forgets it.
/// </summary>
public sealed class ReadFailureTracker
{
    public static readonly TimeSpan RepeatEvery = TimeSpan.FromHours(1);

    private readonly Dictionary<Guid, (DateTime FailingSince, DateTime LastLoggedUtc)> _failing = [];

    public ReadLogDecision Failed(Guid locationId, DateTime nowUtc)
    {
        if (!_failing.TryGetValue(locationId, out (DateTime FailingSince, DateTime LastLoggedUtc) state))
        {
            _failing[locationId] = (nowUtc, nowUtc);
            return new ReadLogDecision(ReadLogKind.LogStart, TimeSpan.Zero);
        }

        if (nowUtc - state.LastLoggedUtc < RepeatEvery)
        {
            return new ReadLogDecision(ReadLogKind.None, nowUtc - state.FailingSince);
        }

        _failing[locationId] = (state.FailingSince, nowUtc);
        return new ReadLogDecision(ReadLogKind.LogStillFailing, nowUtc - state.FailingSince);
    }

    public ReadLogDecision Succeeded(Guid locationId, DateTime nowUtc)
    {
        if (!_failing.Remove(locationId, out (DateTime FailingSince, DateTime LastLoggedUtc) state))
        {
            return new ReadLogDecision(ReadLogKind.None, TimeSpan.Zero);
        }

        return new ReadLogDecision(ReadLogKind.LogRecovered, nowUtc - state.FailingSince);
    }
}
