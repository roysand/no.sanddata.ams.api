using Application.Common.Interfaces.External;
using Application.Common.Interfaces.Repositories;
using Application.Common.Interfaces.Security;
using Application.WaterHeaters;
using Domain.Common;
using Domain.Common.Entities;
using Features.Tests.NorgesPris;
using Features.WaterHeaters.Commands;
using Features.WaterHeaters.Handlers;
using Features.WaterHeaters.Queries;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using static Features.Tests.WaterHeaters.WaterHeaterTestData;

namespace Features.Tests.WaterHeaters;

public class ConnectWaterHeaterHandlerTests
{
    private readonly ILocationRepository<Location> _locations = Substitute.For<ILocationRepository<Location>>();
    private readonly IWaterHeaterRepository<WaterHeater> _heaters = Substitute.For<IWaterHeaterRepository<WaterHeater>>();
    private readonly IWaterHeaterClient _client = Substitute.For<IWaterHeaterClient>();
    private readonly ISecretProtector _protector = Substitute.For<ISecretProtector>();

    public ConnectWaterHeaterHandlerTests()
    {
        _locations.GetByIdAsync(LocationId, Arg.Any<CancellationToken>()).Returns(Location());
        _protector.IsConfigured.Returns(true);
        _protector.Protect(OsoSamples.Key, LocationId).Returns("v1:encrypted");
        _client.GetStateAsync(Arg.Any<WaterHeaterCredentials>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success(Reading()));
    }

    /// <summary>10:00 UTC on 20 November 2026, 11:00 in Oslo: today is 2026-11-20.</summary>
    private static readonly FixedTimeProvider Now = new("2026-11-20T10:00:00Z");

    private Task<Result<WaterHeaterSettingsResponse>> Connect(
        string deviceId = OsoSamples.DeviceId, DateOnly? expiresOn = null) =>
        new ConnectWaterHeaterCommandHandler(
                _locations, _heaters, _client, _protector, Now, NullLogger<ConnectWaterHeaterCommandHandler>.Instance)
            .Handle(new ConnectWaterHeaterCommand(UserId, LocationId, OsoSamples.Key, deviceId, expiresOn), default);

    [Fact]
    public async Task OsoAccepts_InsertsTheHeaterWithTheProtectedKey()
    {
        Result<WaterHeaterSettingsResponse> result = await Connect(OsoSamples.DeviceId.ToUpperInvariant());

        Assert.True(result.IsSuccess);
        _heaters.Received(1).Insert(Arg.Is<WaterHeater>(h =>
            h.LocationId == LocationId
            && h.DeviceId == OsoSamples.DeviceId
            && h.ProtectedSubscriptionKey == "v1:encrypted"));
        await _heaters.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
        Assert.Equal("abcdef…456789", result.Value.DeviceIdHint);
    }

