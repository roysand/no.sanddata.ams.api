using Application.Common.Interfaces.External;
using Application.Common.Interfaces.Repositories;
using Application.Common.Interfaces.Security;
using Application.WaterHeaters;
using Domain.Common;
using Domain.Common.Entities;
using Features.Tests.NorgesPris;
using Features.WaterHeaters.Commands;
using Features.WaterHeaters.Handlers;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using static Features.Tests.WaterHeaters.WaterHeaterTestData;

namespace Features.Tests.WaterHeaters;

/// <summary>Turning High Demand on and off: the checks, the switch, the read-back (spec 022, US1 and US2).</summary>
public class SetWaterHeaterHighDemandHandlerTests
{
    private const string Now = "2026-11-26T05:21:20Z";

    private readonly ILocationRepository<Location> _locations = Substitute.For<ILocationRepository<Location>>();
    private readonly IWaterHeaterRepository<WaterHeater> _heaters =
        Substitute.For<IWaterHeaterRepository<WaterHeater>>();
    private readonly IWaterHeaterClient _client = Substitute.For<IWaterHeaterClient>();
    private readonly ISecretProtector _protector = Substitute.For<ISecretProtector>();

    public SetWaterHeaterHighDemandHandlerTests()
    {
        _locations.GetByIdAsync(LocationId, Arg.Any<CancellationToken>()).Returns(Location());
        _heaters.GetByLocationAsync(LocationId, Arg.Any<CancellationToken>()).Returns(Heater());
        _protector.Unprotect("v1:protected", LocationId).Returns(Result.Success(OsoSamples.Key));
        _client.SetHighDemandAsync(Arg.Any<WaterHeaterCredentials>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success());
        _client.GetStateAsync(Arg.Any<WaterHeaterCredentials>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success(Reading()));
    }

    private Task<Result<WaterHeaterHighDemandResponse>> Switch(bool on) =>
        new SetWaterHeaterHighDemandCommandHandler(
                _locations, _heaters, _client, _protector, new FixedTimeProvider(Now),
                NullLogger<SetWaterHeaterHighDemandCommandHandler>.Instance)
            .Handle(new SetWaterHeaterHighDemandCommand(UserId, LocationId, on), default);

