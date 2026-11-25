using Application.Common.Interfaces.Repositories;
using Application.WaterHeaters;
using Domain.Common;
using Domain.Common.Entities;
using Features.Tests.NorgesPris;
using Features.WaterHeaters.Handlers;
using Features.WaterHeaters.Queries;
using Features.WaterHeaters.Services;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using static Features.Tests.WaterHeaters.WaterHeaterTestData;

namespace Features.Tests.WaterHeaters;

/// <summary>The hot water used: access, the period, and the roll-up of the hourly litres (spec 020).</summary>
public class GetWaterHeaterHotWaterHandlerTests
{
    private const string Now = "2026-11-21T12:30:00Z";
    private static readonly DateTime NowUtc = new(2026, 11, 21, 12, 30, 0, DateTimeKind.Utc);
    private static readonly DateOnly Today = new(2026, 11, 21);

    /// <summary>Oslo midnight starting 21 November 2026.</summary>
    private static readonly DateTime DayStart = new(2026, 11, 20, 23, 0, 0, DateTimeKind.Utc);

    private readonly ILocationRepository<Location> _locations = Substitute.For<ILocationRepository<Location>>();

    private readonly IWaterHeaterPowerSampleRepository<WaterHeaterPowerSample> _samples =
        Substitute.For<IWaterHeaterPowerSampleRepository<WaterHeaterPowerSample>>();

    public GetWaterHeaterHotWaterHandlerTests()
    {
        _locations.IsUserAssociatedAsync(UserId, LocationId, Arg.Any<CancellationToken>()).Returns(true);
        _locations.GetByIdAsync(LocationId, Arg.Any<CancellationToken>()).Returns(Location());
        Hours();
    }

    private void Hours(params WaterHeaterHotWaterHour[] hours) =>
        _samples.GetHourlyHotWaterAsync(LocationId, Arg.Any<DateTime>(), Arg.Any<DateTime>(), Arg.Any<DateTime>(),
                Arg.Any<HotWaterRuleValues>(), Arg.Any<CancellationToken>())
            .Returns(hours);

    private static WaterHeaterHotWaterHour Hour(DateTime start, decimal litres, decimal seconds = 3600m) =>
        new(start, litres, seconds);

    private Task<Result<WaterHeaterHotWaterResponse>> Get(
        HotWaterPeriod period = HotWaterPeriod.Day, DateOnly? date = null, bool isAdmin = false,
        Guid? userId = null) =>
        new GetWaterHeaterHotWaterQueryHandler(_locations, _samples, new FixedTimeProvider(Now),
                NullLogger<GetWaterHeaterHotWaterQueryHandler>.Instance)
            .Handle(new GetWaterHeaterHotWaterQuery(userId ?? UserId, isAdmin, LocationId, period, date), default);

    [Fact]
    public async Task ADay_HasOneItemPerHourUpToNow()
    {
        WaterHeaterHotWaterResponse response = (await Get(date: Today)).Value;

        // Oslo 00:00 to 13:00; 13:00 Oslo starts at 12:00Z, before 12:30Z.
        Assert.Equal(14, response.Items.Count);
        Assert.Equal(DayStart, response.Items[0].Start);
        Assert.Equal(DayStart.AddHours(13), response.Items[^1].Start);
        Assert.Equal((DayStart, DayStart.AddDays(1)), (response.From, response.To));
        Assert.Equal("day", response.Period);
        Assert.Equal(LocationId, response.LocationId);
    }

    [Fact]
    public async Task HoursWithRows_AreRoundedHalfAwayFromZero_AndTheOthersAreNull()
    {
        Hours(Hour(DayStart.AddHours(8), 37.5m), Hour(DayStart.AddHours(9), 0.4m));

        IReadOnlyList<WaterHeaterHotWaterItem> items = (await Get(date: Today)).Value.Items;

        Assert.Equal(new WaterHeaterHotWaterItem(DayStart.AddHours(8), 38, 60), items[8]);
        Assert.Equal(new WaterHeaterHotWaterItem(DayStart.AddHours(9), 0, 60), items[9]);
        Assert.Equal(new WaterHeaterHotWaterItem(DayStart.AddHours(7), null, 0), items[7]);
    }

