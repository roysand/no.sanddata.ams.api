using Application.Common.Interfaces.Repositories;
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

/// <summary>The 5-minute power: access, the window, and the mapping of kWh and seconds (spec 017).</summary>
public class GetWaterHeaterPowerHandlerTests
{
    private const string Now = "2026-11-20T12:00:00Z";
    private static readonly DateTime NowUtc = new(2026, 11, 20, 12, 0, 0, DateTimeKind.Utc);

    private readonly ILocationRepository<Location> _locations = Substitute.For<ILocationRepository<Location>>();

    private readonly IWaterHeaterPowerSampleRepository<WaterHeaterPowerSample> _samples =
        Substitute.For<IWaterHeaterPowerSampleRepository<WaterHeaterPowerSample>>();

    public GetWaterHeaterPowerHandlerTests()
    {
        _locations.IsUserAssociatedAsync(UserId, LocationId, Arg.Any<CancellationToken>()).Returns(true);
        _locations.GetByIdAsync(LocationId, Arg.Any<CancellationToken>()).Returns(Location());
        Steps();
    }

    private void Steps(params WaterHeaterPowerStep[] steps) =>
        _samples.GetPowerStepsAsync(LocationId, Arg.Any<DateTime>(), Arg.Any<DateTime>(), Arg.Any<DateTime>(),
                Arg.Any<CancellationToken>())
            .Returns(steps);

    private static WaterHeaterPowerStep Step(int minute, decimal kwh, decimal seconds) =>
        new(NowUtc.AddHours(-1).AddMinutes(minute), kwh, seconds);

    private Task<Result<WaterHeaterPowerResponse>> Get(
        bool isAdmin = false, Guid? userId = null, DateTime? from = null, DateTime? to = null) =>
        new GetWaterHeaterPowerQueryHandler(_locations, _samples, new FixedTimeProvider(Now),
                NullLogger<GetWaterHeaterPowerQueryHandler>.Instance)
            .Handle(new GetWaterHeaterPowerQuery(userId ?? UserId, isAdmin, LocationId, from, to), default);

    [Fact]
    public async Task AFullStep_IsItsAveragePower()
    {
        Steps(Step(0, 0.1693m, 300m));

        WaterHeaterPowerItem item = Assert.Single((await Get()).Value.Items);

        Assert.Equal(NowUtc.AddHours(-1), item.Start);
        Assert.Equal(2.032m, item.PowerKw);
        Assert.Equal(300, item.SampledSeconds);
    }

    [Fact]
    public async Task APartlySampledStep_IsTheAverageOverItsSampledTime()
    {
        Steps(Step(5, 0.0150m, 134.4m));

        WaterHeaterPowerItem item = Assert.Single((await Get()).Value.Items);

        Assert.Equal(0.402m, item.PowerKw);
        Assert.Equal(134, item.SampledSeconds);
    }

    [Fact]
    public async Task AStepThatRoundsToZeroSeconds_IsLeftOut()
    {
        Steps(Step(0, 0.0001m, 0.4m), Step(5, 0.1693m, 300m));

        WaterHeaterPowerItem item = Assert.Single((await Get()).Value.Items);

        Assert.Equal(NowUtc.AddHours(-1).AddMinutes(5), item.Start);
    }

    [Fact]
    public async Task NoSamples_IsAnEmptyList()
    {
        Result<WaterHeaterPowerResponse> result = await Get();

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value.Items);
    }

    [Fact]
    public async Task TheDefaultWindow_IsTheLastThreeHours_AndIsInTheResponse()
    {
        WaterHeaterPowerResponse response = (await Get()).Value;

        Assert.Equal(LocationId, response.LocationId);
        Assert.Equal(NowUtc.AddHours(-3), response.From);
        Assert.Equal(NowUtc, response.To);
        Assert.Equal(300, response.StepSeconds);
        await _samples.Received(1).GetPowerStepsAsync(
            LocationId, NowUtc.AddHours(-3), NowUtc, NowUtc, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AGivenWindow_IsPassedOnWithNow()
    {
        DateTime from = new(2026, 11, 19, 9, 0, 0, DateTimeKind.Utc);
        DateTime to = from.AddHours(3);

        WaterHeaterPowerResponse response = (await Get(from: from, to: to)).Value;

        Assert.Equal((from, to), (response.From, response.To));
        await _samples.Received(1).GetPowerStepsAsync(LocationId, from, to, NowUtc, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ALinkedUser_GetsTheWindow() =>
        Assert.True((await Get()).IsSuccess);

    [Fact]
    public async Task AnUnlinkedUser_IsNotFound_AndNoSamplesAreRead()
    {
        Result<WaterHeaterPowerResponse> result = await Get(userId: Guid.NewGuid());

        Assert.Equal("Location.NotFound", result.Error.Code);
        await _samples.DidNotReceive().GetPowerStepsAsync(
            Arg.Any<Guid>(), Arg.Any<DateTime>(), Arg.Any<DateTime>(), Arg.Any<DateTime>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AnUnlinkedAdmin_GetsTheWindow() =>
        Assert.True((await Get(isAdmin: true, userId: Guid.NewGuid())).IsSuccess);

    [Fact]
    public async Task AnAdmin_ForAnInactiveLocation_GetsTheWindow()
    {
        _locations.GetByIdAsync(LocationId, Arg.Any<CancellationToken>()).Returns(Location(active: false));

        Assert.True((await Get(isAdmin: true)).IsSuccess);
    }

    [Fact]
    public async Task AnAdmin_ForAnUnknownLocation_IsNotFound()
    {
        _locations.GetByIdAsync(LocationId, Arg.Any<CancellationToken>()).Returns((Location?)null);

        Assert.Equal("Location.NotFound", (await Get(isAdmin: true)).Error.Code);
    }

    [Fact]
    public async Task NoAccess_AndAMissingLocation_GiveTheSameError()
    {
        Error noAccess = (await Get(userId: Guid.NewGuid())).Error;
        _locations.GetByIdAsync(LocationId, Arg.Any<CancellationToken>()).Returns((Location?)null);
        Error missing = (await Get(isAdmin: true)).Error;

        Assert.Equal((noAccess.Code, noAccess.Description), (missing.Code, missing.Description));
    }
}
