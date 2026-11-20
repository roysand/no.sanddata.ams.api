using Application.Common.Interfaces.Repositories;
using Domain.Common;
using Application.WaterHeaters;
using Domain.Common.Entities;
using Features.Tests.NorgesPris;
using Features.WaterHeaters.Commands;
using Features.WaterHeaters.Handlers;
using Features.WaterHeaters.Mappers;
using Features.WaterHeaters.Queries;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using static Features.Tests.WaterHeaters.WaterHeaterTestData;

namespace Features.Tests.WaterHeaters;

public class WaterHeaterAdminHandlerTests
{
    private readonly ILocationRepository<Location> _locations = Substitute.For<ILocationRepository<Location>>();
    private readonly IWaterHeaterRepository<WaterHeater> _heaters = Substitute.For<IWaterHeaterRepository<WaterHeater>>();

    public WaterHeaterAdminHandlerTests() =>
        _locations.GetByIdAsync(LocationId, Arg.Any<CancellationToken>()).Returns(Location());

    private Task<Result<WaterHeaterSettingsResponse>> GetSettings() =>
        new GetWaterHeaterSettingsQueryHandler(_locations, _heaters).Handle(new GetWaterHeaterSettingsQuery(LocationId), default);

    private Task<Result<WaterHeaterRemoved>> Remove() =>
        new RemoveWaterHeaterCommandHandler(_locations, _heaters, NullLogger<RemoveWaterHeaterCommandHandler>.Instance)
            .Handle(new RemoveWaterHeaterCommand(UserId, LocationId), default);

    [Fact]
    public async Task Settings_ShowTheHintAndNoKey()
    {
        _heaters.GetByLocationAsync(LocationId, Arg.Any<CancellationToken>()).Returns(Heater());

        Result<WaterHeaterSettingsResponse> result = await GetSettings();

        Assert.Equal("abcdef…456789", result.Value.DeviceIdHint);
        Assert.DoesNotContain(OsoSamples.DeviceId, result.Value.ToString());
    }

    [Fact]
    public async Task Settings_WithoutAHeater_AreNotConnected()
    {
        Result<WaterHeaterSettingsResponse> result = await GetSettings();

        Assert.Equal("WaterHeater.NotConnected", result.Error.Code);
    }

    [Fact]
    public async Task Settings_ForAnUnknownLocation_AreNotFound()
    {
        _locations.GetByIdAsync(LocationId, Arg.Any<CancellationToken>()).Returns((Location?)null);

        Assert.Equal("Location.NotFound", (await GetSettings()).Error.Code);
    }

    [Fact]
    public async Task Remove_DeletesTheHeater()
    {
        WaterHeater heater = Heater();
        _heaters.GetByLocationAsync(LocationId, Arg.Any<CancellationToken>()).Returns(heater);

        Result<WaterHeaterRemoved> result = await Remove();

        Assert.True(result.IsSuccess);
        _heaters.Received(1).Delete(heater);
    }

    [Fact]
    public async Task Remove_WithoutAHeater_IsNotConnected()
    {
        Assert.Equal("WaterHeater.NotConnected", (await Remove()).Error.Code);
        _heaters.DidNotReceive().Delete(Arg.Any<WaterHeater>());
    }

    [Fact]
    public async Task Remove_ForAnUnknownLocation_IsNotFound()
    {
        _locations.GetByIdAsync(LocationId, Arg.Any<CancellationToken>()).Returns((Location?)null);

        Assert.Equal("Location.NotFound", (await Remove()).Error.Code);
    }

    [Theory]
    [InlineData("abcdef0123456789", "abcdef…456789")]
    [InlineData("abcdef012345", "************")]
    [InlineData("abc", "***")]
    public void DeviceIdHint_ShowsOnlyTheEnds(string deviceId, string hint) =>
        Assert.Equal(hint, WaterHeaterMapper.DeviceIdHint(deviceId));

    // ---- changing only the OSO key's expiry date (spec 015) -------------------------------------------------------

    private Task<Result<WaterHeaterSettingsResponse>> SetExpiry(DateOnly date) =>
        new SetWaterHeaterKeyExpiryCommandHandler(_heaters, new FixedTimeProvider("2026-11-20T10:00:00Z"),
                NullLogger<SetWaterHeaterKeyExpiryCommandHandler>.Instance)
            .Handle(new SetWaterHeaterKeyExpiryCommand(UserId, LocationId, date), default);

    [Fact]
    public async Task Settings_IncludeTheKeysExpiryDate()
    {
        _heaters.GetByLocationAsync(LocationId, Arg.Any<CancellationToken>()).Returns(Heater());

        Result<WaterHeaterSettingsResponse> result = await GetSettings();

        Assert.Equal(new DateOnly(2027, 11, 19), result.Value.KeyExpiresOn);
    }

    [Fact]
    public async Task SetExpiry_ChangesOnlyTheDate()
    {
        WaterHeater heater = Heater();
        heater.CreatedAt = new DateTime(2026, 11, 19, 19, 32, 0, DateTimeKind.Utc);
        _heaters.GetByLocationAsync(LocationId, Arg.Any<CancellationToken>()).Returns(heater);

        Result<WaterHeaterSettingsResponse> result = await SetExpiry(new DateOnly(2027, 12, 1));

        Assert.True(result.IsSuccess);
        Assert.Equal(new DateOnly(2027, 12, 1), result.Value.KeyExpiresOn);
        Assert.Equal(new DateOnly(2027, 12, 1), heater.SubscriptionKeyExpiresOn);
        Assert.Equal(OsoSamples.DeviceId, heater.DeviceId);
        Assert.Equal("v1:protected", heater.ProtectedSubscriptionKey);
        Assert.Equal(new DateTime(2026, 11, 19, 19, 32, 0, DateTimeKind.Utc), heater.CreatedAt);
        await _heaters.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SetExpiry_WithoutAHeater_IsNotConnected()
    {
        Result<WaterHeaterSettingsResponse> result = await SetExpiry(new DateOnly(2027, 12, 1));

        Assert.Equal(WaterHeaterErrors.NotConnected, result.Error);
    }

    [Theory]
    [InlineData("2026-11-20")]
    [InlineData("2031-11-21")]
    public async Task SetExpiry_OutOfRange_ChangesNothing(string date)
    {
        WaterHeater heater = Heater();
        _heaters.GetByLocationAsync(LocationId, Arg.Any<CancellationToken>()).Returns(heater);

        Result<WaterHeaterSettingsResponse> result = await SetExpiry(DateOnly.Parse(date));

        Assert.Equal(WaterHeaterErrors.KeyExpiryOutOfRange, result.Error);
        Assert.Equal(new DateOnly(2027, 11, 19), heater.SubscriptionKeyExpiresOn);
        await _heaters.DidNotReceiveWithAnyArgs().SaveChangesAsync(default);
    }
}
