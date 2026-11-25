using Application.Common.Interfaces.External;
using Application.EvChargers;
using Domain.Common;
using Features.EvChargers.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using StateResult = Domain.Common.Result<(Application.EvChargers.ZaptecChargerState Values, System.DateTime ReadAtUtc)>;

namespace Features.Tests.EvChargers;

/// <summary>One Zaptec read per charger, shared 3 minutes or 1 minute after a failure (spec 021, research R3).
/// </summary>
public class ChargerStateCacheTests
{
    private static readonly Guid ChargerId = ZaptecSamples.ChargerId;
    private static readonly Guid LocationId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

    private readonly IEvChargerClient _client = Substitute.For<IEvChargerClient>();
    private readonly ManualTimeProvider _time = new(new DateTimeOffset(2026, 11, 24, 18, 0, 0, TimeSpan.Zero));
    private readonly ChargerStateCache _cache;

    private static readonly ZaptecChargerState Values =
        new(null, new DateTime(2026, 11, 24, 17, 59, 0, DateTimeKind.Utc), null, null, null, null);

    public ChargerStateCacheTests()
    {
        _client.GetStateAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(Result.Success(Values));
        ServiceProvider services = new ServiceCollection().AddSingleton(_client).BuildServiceProvider();
        _cache = new ChargerStateCache(
            services.GetRequiredService<IServiceScopeFactory>(), _time, NullLogger<ChargerStateCache>.Instance);
    }

    private Task<StateResult> Get(Guid? chargerId = null) =>
        _cache.GetAsync(chargerId ?? ChargerId, LocationId, default);

    private int Calls() =>
        _client.ReceivedCalls().Count(c => c.GetMethodInfo().Name == nameof(IEvChargerClient.GetStateAsync));

    [Fact]
    public async Task TwoCallsWithin3Minutes_ReadZaptecOnce()
    {
        StateResult first = await Get();
        _time.Advance(TimeSpan.FromMinutes(2).Add(TimeSpan.FromSeconds(59)));
        StateResult second = await Get();

        Assert.Equal(1, Calls());
        Assert.Equal(first.Value.ReadAtUtc, second.Value.ReadAtUtc);
        Assert.Equal(new DateTime(2026, 11, 24, 18, 0, 0, DateTimeKind.Utc), first.Value.ReadAtUtc);
    }

    [Fact]
    public async Task After3Minutes_ZaptecIsReadAgain()
    {
        await Get();
        _time.Advance(TimeSpan.FromMinutes(3).Add(TimeSpan.FromSeconds(1)));
        StateResult second = await Get();

        Assert.Equal(2, Calls());
        Assert.Equal(new DateTime(2026, 11, 24, 18, 3, 1, DateTimeKind.Utc), second.Value.ReadAtUtc);
    }

    [Fact]
    public async Task AFailure_IsSharedFor1Minute()
    {
        _client.GetStateAsync(ChargerId, Arg.Any<CancellationToken>())
            .Returns(Result.Failure<ZaptecChargerState>(EvChargerErrors.Unavailable));

        Assert.Equal(EvChargerErrors.Unavailable, (await Get()).Error);
        _time.Advance(TimeSpan.FromSeconds(59));
        Assert.Equal(EvChargerErrors.Unavailable, (await Get()).Error);
        Assert.Equal(1, Calls());

        _time.Advance(TimeSpan.FromSeconds(2));
        await Get();
        Assert.Equal(2, Calls());
    }

    [Fact]
    public async Task CallsWhileAReadIsRunning_WaitForThatRead()
    {
        var pending = new TaskCompletionSource<Result<ZaptecChargerState>>();
        _client.GetStateAsync(ChargerId, Arg.Any<CancellationToken>()).Returns(pending.Task);

        Task<StateResult> first = Get();
        Task<StateResult> second = Get();
        Assert.False(first.IsCompleted);
        pending.SetResult(Result.Success(Values));

        Assert.True((await first).IsSuccess);
        Assert.True((await second).IsSuccess);
        Assert.Equal(1, Calls());
    }

    [Fact]
    public async Task ACallerGivingUp_DoesNotCancelTheReadForOthers()
    {
        var pending = new TaskCompletionSource<Result<ZaptecChargerState>>();
        _client.GetStateAsync(ChargerId, Arg.Any<CancellationToken>()).Returns(pending.Task);
        using var gone = new CancellationTokenSource();

        Task<StateResult> leaving = _cache.GetAsync(ChargerId, LocationId, gone.Token);
        Task<StateResult> staying = Get();
        await gone.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => leaving);
        pending.SetResult(Result.Success(Values));

        Assert.True((await staying).IsSuccess);
        await _client.Received(1).GetStateAsync(ChargerId, CancellationToken.None);
    }

    [Fact]
    public async Task AnExceptionInTheRead_IsUnavailable_AndNotKeptAsAFault()
    {
        _client.GetStateAsync(ChargerId, Arg.Any<CancellationToken>())
            .Returns<Task<Result<ZaptecChargerState>>>(_ => throw new InvalidOperationException("boom"));

        Assert.Equal(EvChargerErrors.Unavailable, (await Get()).Error);
    }

    [Fact]
    public async Task TwoChargers_AreReadSeparately()
    {
        await Get();
        await Get(Guid.NewGuid());

        Assert.Equal(2, Calls());
    }

    [Fact]
    public void NobodyAsks_ZaptecIsNotRead()
    {
        _time.Advance(TimeSpan.FromHours(8));

        Assert.Equal(0, Calls());
    }

    private sealed class ManualTimeProvider(DateTimeOffset start) : TimeProvider
    {
        private DateTimeOffset _now = start;
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan by) => _now += by;
    }
}
