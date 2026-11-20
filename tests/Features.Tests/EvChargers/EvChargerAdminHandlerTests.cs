using Application.Common.Interfaces.External;
using Application.Common.Interfaces.Repositories;
using Application.EvChargers;
using Domain.Common;
using Domain.Common.Entities;
using Features.EvChargers.Commands;
using Features.EvChargers.Handlers;
using Features.EvChargers.Queries;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Features.Tests.EvChargers;

public class EvChargerAdminHandlerTests
{
    private static readonly Guid UserId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid LocationId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly Guid OtherLocationId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
    private static readonly Guid OtherChargerId = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd");

    private static readonly ZaptecCharger Charger = new(
        ZaptecSamples.ChargerId, "Test charger", ZaptecSamples.InstallationId, "Test installation");
    private static readonly ZaptecCharger Other = new(
        OtherChargerId, "Other charger", ZaptecSamples.InstallationId, "Test installation");

    private readonly ILocationRepository<Location> _locations = Substitute.For<ILocationRepository<Location>>();
    private readonly IEvChargerRepository<EvCharger> _chargers = Substitute.For<IEvChargerRepository<EvCharger>>();
    private readonly IEvChargerClient _client = Substitute.For<IEvChargerClient>();
    private readonly Location _location = new(LocationId, "Hjemme", "Address", "SN-1", "NO1", true, false);

    public EvChargerAdminHandlerTests()
    {
        _locations.GetByIdAsync(LocationId, Arg.Any<CancellationToken>()).Returns(_location);
        _client.GetChargersAsync(Arg.Any<CancellationToken>())
            .Returns(Result.Success<IReadOnlyList<ZaptecCharger>>([Charger, Other]));
    }

    private Task<Result<EvChargerSettingsResponse>> Connect(Guid chargerId) =>
        new ConnectEvChargerCommandHandler(_locations, _chargers, _client, NullLogger<ConnectEvChargerCommandHandler>.Instance)
            .Handle(new ConnectEvChargerCommand(UserId, LocationId, chargerId), default);

    private Task<Result<EvChargerRemoved>> Remove() =>
        new RemoveEvChargerCommandHandler(_locations, _chargers, NullLogger<RemoveEvChargerCommandHandler>.Instance)
            .Handle(new RemoveEvChargerCommand(UserId, LocationId), default);

    private Task<Result<EvChargerSettingsResponse>> Settings() =>
        new GetEvChargerSettingsQueryHandler(_locations, _chargers).Handle(new GetEvChargerSettingsQuery(LocationId), default);

    private static EvCharger Connected(Guid locationId) =>
        new(Guid.NewGuid(), locationId, Charger.Id, Charger.Name, Charger.InstallationId, Charger.InstallationName);

    [Fact]
    public async Task List_PassesTheChargersThrough()
    {
        Result<IReadOnlyList<ZaptecChargerResponse>> result =
            await new ListZaptecChargersQueryHandler(_client).Handle(new ListZaptecChargersQuery(), default);

        Assert.Equal(
            [new ZaptecChargerResponse(Charger.Id, "Test charger", Charger.InstallationId, "Test installation"),
                new ZaptecChargerResponse(Other.Id, "Other charger", Other.InstallationId, "Test installation")],
            result.Value);
    }

    [Fact]
    public async Task List_PassesErrorsThrough()
    {
        _client.GetChargersAsync(Arg.Any<CancellationToken>())
            .Returns(Result.Failure<IReadOnlyList<ZaptecCharger>>(EvChargerErrors.NotConfigured));

        Result<IReadOnlyList<ZaptecChargerResponse>> result =
            await new ListZaptecChargersQueryHandler(_client).Handle(new ListZaptecChargersQuery(), default);

        Assert.Equal(EvChargerErrors.NotConfigured, result.Error);
    }

    [Fact]
    public async Task Connect_SavesTheIdsAndNames()
    {
        Result<EvChargerSettingsResponse> result = await Connect(Charger.Id);

        Assert.True(result.IsSuccess);
        _chargers.Received(1).Insert(Arg.Is<EvCharger>(c =>
            c.LocationId == LocationId && c.ChargerId == Charger.Id && c.ChargerName == "Test charger" &&
            c.InstallationId == Charger.InstallationId && c.InstallationName == "Test installation" &&
            c.ReadUpToUtc == null));
        await _chargers.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
        Assert.Equal(Charger.Id, result.Value.ChargerId);
        Assert.False(result.Value.IncludeInTotals);
    }

