using Application.Common.Interfaces.Repositories;
using Application.EvChargers;
using Domain.Common;
using Domain.Common.Entities;
using Features.EvChargers.Commands;
using Features.EvChargers.Handlers;
using Features.EvChargers.Queries;
using Features.Tests.NorgesPris;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Features.Tests.EvChargers;

/// <summary>The handlers for users of a location: the sessions list (US2) and the checkbox (US3).</summary>
public class EvChargerHandlerTests
{
    private static readonly Guid UserId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid LocationId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly DateTime NowUtc = new(2026, 11, 20, 10, 0, 0, DateTimeKind.Utc);

    private readonly ILocationRepository<Location> _locations = Substitute.For<ILocationRepository<Location>>();
    private readonly IUserLocationRepository<UserLocation> _links = Substitute.For<IUserLocationRepository<UserLocation>>();
    private readonly IEvChargerRepository<EvCharger> _chargers = Substitute.For<IEvChargerRepository<EvCharger>>();
    private readonly IChargingSessionRepository _sessions = Substitute.For<IChargingSessionRepository>();
    private readonly Location _location = new(LocationId, "Hjemme", "Address", "SN-1", "NO1", true, false);

    public EvChargerHandlerTests()
    {
        _locations.GetByIdAsync(LocationId, Arg.Any<CancellationToken>()).Returns(_location);
        _sessions.GetSessionsAsync(LocationId, Arg.Any<DateTime>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns([]);
    }

    private void Linked(bool owner)
    {
        _locations.IsUserAssociatedAsync(UserId, LocationId, Arg.Any<CancellationToken>()).Returns(true);
        _links.IsOwnerAsync(UserId, LocationId, Arg.Any<CancellationToken>()).Returns(owner);
    }

    private Task<Result<ChargingSessionsResponse>> Sessions(bool isAdmin = false, DateTime? from = null, DateTime? to = null) =>
        new GetChargingSessionsQueryHandler(_locations, _sessions, new FixedTimeProvider("2026-11-20T10:00:00Z"))
            .Handle(new GetChargingSessionsQuery(UserId, isAdmin, LocationId, from, to), default);

    private Task<Result<EvChargerIncludeResponse>> Include(bool include, bool isAdmin = false) =>
        new SetIncludeEvChargerCommandHandler(_locations, _links, _chargers, _sessions,
                NullLogger<SetIncludeEvChargerCommandHandler>.Instance)
            .Handle(new SetIncludeEvChargerCommand(UserId, isAdmin, LocationId, include), default);

    private static ChargingSession StoredSession(DateTime endUtc) =>
        new(Guid.NewGuid(), LocationId, Guid.NewGuid(), Guid.NewGuid(), endUtc.AddHours(-3), endUtc, 12.5m, true,
            [(endUtc.AddHours(-3), 12.5m, false)]);

    [Fact]
    public async Task Sessions_ForAStranger_AreNotFound()
    {
        Result<ChargingSessionsResponse> result = await Sessions();

        Assert.Equal("Location.NotFound", result.Error.Code);
        await _sessions.DidNotReceive().GetSessionsAsync(
            Arg.Any<Guid>(), Arg.Any<DateTime>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Sessions_ForAViewer_AreListedAsStored()
    {
        Linked(owner: false);
        DateTime newer = NowUtc.AddDays(-1);
        DateTime older = NowUtc.AddDays(-3);
        _sessions.GetSessionsAsync(LocationId, Arg.Any<DateTime>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns([StoredSession(newer), StoredSession(older)]);

        Result<ChargingSessionsResponse> result = await Sessions();

        Assert.Equal([newer, older], result.Value.Items.Select(i => i.End));
        Assert.Equal(12.5m, result.Value.Items[0].Kwh);
        Assert.False(result.Value.Items[0].Estimated);
        Assert.Equal(DateTimeKind.Utc, result.Value.Items[0].Start.Kind);
    }

    [Fact]
    public async Task Sessions_ByDefault_AreTheLast30Days()
    {
        await Sessions(isAdmin: true);

        await _sessions.Received(1).GetSessionsAsync(LocationId, NowUtc.AddDays(-30), NowUtc, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Sessions_ForAnAdmin_UseTheGivenRange()
    {
        var from = new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc);
        var to = new DateTime(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc);

        await Sessions(isAdmin: true, from, to);

        await _sessions.Received(1).GetSessionsAsync(LocationId, from, to, Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Include_AnOwner_CanTurnItOnAndOff(bool include)
    {
        Linked(owner: true);
        _chargers.GetByLocationAsync(LocationId, Arg.Any<CancellationToken>())
            .Returns(new EvCharger(Guid.NewGuid(), LocationId, Guid.NewGuid(), "C", Guid.NewGuid(), "I"));

        Result<EvChargerIncludeResponse> result = await Include(include);

        Assert.Equal(new EvChargerIncludeResponse(LocationId, include), result.Value);
        Assert.Equal(include, _location.IncludeEvChargerInTotals);
        await _locations.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Include_AnAdminWhoIsNotLinked_CanChangeIt()
    {
        _sessions.AnyForLocationAsync(LocationId, Arg.Any<CancellationToken>()).Returns(true);

        Result<EvChargerIncludeResponse> result = await Include(true, isAdmin: true);

        Assert.True(result.IsSuccess);
        Assert.True(_location.IncludeEvChargerInTotals);
    }

    [Fact]
    public async Task Include_AViewer_IsForbidden()
    {
        Linked(owner: false);

        Result<EvChargerIncludeResponse> result = await Include(true);

        Assert.Equal(EvChargerErrors.Forbidden, result.Error);
        Assert.False(_location.IncludeEvChargerInTotals);
    }

    [Fact]
    public async Task Include_AStranger_GetsNotFound()
    {
        Result<EvChargerIncludeResponse> result = await Include(true);

        Assert.Equal("Location.NotFound", result.Error.Code);
    }

    [Fact]
    public async Task Include_OnWithoutAnyCharger_IsNotConnected()
    {
        Linked(owner: true);

        Result<EvChargerIncludeResponse> result = await Include(true);

        Assert.Equal(EvChargerErrors.NotConnected, result.Error);
        await _locations.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Include_OnWithStoredSessionsButNoConnection_IsAllowed()
    {
        Linked(owner: true);
        _sessions.AnyForLocationAsync(LocationId, Arg.Any<CancellationToken>()).Returns(true);

        Result<EvChargerIncludeResponse> result = await Include(true);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task Include_Off_IsAlwaysAllowed()
    {
        Linked(owner: true);
        _location.SetIncludeEvCharger(true);

        Result<EvChargerIncludeResponse> result = await Include(false);

        Assert.True(result.IsSuccess);
        Assert.False(_location.IncludeEvChargerInTotals);
    }
}
