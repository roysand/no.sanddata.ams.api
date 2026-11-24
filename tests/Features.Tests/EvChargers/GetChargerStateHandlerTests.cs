using Application.Common.Interfaces.External;
using Application.Common.Interfaces.Repositories;
using Application.EvChargers;
using Domain.Common;
using Domain.Common.Entities;
using Features.EvChargers.Handlers;
using Features.EvChargers.Queries;
using Features.EvChargers.Services;
using Features.Tests.NorgesPris;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Features.Tests.EvChargers;

/// <summary>The charger's state right now (spec 021).</summary>
public class GetChargerStateHandlerTests
{
    private static readonly Guid UserId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid LocationId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly DateTime NowUtc = new(2026, 11, 24, 18, 34, 0, DateTimeKind.Utc);

    private readonly ILocationRepository<Location> _locations = Substitute.For<ILocationRepository<Location>>();
    private readonly IEvChargerRepository<EvCharger> _chargers = Substitute.For<IEvChargerRepository<EvCharger>>();
    private readonly IChargingSessionRepository _sessions = Substitute.For<IChargingSessionRepository>();
    private readonly IEvChargerClient _client = Substitute.For<IEvChargerClient>();
    private readonly Location _location = new(LocationId, "Hjemme", "Address", "SN-1", "NO1", true, false);
    private readonly EvCharger _charger =
        new(Guid.NewGuid(), LocationId, ZaptecSamples.ChargerId, "Lader", Guid.NewGuid(), "Hjemme");
    private readonly FixedTimeProvider _time = new("2026-11-24T18:34:00Z");
    private ChargerStateCache? _cache;

    public GetChargerStateHandlerTests()
    {
        _locations.GetByIdAsync(LocationId, Arg.Any<CancellationToken>()).Returns(_location);
        _chargers.GetByLocationAsync(LocationId, Arg.Any<CancellationToken>()).Returns(_charger);
        _client.IsConfigured.Returns(true);
        _client.GetStateAsync(ZaptecSamples.ChargerId, Arg.Any<CancellationToken>())
            .Returns(Result.Success(Charging()));
    }

    private static ZaptecChargerState Charging() =>
        new(
            new ZaptecStateValue<bool>(true, NowUtc.AddHours(-4)),
            NowUtc.AddMinutes(-1),
            new ZaptecStateValue<decimal>(4414.704m, NowUtc.AddMinutes(-40)),
            new ZaptecStateValue<decimal>(12.962m, NowUtc.AddMinutes(-2)),
            new ZaptecStateValue<int>(3, new DateTime(2026, 11, 24, 15, 37, 41, DateTimeKind.Utc)),
            new DateTime(2026, 11, 24, 14, 52, 3, DateTimeKind.Utc));

    private void Linked() =>
        _locations.IsUserAssociatedAsync(UserId, LocationId, Arg.Any<CancellationToken>()).Returns(true);

    // One cache per test, over the substituted client, registered as the real one resolves it.
    private ChargerStateCache Cache() => _cache ??= new ChargerStateCache(
        new ServiceCollection().AddSingleton(_client).BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(),
        _time,
        NullLogger<ChargerStateCache>.Instance);

    private GetChargerStateQueryHandler Handler() => new(_locations, _chargers, _client, Cache(), _time);

    private Task<Result<ChargerStateResponse>> State(bool isAdmin = false) =>
        Handler().Handle(new GetChargerStateQuery(UserId, isAdmin, LocationId), default);

    [Fact]
    public async Task ALinkedUser_GetsTheStateWhileCharging()
    {
        Linked();

        Result<ChargerStateResponse> result = await State();

        Assert.True(result.IsSuccess);
        Assert.Equal("charging", result.Value.State);
        Assert.Equal(4.41m, result.Value.PowerKw);
        Assert.Equal(12.962m, result.Value.SessionKwh);
        Assert.NotNull(result.Value.ReadAt);
        Assert.Equal(DateTimeKind.Utc, result.Value.ReadAt!.Value.Kind);
        Assert.Equal(DateTimeKind.Utc, result.Value.ChargingSince!.Value.Kind);
    }

    [Fact]
    public async Task AZaptecFailure_IsTheError()
    {
        Linked();
        _client.GetStateAsync(ZaptecSamples.ChargerId, Arg.Any<CancellationToken>())
            .Returns(Result.Failure<ZaptecChargerState>(EvChargerErrors.Unavailable));

        Result<ChargerStateResponse> result = await State();

        Assert.Equal(EvChargerErrors.Unavailable, result.Error);
    }

    [Fact]
    public async Task TwoRequests_ReadZaptecOnce()
    {
        Linked();

        Result<ChargerStateResponse> first = await State();
        Result<ChargerStateResponse> second = await State(isAdmin: true);

        Assert.Equal(first.Value.ReadAt, second.Value.ReadAt);
        await _client.Received(1).GetStateAsync(ZaptecSamples.ChargerId, Arg.Any<CancellationToken>());
    }

    // US3: only people who may see the location.

    [Fact]
    public async Task AStranger_IsNotFound_AndNothingIsRead()
    {
        Result<ChargerStateResponse> result = await State();

        Assert.Equal("Location.NotFound", result.Error.Code);
        await _chargers.DidNotReceive().GetByLocationAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
        await _client.DidNotReceive().GetStateAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
        Assert.Empty(_sessions.ReceivedCalls());
    }

    [Fact]
    public async Task AnAdminNotLinked_GetsTheState()
    {
        Result<ChargerStateResponse> result = await State(isAdmin: true);

        Assert.True(result.IsSuccess);
        Assert.Equal("charging", result.Value.State);
        Assert.Empty(_sessions.ReceivedCalls());
    }

    [Fact]
    public async Task AnAdmin_ForNoSuchLocation_IsNotFound()
    {
        _locations.GetByIdAsync(LocationId, Arg.Any<CancellationToken>()).Returns((Location?)null);

        Result<ChargerStateResponse> result = await State(isAdmin: true);

        Assert.Equal("Location.NotFound", result.Error.Code);
        await _client.DidNotReceive().GetStateAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task NoCharger_IsNoCharger_WithoutAskingZaptec()
    {
        Linked();
        _chargers.GetByLocationAsync(LocationId, Arg.Any<CancellationToken>()).Returns((EvCharger?)null);

        Result<ChargerStateResponse> result = await State();

        Assert.True(result.IsSuccess);
        Assert.Equal(new ChargerStateResponse("noCharger", null, null, null, null, null, null, null), result.Value);
        await _client.DidNotReceive().GetStateAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
        Assert.Empty(_sessions.ReceivedCalls());
    }

    [Fact]
    public async Task NoZaptecLogin_IsNotConfigured_WithoutAskingZaptec()
    {
        Linked();
        _client.IsConfigured.Returns(false);

        Result<ChargerStateResponse> result = await State();

        Assert.Equal(EvChargerErrors.NotConfigured, result.Error);
        await _client.DidNotReceive().GetStateAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
        Assert.Empty(_sessions.ReceivedCalls());
    }
}