    [Fact]
    public async Task On_SwitchesOnceThenReadsTheStateBack()
    {
        Result<WaterHeaterHighDemandResponse> result = await Switch(on: true);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value.HighDemand);
        Assert.Equal(LocationId, result.Value.LocationId);
        Assert.NotNull(result.Value.State);
        Assert.Equal("Supercharge", result.Value.State.SpecialMode.Mode);
        Assert.NotNull(result.Value.State.SpecialMode.End);
        Received.InOrder(() =>
        {
            _client.SetHighDemandAsync(
                Arg.Is<WaterHeaterCredentials>(c =>
                    c.DeviceId == OsoSamples.DeviceId && c.SubscriptionKey == OsoSamples.Key),
                true, Arg.Any<CancellationToken>());
            _client.GetStateAsync(Arg.Any<WaterHeaterCredentials>(), Arg.Any<CancellationToken>());
        });
        await _client.Received(1).SetHighDemandAsync(
            Arg.Any<WaterHeaterCredentials>(), Arg.Any<bool>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Off_SwitchesOffOnceAndAnswersTheStateReadBack()
    {
        WaterHeaterReading normal = Reading() with
        {
            SpecialMode = SpecialMode.None,
            SpecialModeStartUtc = null,
            SpecialModeEndUtc = null,
            ProfileName = "NormalMode"
        };
        _client.GetStateAsync(Arg.Any<WaterHeaterCredentials>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success(normal));

        Result<WaterHeaterHighDemandResponse> result = await Switch(on: false);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value.HighDemand);
        Assert.Equal("None", result.Value.State!.SpecialMode.Mode);
        await _client.Received(1).SetHighDemandAsync(
            Arg.Any<WaterHeaterCredentials>(), false, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Off_WhenAlreadyOff_IsASuccess()
    {
        _client.GetStateAsync(Arg.Any<WaterHeaterCredentials>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success(Reading() with { SpecialMode = SpecialMode.None }));

        Result<WaterHeaterHighDemandResponse> first = await Switch(on: false);
        Result<WaterHeaterHighDemandResponse> second = await Switch(on: false);

        Assert.True(first.IsSuccess);
        Assert.True(second.IsSuccess);
    }

    [Fact]
    public async Task TheReadBackFails_TheSwitchStillSucceedsWithoutAState()
    {
        _client.GetStateAsync(Arg.Any<WaterHeaterCredentials>(), Arg.Any<CancellationToken>())
            .Returns(Result.Failure<WaterHeaterReading>(WaterHeaterErrors.Unavailable));

        Result<WaterHeaterHighDemandResponse> result = await Switch(on: true);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value.HighDemand);
        Assert.Null(result.Value.State);
    }

    [Theory]
    [InlineData("WaterHeater.Unavailable")]
    [InlineData("WaterHeater.Rejected")]
    [InlineData("WaterHeater.HighDemandNotOffered")]
    public async Task TheSwitchFails_GivesItsErrorAndDoesNotReadTheState(string code)
    {
        Error error = code switch
        {
            "WaterHeater.Unavailable" => WaterHeaterErrors.Unavailable,
            "WaterHeater.Rejected" => WaterHeaterErrors.Rejected,
            _ => WaterHeaterErrors.HighDemandNotOffered
        };
        _client.SetHighDemandAsync(Arg.Any<WaterHeaterCredentials>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(Result.Failure(error));

        Result<WaterHeaterHighDemandResponse> result = await Switch(on: true);

        Assert.Equal(code, result.Error.Code);
        await _client.DidNotReceive().GetStateAsync(Arg.Any<WaterHeaterCredentials>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task NoLocation_IsNotFoundAndOsoIsNotCalled()
    {
        _locations.GetByIdAsync(LocationId, Arg.Any<CancellationToken>()).Returns((Location?)null);

        Result<WaterHeaterHighDemandResponse> result = await Switch(on: true);

        Assert.Equal("Location.NotFound", result.Error.Code);
        await AssertOsoNotCalled();
    }

    [Fact]
    public async Task NoHeater_IsNotConnectedAndOsoIsNotCalled()
    {
        _heaters.GetByLocationAsync(LocationId, Arg.Any<CancellationToken>()).Returns((WaterHeater?)null);

        Result<WaterHeaterHighDemandResponse> result = await Switch(on: true);

        Assert.Equal("WaterHeater.NotConnected", result.Error.Code);
        await AssertOsoNotCalled();
    }

    [Fact]
    public async Task AnUnreadableKey_IsKeyUnreadableAndOsoIsNotCalled()
    {
        _protector.Unprotect("v1:protected", LocationId)
            .Returns(Result.Failure<string>(WaterHeaterErrors.KeyUnreadable));

        Result<WaterHeaterHighDemandResponse> result = await Switch(on: false);

        Assert.Equal("WaterHeater.KeyUnreadable", result.Error.Code);
        await AssertOsoNotCalled();
    }

    [Fact]
    public async Task AnInactiveLocation_CanStillBeSwitched()
    {
        _locations.GetByIdAsync(LocationId, Arg.Any<CancellationToken>()).Returns(Location(active: false));

        Result<WaterHeaterHighDemandResponse> result = await Switch(on: true);

        Assert.True(result.IsSuccess);
    }

    private async Task AssertOsoNotCalled()
    {
        await _client.DidNotReceive().SetHighDemandAsync(
            Arg.Any<WaterHeaterCredentials>(), Arg.Any<bool>(), Arg.Any<CancellationToken>());
        await _client.DidNotReceive().GetStateAsync(Arg.Any<WaterHeaterCredentials>(), Arg.Any<CancellationToken>());
    }
}
