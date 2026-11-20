using Features.EvChargers.Services;

namespace Features.Tests.EvChargers;

public class ReadFailureTrackerTests
{
    private static readonly Guid A = Guid.NewGuid();
    private static readonly Guid B = Guid.NewGuid();
    private static readonly DateTime T0 = new(2026, 11, 20, 10, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void TheFirstFailure_IsLogged() => Assert.Equal(ReadLogKind.LogStart, new ReadFailureTracker().Failed(A, T0).Kind);

    [Fact]
    public void FailuresWithinTheHour_AreNotLoggedAgain()
    {
        var tracker = new ReadFailureTracker();
        tracker.Failed(A, T0);

        for (int minute = 15; minute < 60; minute += 15)
        {
            Assert.Equal(ReadLogKind.None, tracker.Failed(A, T0.AddMinutes(minute)).Kind);
        }
    }

    [Fact]
    public void AFailureAnHourAfterTheLastLog_IsLoggedWithTheMinutesSinceTheStart()
    {
        var tracker = new ReadFailureTracker();
        tracker.Failed(A, T0);

        ReadLogDecision again = tracker.Failed(A, T0.AddMinutes(60));

        Assert.Equal(ReadLogKind.LogStillFailing, again.Kind);
        Assert.Equal(TimeSpan.FromMinutes(60), again.Duration);
        Assert.Equal(ReadLogKind.None, tracker.Failed(A, T0.AddMinutes(75)).Kind);
    }

    [Fact]
    public void ASuccessAfterFailures_IsLoggedOnceAndResets()
    {
        var tracker = new ReadFailureTracker();
        tracker.Failed(A, T0);

        ReadLogDecision recovered = tracker.Succeeded(A, T0.AddMinutes(30));

        Assert.Equal(ReadLogKind.LogRecovered, recovered.Kind);
        Assert.Equal(TimeSpan.FromMinutes(30), recovered.Duration);
        Assert.Equal(ReadLogKind.None, tracker.Succeeded(A, T0.AddMinutes(45)).Kind);
        Assert.Equal(ReadLogKind.LogStart, tracker.Failed(A, T0.AddMinutes(60)).Kind);
    }

    [Fact]
    public void Locations_AreTrackedSeparately()
    {
        var tracker = new ReadFailureTracker();
        tracker.Failed(A, T0);

        Assert.Equal(ReadLogKind.LogStart, tracker.Failed(B, T0.AddMinutes(15)).Kind);
        Assert.Equal(ReadLogKind.None, tracker.Succeeded(Guid.NewGuid(), T0).Kind);
    }
}