    [Fact]
    public async Task OsoIsCheckedWithTheNewCredentials_BeforeSaving()
    {
        await Connect();

        await _client.Received(1).GetStateAsync(
            Arg.Is<WaterHeaterCredentials>(c =>
                c.LocationId == LocationId && c.DeviceId == OsoSamples.DeviceId && c.SubscriptionKey == OsoSamples.Key),
            Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("WaterHeater.Rejected")]
    [InlineData("WaterHeater.Unavailable")]
    [InlineData("WaterHeater.InvalidResponse")]
    public async Task OsoFails_NothingIsSaved(string code)
    {
        Error error = code switch
        {
            "WaterHeater.Rejected" => WaterHeaterErrors.Rejected,
            "WaterHeater.Unavailable" => WaterHeaterErrors.Unavailable,
            _ => WaterHeaterErrors.InvalidResponse
        };
        _client.GetStateAsync(Arg.Any<WaterHeaterCredentials>(), Arg.Any<CancellationToken>())
            .Returns(Result.Failure<WaterHeaterReading>(error));

        Result<WaterHeaterSettingsResponse> result = await Connect();

        Assert.Equal(code, result.Error.Code);
        _heaters.DidNotReceive().Insert(Arg.Any<WaterHeater>());
        await _heaters.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task EncryptionNotConfigured_IsRefusedWithoutCallingOso()
    {
        _protector.IsConfigured.Returns(false);

        Result<WaterHeaterSettingsResponse> result = await Connect();

        Assert.Equal("WaterHeater.EncryptionNotConfigured", result.Error.Code);
        await _client.DidNotReceive().GetStateAsync(Arg.Any<WaterHeaterCredentials>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UnknownLocation_IsNotFound()
    {
        _locations.GetByIdAsync(LocationId, Arg.Any<CancellationToken>()).Returns((Location?)null);

        Result<WaterHeaterSettingsResponse> result = await Connect();

        Assert.Equal("Location.NotFound", result.Error.Code);
    }

    [Fact]
    public async Task DeviceAtAnotherLocation_IsAConflict()
    {
        _heaters.GetByDeviceIdAsync(OsoSamples.DeviceId, Arg.Any<CancellationToken>())
            .Returns(Heater(locationId: Guid.NewGuid()));

        Result<WaterHeaterSettingsResponse> result = await Connect();

        Assert.Equal("WaterHeater.DeviceInUse", result.Error.Code);
        await _client.DidNotReceive().GetStateAsync(Arg.Any<WaterHeaterCredentials>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ConnectingAgain_ReplacesTheExistingHeater()
    {
        WaterHeater existing = Heater(deviceId: "0123456789abcdef0123");
        existing.CreatedAt = new DateTime(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc);
        _heaters.GetByLocationAsync(LocationId, Arg.Any<CancellationToken>()).Returns(existing);

        Result<WaterHeaterSettingsResponse> result = await Connect();

        Assert.True(result.IsSuccess);
        _heaters.DidNotReceive().Insert(Arg.Any<WaterHeater>());
        Assert.Equal(OsoSamples.DeviceId, existing.DeviceId);
        Assert.Equal("v1:encrypted", existing.ProtectedSubscriptionKey);
        Assert.Equal(new DateTime(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc), result.Value.ConnectedAt);
        await _heaters.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SameDeviceSameLocation_IsAReplaceNotAConflict()
    {
        WaterHeater existing = Heater();
        _heaters.GetByDeviceIdAsync(OsoSamples.DeviceId, Arg.Any<CancellationToken>()).Returns(existing);
        _heaters.GetByLocationAsync(LocationId, Arg.Any<CancellationToken>()).Returns(existing);

        Result<WaterHeaterSettingsResponse> result = await Connect();

        Assert.True(result.IsSuccess);
    }

    // ---- the OSO key's expiry date (spec 015) ---------------------------------------------------------------------

    [Fact]
    public async Task TheExpiryDate_IsStoredAndReturned()
    {
        Result<WaterHeaterSettingsResponse> result = await Connect(expiresOn: new DateOnly(2027, 11, 19));

        Assert.True(result.IsSuccess);
        _heaters.Received(1).Insert(Arg.Is<WaterHeater>(h => h.SubscriptionKeyExpiresOn == new DateOnly(2027, 11, 19)));
        Assert.Equal(new DateOnly(2027, 11, 19), result.Value.KeyExpiresOn);
    }

    [Fact]
    public async Task WithoutADate_TheKeyIsValidForOneYearFromTodayInOslo()
    {
        Result<WaterHeaterSettingsResponse> result = await Connect();

        Assert.Equal(new DateOnly(2027, 11, 20), result.Value.KeyExpiresOn);
    }

    [Theory]
    [InlineData("2026-11-20")] // today
    [InlineData("2026-11-19")] // yesterday
    [InlineData("2031-11-21")] // more than five years ahead
    public async Task ADateOutOfRange_IsRefusedBeforeOsoIsCalled(string date)
    {
        Result<WaterHeaterSettingsResponse> result = await Connect(expiresOn: DateOnly.Parse(date));

        Assert.Equal(WaterHeaterErrors.KeyExpiryOutOfRange, result.Error);
        await _client.DidNotReceiveWithAnyArgs().GetStateAsync(default!, default);
        _heaters.DidNotReceiveWithAnyArgs().Insert(default!);
        await _heaters.DidNotReceiveWithAnyArgs().SaveChangesAsync(default);
    }

    [Fact]
    public async Task ExactlyFiveYearsAhead_IsAccepted()
    {
        Result<WaterHeaterSettingsResponse> result = await Connect(expiresOn: new DateOnly(2031, 11, 20));

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task Replacing_SetsTheNewDate_AndKeepsWhenItWasConnected()
    {
        WaterHeater existing = Heater(deviceId: "0123456789abcdef0123", expiresOn: new DateOnly(2027, 1, 1));
        existing.CreatedAt = new DateTime(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc);
        _heaters.GetByLocationAsync(LocationId, Arg.Any<CancellationToken>()).Returns(existing);

        await Connect(expiresOn: new DateOnly(2027, 12, 24));

        Assert.Equal(new DateOnly(2027, 12, 24), existing.SubscriptionKeyExpiresOn);
        Assert.Equal(new DateTime(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc), existing.CreatedAt);
    }
}
