using Features.WaterHeaters.Services;

namespace Features.Tests.WaterHeaters;

public class SampleFailureTrackerTests
{
    private static readonly Guid A = Guid.NewGuid();
    private static readonly Guid B = Guid.NewGuid();
    private static readonly DateTime T0 = new(2026, 11, 20, 10, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void TheFirstFailure_IsLogged()
    {
        var tracker = new SampleFailureTracker();

        Assert.Equal(SampleLogKind.LogStart, tracker.Failed(A, T0).Kind);
    }

    [Fact]
    public void FailuresWithinTheHour_AreNotLoggedAgain()
    {
        var tracker = new SampleFailureTracker();
        tracker.Failed(A, T0);

        for (int minute = 1; minute < 60; minute++)
        {
            Assert.Equal(SampleLogKind.None, tracker.Failed(A, T0.AddMinutes(minute)).Kind);
        }
    }

    [Fact]
    public void AFailureAnHourAfterTheLastLog_IsLoggedWithTheMinutesSinceTheStart()
    {
        var tracker = new SampleFailureTracker();
        tracker.Failed(A, T0);

        SampleLogDecision first = tracker.Failed(A, T0.AddMinutes(60));
        SampleLogDecision quiet = tracker.Failed(A, T0.AddMinutes(61));
        SampleLogDecision second = tracker.Failed(A, T0.AddMinutes(120));

        Assert.Equal(SampleLogKind.LogStillFailing, first.Kind);
        Assert.Equal(TimeSpan.FromMinutes(60), first.Duration);
        Assert.Equal(SampleLogKind.None, quiet.Kind);
        Assert.Equal(SampleLogKind.LogStillFailing, second.Kind);
        Assert.Equal(TimeSpan.FromMinutes(120), second.Duration);
    }

    [Fact]
    public void ASuccessAfterFailures_IsLoggedOnceAndResets()
    {
        var tracker = new SampleFailureTracker();
        tracker.Failed(A, T0);
        tracker.Failed(A, T0.AddMinutes(5));

        SampleLogDecision recovered = tracker.Succeeded(A, T0.AddMinutes(7));

        Assert.Equal(SampleLogKind.LogRecovered, recovered.Kind);
        Assert.Equal(TimeSpan.FromMinutes(7), recovered.Duration);
        Assert.Equal(SampleLogKind.None, tracker.Succeeded(A, T0.AddMinutes(8)).Kind);
        Assert.Equal(SampleLogKind.LogStart, tracker.Failed(A, T0.AddMinutes(9)).Kind);
    }

    [Fact]
    public void ASuccessWithoutFailures_IsNotLogged()
    {
        Assert.Equal(SampleLogKind.None, new SampleFailureTracker().Succeeded(A, T0).Kind);
    }

    [Fact]
    public void Locations_AreTrackedSeparately()
    {
        var tracker = new SampleFailureTracker();
        tracker.Failed(A, T0);

        Assert.Equal(SampleLogKind.LogStart, tracker.Failed(B, T0.AddMinutes(1)).Kind);
        Assert.Equal(SampleLogKind.LogRecovered, tracker.Succeeded(B, T0.AddMinutes(2)).Kind);
        Assert.Equal(SampleLogKind.None, tracker.Failed(A, T0.AddMinutes(3)).Kind);
    }
}
