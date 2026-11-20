using Application.Common.Interfaces.External;
using Application.Common.Interfaces.Repositories;
using Application.EvChargers;
using Domain.Common;
using Domain.Common.Entities;
using Features.EvChargers.Services;
using Features.Tests.NorgesPris;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Features.Tests.EvChargers;

public class EvChargerReadServiceTests
{
    private const string Now = "2026-11-20T10:00:05Z";
    private static readonly DateTime NowUtc = new(2026, 11, 20, 10, 0, 5, DateTimeKind.Utc);

    private readonly IEvChargerRepository<EvCharger> _chargers = Substitute.For<IEvChargerRepository<EvCharger>>();
    private readonly IChargingSessionRepository _sessions = Substitute.For<IChargingSessionRepository>();
    private readonly IEvChargerClient _client = Substitute.For<IEvChargerClient>();
    private readonly List<IReadOnlyList<ChargingSessionToStore>> _upserts = [];

    private readonly EvCharger _first = Charger(Guid.NewGuid(), Guid.NewGuid());
    private readonly EvCharger _second = Charger(Guid.NewGuid(), Guid.NewGuid());

    private static readonly ZaptecSession Session = new(
        Guid.NewGuid(), new DateTime(2026, 11, 19, 19, 0, 0, DateTimeKind.Utc),
        new DateTime(2026, 11, 19, 21, 0, 0, DateTimeKind.Utc), 8m,
        [new EnergyPoint(new DateTime(2026, 11, 19, 20, 0, 0, DateTimeKind.Utc), 4m),
            new EnergyPoint(new DateTime(2026, 11, 19, 21, 0, 0, DateTimeKind.Utc), 4m)],
        Discard: false);

    private static readonly ZaptecSession Voided = Session with { Id = Guid.NewGuid(), Discard = true };

    public EvChargerReadServiceTests()
    {
        _client.IsConfigured.Returns(true);
        _client.GetCompletedSessionsAsync(Arg.Any<Guid>(), Arg.Any<DateTime>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success<IReadOnlyList<ZaptecSession>>([Session, Voided]));
        _chargers.GetForActiveLocationsAsync(Arg.Any<CancellationToken>()).Returns([_first, _second]);
        _sessions.UpsertAsync(Arg.Any<Guid>(), Arg.Any<Guid>(),
                Arg.Do<IReadOnlyList<ChargingSessionToStore>>(_upserts.Add), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);
    }

    private static EvCharger Charger(Guid locationId, Guid chargerId) =>
        new(Guid.NewGuid(), locationId, chargerId, "Test charger", Guid.NewGuid(), "Test installation");

    private Task ReadOnce(EvChargerReadService? service = null)
    {
        ServiceProvider services = new ServiceCollection()
            .AddSingleton(_chargers).AddSingleton(_sessions).AddSingleton(_client)
            .BuildServiceProvider();
        return (service ?? Service()).ReadOnceAsync(services, default);
    }

    private static EvChargerReadService Service() => new(
        Substitute.For<IServiceScopeFactory>(),
        new ConfigurationBuilder().Build(),
        new FixedTimeProvider(Now),
        NullLogger<EvChargerReadService>.Instance);

    private void Fail(EvCharger charger, Error error) =>
        _client.GetCompletedSessionsAsync(charger.ChargerId, Arg.Any<DateTime>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns(Result.Failure<IReadOnlyList<ZaptecSession>>(error));

    [Fact]
    public async Task TheFirstRead_GoesBackToTheStartOfThePreviousOsloMonth()
    {
        await ReadOnce();

        // 2026-10-01 00:00 in Oslo (summer time, +2) is 2026-09-30 22:00 UTC.
        await _client.Received(1).GetCompletedSessionsAsync(_first.ChargerId,
            new DateTime(2026, 9, 30, 22, 0, 0, DateTimeKind.Utc), NowUtc, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task LaterReads_GoBackADayBeforeTheWatermark()
    {
        _first.MarkReadUpTo(new DateTime(2026, 11, 20, 9, 45, 0, DateTimeKind.Utc));

        await ReadOnce();

        await _client.Received(1).GetCompletedSessionsAsync(_first.ChargerId,
            new DateTime(2026, 11, 19, 9, 45, 0, DateTimeKind.Utc), NowUtc, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ASuccess_StoresTheSplitSessionsAndMovesTheWatermark()
    {
        await ReadOnce();

        Assert.Equal(2, _upserts.Count);
        ChargingSessionToStore stored = _upserts[0][0];
        Assert.Equal(Session.Id, stored.Session.Id);
        Assert.Equal([4m, 4m], stored.Hours.Select(h => h.Kwh));
        Assert.Equal(NowUtc, _first.ReadUpToUtc);
        Assert.Equal(NowUtc, _second.ReadUpToUtc);
        await _sessions.Received(2).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DiscardedSessions_ArePassedOnSoTheyCanBeDeleted()
    {
        await ReadOnce();

        ChargingSessionToStore voided = _upserts[0].Single(s => s.Session.Id == Voided.Id);
        Assert.True(voided.Session.Discard);
        Assert.Empty(voided.Hours);
    }

    [Fact]
    public async Task AFailure_StoresNothingAndKeepsTheWatermark()
    {
        var before = new DateTime(2026, 11, 20, 9, 45, 0, DateTimeKind.Utc);
        _first.MarkReadUpTo(before);
        Fail(_first, EvChargerErrors.Unavailable);

        await ReadOnce();

        Assert.Equal(before, _first.ReadUpToUtc);
        await _sessions.DidNotReceive().UpsertAsync(_first.LocationId, Arg.Any<Guid>(),
            Arg.Any<IReadOnlyList<ChargingSessionToStore>>(), Arg.Any<CancellationToken>());
        // The other charger is still read and stored.
        Assert.Equal(NowUtc, _second.ReadUpToUtc);
        await _sessions.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AnException_AtOneCharger_DoesNotStopTheNext()
    {
        _client.GetCompletedSessionsAsync(_first.ChargerId, Arg.Any<DateTime>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns<Task<Result<IReadOnlyList<ZaptecSession>>>>(_ => throw new InvalidOperationException("boom"));

        await ReadOnce();

        Assert.Null(_first.ReadUpToUtc);
        Assert.Equal(NowUtc, _second.ReadUpToUtc);
    }

    [Fact]
    public async Task NotConfigured_CallsNothing()
    {
        _client.IsConfigured.Returns(false);

        await ReadOnce();

        await _chargers.DidNotReceive().GetForActiveLocationsAsync(Arg.Any<CancellationToken>());
        await _client.DidNotReceive().GetCompletedSessionsAsync(
            Arg.Any<Guid>(), Arg.Any<DateTime>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ASessionEndingBeforeItStarts_IsSkipped()
    {
        ZaptecSession backwards = Session with
        {
            Id = Guid.NewGuid(),
            StartUtc = Session.EndUtc,
            EndUtc = Session.StartUtc
        };
        _client.GetCompletedSessionsAsync(Arg.Any<Guid>(), Arg.Any<DateTime>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success<IReadOnlyList<ZaptecSession>>([backwards]));

        await ReadOnce();

        Assert.All(_upserts, u => Assert.Empty(u));
    }
}