    [Fact]
    public async Task Connect_AnUnknownCharger_SavesNothing()
    {
        Result<EvChargerSettingsResponse> result = await Connect(Guid.NewGuid());

        Assert.Equal(EvChargerErrors.UnknownCharger, result.Error);
        _chargers.DidNotReceive().Insert(Arg.Any<EvCharger>());
        await _chargers.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Connect_AChargerAtAnotherLocation_IsInUse()
    {
        _chargers.GetByChargerIdAsync(Charger.Id, Arg.Any<CancellationToken>()).Returns(Connected(OtherLocationId));

        Result<EvChargerSettingsResponse> result = await Connect(Charger.Id);

        Assert.Equal(EvChargerErrors.ChargerInUse, result.Error);
        await _client.DidNotReceive().GetChargersAsync(Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("EvCharger.Unavailable")]
    [InlineData("EvCharger.SignInRejected")]
    [InlineData("EvCharger.NotConfigured")]
    public async Task Connect_WhenZaptecFails_SavesNothing(string code)
    {
        Error error = code switch
        {
            "EvCharger.Unavailable" => EvChargerErrors.Unavailable,
            "EvCharger.SignInRejected" => EvChargerErrors.SignInRejected,
            _ => EvChargerErrors.NotConfigured
        };
        _client.GetChargersAsync(Arg.Any<CancellationToken>()).Returns(Result.Failure<IReadOnlyList<ZaptecCharger>>(error));

        Result<EvChargerSettingsResponse> result = await Connect(Charger.Id);

        Assert.Equal(code, result.Error.Code);
        await _chargers.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Connect_ToAMissingLocation_IsNotFound()
    {
        Result<EvChargerSettingsResponse> result =
            await new ConnectEvChargerCommandHandler(_locations, _chargers, _client,
                    NullLogger<ConnectEvChargerCommandHandler>.Instance)
                .Handle(new ConnectEvChargerCommand(UserId, OtherLocationId, Charger.Id), default);

        Assert.Equal("Location.NotFound", result.Error.Code);
    }

    [Fact]
    public async Task Connect_AnotherCharger_ReplacesAndReadsHistoryAgain()
    {
        EvCharger existing = Connected(LocationId);
        existing.MarkReadUpTo(new DateTime(2026, 11, 20, 12, 0, 0, DateTimeKind.Utc));
        _chargers.GetByLocationAsync(LocationId, Arg.Any<CancellationToken>()).Returns(existing);

        Result<EvChargerSettingsResponse> result = await Connect(Other.Id);

        Assert.True(result.IsSuccess);
        Assert.Equal(Other.Id, existing.ChargerId);
        Assert.Equal("Other charger", existing.ChargerName);
        Assert.Null(existing.ReadUpToUtc);
        _chargers.DidNotReceive().Insert(Arg.Any<EvCharger>());
    }

    [Fact]
    public async Task Connect_TheSameChargerAgain_KeepsTheWatermark()
    {
        EvCharger existing = Connected(LocationId);
        var readUpTo = new DateTime(2026, 11, 20, 12, 0, 0, DateTimeKind.Utc);
        existing.MarkReadUpTo(readUpTo);
        _chargers.GetByLocationAsync(LocationId, Arg.Any<CancellationToken>()).Returns(existing);
        _chargers.GetByChargerIdAsync(Charger.Id, Arg.Any<CancellationToken>()).Returns(existing);

        Result<EvChargerSettingsResponse> result = await Connect(Charger.Id);

        Assert.True(result.IsSuccess);
        Assert.Equal(readUpTo, existing.ReadUpToUtc);
    }

    [Fact]
    public async Task Settings_WithoutACharger_AreNotConnected()
    {
        Result<EvChargerSettingsResponse> result = await Settings();

        Assert.Equal(EvChargerErrors.NotConnected, result.Error);
    }

    [Fact]
    public async Task Settings_ShowTheChargerAndTheSetting()
    {
        _location.SetIncludeEvCharger(true);
        _chargers.GetByLocationAsync(LocationId, Arg.Any<CancellationToken>()).Returns(Connected(LocationId));

        Result<EvChargerSettingsResponse> result = await Settings();

        Assert.Equal("Test charger", result.Value.ChargerName);
        Assert.Equal("Test installation", result.Value.InstallationName);
        Assert.True(result.Value.IncludeInTotals);
    }

    [Fact]
    public async Task Remove_DeletesTheConnectionAndTurnsTheSettingOff()
    {
        _location.SetIncludeEvCharger(true);
        EvCharger charger = Connected(LocationId);
        _chargers.GetByLocationAsync(LocationId, Arg.Any<CancellationToken>()).Returns(charger);

        Result<EvChargerRemoved> result = await Remove();

        Assert.True(result.IsSuccess);
        _chargers.Received(1).Delete(charger);
        Assert.False(_location.IncludeEvChargerInTotals);
    }

    [Fact]
    public async Task Remove_WithoutACharger_IsNotConnected()
    {
        Result<EvChargerRemoved> result = await Remove();

        Assert.Equal(EvChargerErrors.NotConnected, result.Error);
        _chargers.DidNotReceive().Delete(Arg.Any<EvCharger>());
    }
}
