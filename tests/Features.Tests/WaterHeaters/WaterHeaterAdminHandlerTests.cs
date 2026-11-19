using Application.Common.Interfaces.Repositories;
using Domain.Common;
using Domain.Common.Entities;
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
}
