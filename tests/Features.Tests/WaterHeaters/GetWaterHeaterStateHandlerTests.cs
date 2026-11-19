using Application.Common.Interfaces.External;
using Application.Common.Interfaces.Repositories;
using Application.Common.Interfaces.Security;
using Application.WaterHeaters;
using Domain.Common;
using Domain.Common.Entities;
using Features.Tests.NorgesPris;
using Features.WaterHeaters.Handlers;
using Features.WaterHeaters.Queries;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using static Features.Tests.WaterHeaters.WaterHeaterTestData;

namespace Features.Tests.WaterHeaters;

public class GetWaterHeaterStateHandlerTests
{
    private const string Now = "2026-11-19T18:08:31Z";
    private static readonly DateTime NowUtc = new(2026, 11, 19, 18, 8, 31, DateTimeKind.Utc);

    private readonly ILocationRepository<Location> _locations = Substitute.For<ILocationRepository<Location>>();
    private readonly IWaterHeaterRepository<WaterHeater> _heaters = Substitute.For<IWaterHeaterRepository<WaterHeater>>();
    private readonly IWaterHeaterClient _client = Substitute.For<IWaterHeaterClient>();
    private readonly ISecretProtector _protector = Substitute.For<ISecretProtector>();

    public GetWaterHeaterStateHandlerTests()
    {
        _locations.IsUserAssociatedAsync(UserId, LocationId, Arg.Any<CancellationToken>()).Returns(true);
        _locations.GetByIdAsync(LocationId, Arg.Any<CancellationToken>()).Returns(Location());
        _heaters.GetByLocationAsync(LocationId, Arg.Any<CancellationToken>()).Returns(Heater());
        _protector.Unprotect("v1:protected", LocationId).Returns(Result.Success(OsoSamples.Key));
        _client.GetStateAsync(Arg.Any<WaterHeaterCredentials>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success(Reading()));
    }

    private Task<Result<WaterHeaterStateResponse>> Get(bool isAdmin = false, Guid? userId = null) =>
        new GetWaterHeaterStateQueryHandler(_locations, _heaters, _client, _protector, new FixedTimeProvider(Now),
                NullLogger<GetWaterHeaterStateQueryHandler>.Instance)
            .Handle(new GetWaterHeaterStateQuery(userId ?? UserId, isAdmin, LocationId), default);

    [Fact]
    public async Task ALinkedUser_GetsTheMappedState()
    {
        Result<WaterHeaterStateResponse> result = await Get();

        Assert.True(result.IsSuccess);
        WaterHeaterStateResponse s = result.Value;
        Assert.Equal(102, s.AgeSeconds);
        Assert.False(s.IsStale);
        Assert.Equal(344, s.HotWater.AvailableLitres40);
        Assert.Equal(72.8m, s.Temperatures.TopCelsius);
        Assert.Null(s.Temperatures.FourthCelsius);
        Assert.Equal("Supercharge", s.SpecialMode.Mode);
        Assert.NotNull(s.SpecialMode.Start);
        Assert.Equal(new WaterHeaterOsoCodes(0, 1, 2, 3), s.OsoCodes);
        await _client.Received(1).GetStateAsync(
            Arg.Is<WaterHeaterCredentials>(c => c.DeviceId == OsoSamples.DeviceId && c.SubscriptionKey == OsoSamples.Key),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AnUnlinkedUser_IsNotFound_AndOsoIsNotCalled()
    {
        Result<WaterHeaterStateResponse> result = await Get(userId: Guid.NewGuid());

        Assert.Equal("Location.NotFound", result.Error.Code);
        await _client.DidNotReceive().GetStateAsync(Arg.Any<WaterHeaterCredentials>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AnUnlinkedAdmin_GetsTheState()
    {
        Result<WaterHeaterStateResponse> result = await Get(isAdmin: true, userId: Guid.NewGuid());

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task AnAdmin_ForAnInactiveOrUnknownLocation_IsNotFound()
    {
        _locations.GetByIdAsync(LocationId, Arg.Any<CancellationToken>()).Returns(Location(active: false));
        Assert.Equal("Location.NotFound", (await Get(isAdmin: true)).Error.Code);

        _locations.GetByIdAsync(LocationId, Arg.Any<CancellationToken>()).Returns((Location?)null);
        Assert.Equal("Location.NotFound", (await Get(isAdmin: true)).Error.Code);
    }

    [Fact]
    public async Task NoHeater_IsNotConnected()
    {
        _heaters.GetByLocationAsync(LocationId, Arg.Any<CancellationToken>()).Returns((WaterHeater?)null);

        Assert.Equal("WaterHeater.NotConnected", (await Get()).Error.Code);
    }

    [Fact]
    public async Task AnUnreadableKey_FailsWithoutCallingOso()
    {
        _protector.Unprotect("v1:protected", LocationId).Returns(Result.Failure<string>(WaterHeaterErrors.KeyUnreadable));

        Result<WaterHeaterStateResponse> result = await Get();

        Assert.Equal("WaterHeater.KeyUnreadable", result.Error.Code);
        await _client.DidNotReceive().GetStateAsync(Arg.Any<WaterHeaterCredentials>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("WaterHeater.Rejected")]
    [InlineData("WaterHeater.Unavailable")]
    [InlineData("WaterHeater.InvalidResponse")]
    public async Task OsoErrors_ArePassedThrough(string code)
    {
        Error error = code switch
        {
            "WaterHeater.Rejected" => WaterHeaterErrors.Rejected,
            "WaterHeater.Unavailable" => WaterHeaterErrors.Unavailable,
            _ => WaterHeaterErrors.InvalidResponse
        };
        _client.GetStateAsync(Arg.Any<WaterHeaterCredentials>(), Arg.Any<CancellationToken>())
            .Returns(Result.Failure<WaterHeaterReading>(error));

        Assert.Equal(code, (await Get()).Error.Code);
    }

    [Fact]
    public async Task AReadingOlderThan15Minutes_IsStale()
    {
        _client.GetStateAsync(Arg.Any<WaterHeaterCredentials>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success(Reading(NowUtc.AddMinutes(-16))));

        WaterHeaterStateResponse s = (await Get()).Value;

        Assert.Equal(960, s.AgeSeconds);
        Assert.True(s.IsStale);
    }

    [Fact]
    public async Task AReadingInTheFuture_HasAgeZero()
    {
        _client.GetStateAsync(Arg.Any<WaterHeaterCredentials>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success(Reading(NowUtc.AddSeconds(5))));

        Assert.Equal(0, (await Get()).Value.AgeSeconds);
    }

    [Fact]
    public async Task NoSpecialMode_HasNoTimes()
    {
        _client.GetStateAsync(Arg.Any<WaterHeaterCredentials>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success(Reading() with { SpecialMode = SpecialMode.None }));

        WaterHeaterStateResponse s = (await Get()).Value;

        Assert.Equal("None", s.SpecialMode.Mode);
        Assert.Null(s.SpecialMode.Start);
        Assert.Null(s.SpecialMode.End);
    }
}
