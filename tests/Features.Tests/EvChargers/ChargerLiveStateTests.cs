using Application.EvChargers;

namespace Features.Tests.EvChargers;

/// <summary>The rules from Zaptec's values to the answer (spec 021, data-model.md "Rules").</summary>
public class ChargerLiveStateTests
{
    private static readonly DateTime Now = new(2026, 11, 24, 18, 34, 0, DateTimeKind.Utc);
    private static readonly DateTime ReadAt = Now.AddSeconds(-30);
    private static readonly DateTime PluggedIn = new(2026, 11, 24, 14, 52, 3, DateTimeKind.Utc);
    private static readonly DateTime ModeChanged = new(2026, 11, 24, 15, 37, 41, DateTimeKind.Utc);

    private static ZaptecChargerState Values(
        int? mode = 3,
        bool? online = true,
        TimeSpan? checkInAgo = null,
        decimal? powerW = 4414.704m,
        decimal? sessionKwh = 12.9624m,
        DateTime? sessionKwhAt = null,
        DateTime? sessionSet = null) =>
        new(
            online is { } o ? new ZaptecStateValue<bool>(o, Now.AddHours(-4)) : null,
            Now - (checkInAgo ?? TimeSpan.FromMinutes(1)),
            powerW is { } p ? new ZaptecStateValue<decimal>(p, Now.AddMinutes(-40)) : null,
            sessionKwh is { } k ? new ZaptecStateValue<decimal>(k, sessionKwhAt ?? Now.AddMinutes(-2)) : null,
            mode is { } m ? new ZaptecStateValue<int>(m, ModeChanged) : null,
            sessionSet ?? PluggedIn);

    private static ChargerLiveState From(ZaptecChargerState values) => ChargerLiveState.From(values, ReadAt, Now);

    [Theory]
    [InlineData(1, ChargerLiveState.States.NoCar)]
    [InlineData(2, ChargerLiveState.States.Connected)]
    [InlineData(3, ChargerLiveState.States.Charging)]
    [InlineData(5, ChargerLiveState.States.Finished)]
    [InlineData(4, ChargerLiveState.States.Unknown)]
    [InlineData(null, ChargerLiveState.States.Unknown)]
    public void TheModeGivesTheState(int? mode, string expected)
    {
        ChargerLiveState state = From(Values(mode));

        Assert.Equal(expected, state.State);
        Assert.True(state.Online);
        Assert.Equal(ReadAt, state.ReadAtUtc);
        Assert.Equal(Now.AddMinutes(-1), state.LastReportedAtUtc);
    }

    [Fact]
    public void Charging_HasEverything()
    {
        ChargerLiveState state = From(Values());

        Assert.Equal(4.41m, state.PowerKw);
        Assert.Equal(12.962m, state.SessionKwh);
        Assert.Equal(PluggedIn, state.PluggedInAtUtc);
        Assert.Equal(ModeChanged, state.ChargingSinceUtc);
    }

    [Fact]
    public void TheOnlineFlagAt0_IsOffline()
    {
        ChargerLiveState state = From(Values(online: false));

        AssertOffline(state, Now.AddMinutes(-1));
    }

    [Fact]
    public void ACheckInOlderThan10Minutes_IsOfflineEvenIfTheFlagSays1()
    {
        ChargerLiveState state = From(Values(checkInAgo: TimeSpan.FromMinutes(10).Add(TimeSpan.FromSeconds(1))));

        AssertOffline(state, Now - TimeSpan.FromMinutes(10).Add(TimeSpan.FromSeconds(1)));
    }

    [Fact]
    public void ACheckInExactly10MinutesOld_IsStillOnline()
    {
        ChargerLiveState state = From(Values(checkInAgo: TimeSpan.FromMinutes(10)));

        Assert.Equal(ChargerLiveState.States.Charging, state.State);
    }

    [Fact]
    public void NoCheckIn_IsOffline()
    {
        ChargerLiveState state = From(Values() with { LastCheckInUtc = null });

        AssertOffline(state, null);
    }

    [Fact]
    public void NoOnlineFlag_WithARecentCheckIn_IsOnline()
    {
        ChargerLiveState state = From(Values(online: null));

        Assert.True(state.Online);
        Assert.Equal(ChargerLiveState.States.Charging, state.State);
    }

    [Theory]
    [InlineData(2)]
    [InlineData(5)]
    [InlineData(4)]
    public void NotCharging_HasNoPower_EvenIfZaptecStillReportsIt(int mode)
    {
        ChargerLiveState state = From(Values(mode));

        Assert.Null(state.PowerKw);
        Assert.Null(state.ChargingSinceUtc);
        Assert.Equal(12.962m, state.SessionKwh);
    }

    [Theory]
    [InlineData(2)]
    [InlineData(5)]
    public void ACar_HasWhenItWasPluggedIn(int mode) => Assert.Equal(PluggedIn, From(Values(mode)).PluggedInAtUtc);

    [Fact]
    public void Unknown_HasNoPluggedInTime() => Assert.Null(From(Values(4)).PluggedInAtUtc);

    [Fact]
    public void NoCar_HasNoFigures()
    {
        ChargerLiveState state = From(Values(1));

        Assert.Null(state.PowerKw);
        Assert.Null(state.SessionKwh);
        Assert.Null(state.PluggedInAtUtc);
        Assert.Null(state.ChargingSinceUtc);
    }

    [Fact]
    public void AKwhValueOlderThanTheSession_BelongsToTheLastCharge_AndIs0()
    {
        ChargerLiveState state = From(Values(2, sessionKwhAt: PluggedIn.AddMinutes(-1)));

        Assert.Equal(0m, state.SessionKwh);
    }

    [Fact]
    public void NoKwhValue_IsNull() => Assert.Null(From(Values(2, sessionKwh: null)).SessionKwh);

    [Fact]
    public void ChargingWithNoPowerValue_HasNoPower() => Assert.Null(From(Values(powerW: null)).PowerKw);

    [Fact]
    public void PowerAndKwhAreRounded()
    {
        ChargerLiveState state = From(Values(powerW: 4415m, sessionKwh: 0.0005m));

        Assert.Equal(4.42m, state.PowerKw);
        Assert.Equal(0.001m, state.SessionKwh);
    }

    [Fact]
    public void NoCharger_HasOnlyItsState()
    {
        var state = ChargerLiveState.NoCharger();

        Assert.Equal(ChargerLiveState.States.NoCharger, state.State);
        Assert.Equal(new ChargerLiveState(ChargerLiveState.States.NoCharger, null, null, null, null, null, null, null),
            state);
    }

    private static void AssertOffline(ChargerLiveState state, DateTime? lastReported)
    {
        var expected = new ChargerLiveState(
            ChargerLiveState.States.Offline, false, null, null, null, null, lastReported, ReadAt);
        Assert.Equal(expected, state);
    }
}