    [Fact]
    public async Task SampledMinutes_AreRoundedDown_AndCappedAtTheHour()
    {
        Hours(Hour(DayStart.AddHours(1), 5m, 3599.9m), Hour(DayStart.AddHours(2), 5m, 3700m));

        IReadOnlyList<WaterHeaterHotWaterItem> items = (await Get(date: Today)).Value.Items;

        Assert.Equal(59, items[1].SampledMinutes);
        Assert.Equal(60, items[2].SampledMinutes);
    }

    [Fact]
    public async Task AWeek_RollsUtcHoursIntoTheirOsloDay_AndSumsBeforeRounding()
    {
        // Tuesday 17 November in Oslo runs from 16 Nov 23:00Z to 17 Nov 23:00Z.
        DateTime tuesday = new(2026, 11, 16, 23, 0, 0, DateTimeKind.Utc);
        Hours(Hour(tuesday, 0.3m), Hour(tuesday.AddHours(1), 0.3m), Hour(tuesday.AddHours(23), 0.3m),
            Hour(tuesday.AddHours(24), 4m));

        WaterHeaterHotWaterResponse response = (await Get(HotWaterPeriod.Week, Today)).Value;

        // Monday 16 to Saturday 21 November; Sunday starts after now.
        Assert.Equal(6, response.Items.Count);
        Assert.Equal(new WaterHeaterHotWaterItem(tuesday, 1, 180), response.Items[1]);
        Assert.Equal(new WaterHeaterHotWaterItem(tuesday.AddDays(1), 4, 60), response.Items[2]);
        Assert.Null(response.Items[0].Litres);
        Assert.Equal("week", response.Period);
    }

    [Fact]
    public async Task TheTotal_IsTheSumOfTheRoundedItems()
    {
        Hours(Hour(DayStart.AddHours(7), 0.4m), Hour(DayStart.AddHours(8), 0.4m), Hour(DayStart.AddHours(9), 37.5m,
            1800m));

        WaterHeaterHotWaterResponse response = (await Get(date: Today)).Value;

        Assert.Equal(38, response.TotalLitres);
        Assert.Equal(150, response.SampledMinutes);
    }

    [Fact]
    public async Task NoRows_GiveANullTotal_AndNullItems()
    {
        WaterHeaterHotWaterResponse response = (await Get(date: Today)).Value;

        Assert.Null(response.TotalLitres);
        Assert.Equal(0, response.SampledMinutes);
        Assert.All(response.Items, i => Assert.Equal((null, 0), (i.Litres, i.SampledMinutes)));
    }

    [Fact]
    public async Task APeriodWhollyInTheFuture_HasNoItems_AndANullTotal()
    {
        WaterHeaterHotWaterResponse response = (await Get(date: Today.AddDays(1))).Value;

        Assert.Empty(response.Items);
        Assert.Null(response.TotalLitres);
        Assert.Equal(DayStart.AddDays(1), response.From);
    }

    [Fact]
    public async Task TheRepository_GetsThePeriodNowAndTheRule()
    {
        await Get(HotWaterPeriod.Month, Today);

        await _samples.Received(1).GetHourlyHotWaterAsync(
            LocationId,
            new DateTime(2026, 10, 31, 23, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 11, 30, 23, 0, 0, DateTimeKind.Utc),
            NowUtc,
            HotWaterRule.Current,
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task NoDate_IsTodayInOslo()
    {
        WaterHeaterHotWaterResponse response = (await Get()).Value;

        Assert.Equal((DayStart, DayStart.AddDays(1)), (response.From, response.To));
    }

    [Fact]
    public async Task ALinkedUser_GetsThePeriod() =>
        Assert.True((await Get()).IsSuccess);

    [Fact]
    public async Task AnUnlinkedUser_IsNotFound_AndNoSamplesAreRead()
    {
        Result<WaterHeaterHotWaterResponse> result = await Get(userId: Guid.NewGuid());

        Assert.Equal("Location.NotFound", result.Error.Code);
        await _samples.DidNotReceive().GetHourlyHotWaterAsync(
            Arg.Any<Guid>(), Arg.Any<DateTime>(), Arg.Any<DateTime>(), Arg.Any<DateTime>(),
            Arg.Any<HotWaterRuleValues>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AnUnlinkedAdmin_GetsThePeriod() =>
        Assert.True((await Get(isAdmin: true, userId: Guid.NewGuid())).IsSuccess);

    [Fact]
    public async Task AnAdmin_ForAnInactiveLocation_GetsThePeriod()
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
