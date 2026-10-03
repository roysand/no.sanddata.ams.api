using Application.Common.Interfaces.Repositories;
using Domain.Common;
using Domain.Common.Entities;
using Features.ElectricityCost.Handlers;
using Features.ElectricityCost.Queries;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Features.Tests.ElectricityCost;

public class GetConsumptionQueryHandlerTests
{
    private static readonly DateTime Start = new(2026, 10, 2, 10, 0, 0, DateTimeKind.Utc);

    private readonly ILocationRepository<Location> _locations = Substitute.For<ILocationRepository<Location>>();
    private readonly IMeterRepository<Meter> _meters = Substitute.For<IMeterRepository<Meter>>();
    private readonly IConsumptionRepository _consumption = Substitute.For<IConsumptionRepository>();
    private readonly GetConsumptionQueryHandler _handler;

    public GetConsumptionQueryHandlerTests()
    {
        _handler = new GetConsumptionQueryHandler(_locations, _meters, _consumption,
            Substitute.For<ILogger<GetConsumptionQueryHandler>>());
        _locations.IsUserAssociatedAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(true);
    }

    [Fact]
    public async Task Handle_UnknownLocation_ReturnsLocationNotFound()
    {
        _locations.IsUserAssociatedAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(false);

        Result<ConsumptionResponse> result = await _handler.Handle(
            new GetConsumptionQuery(Guid.NewGuid(), Guid.NewGuid(), null, "hour", null, null), CancellationToken.None);

        Assert.Equal("Location.NotFound", result.Error.Code);
    }

    [Fact]
    public async Task Handle_MeterFromOtherLocation_ReturnsMeterNotFound()
    {
        var meterId = Guid.NewGuid();
        _meters.GetByIdAsync(meterId, Arg.Any<CancellationToken>())
            .Returns(new Meter(meterId, Guid.NewGuid(), "dev1", null, true));

        Result<ConsumptionResponse> result = await _handler.Handle(
            new GetConsumptionQuery(Guid.NewGuid(), Guid.NewGuid(), meterId, "hour", null, null), CancellationToken.None);

        Assert.Equal("Meter.NotFound", result.Error.Code);
    }

    [Fact]
    public async Task Handle_MinuteGranularity_ReturnsKwhPerMinute()
    {
        _consumption.GetMinuteAsync(Arg.Any<Guid>(), Arg.Any<Guid?>(), Arg.Any<DateTime>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns([new MinuteConsumption { BucketStart = Start, AvgPowerWatts = 6000 }]);

        Result<ConsumptionResponse> result = await _handler.Handle(
            new GetConsumptionQuery(Guid.NewGuid(), Guid.NewGuid(), null, "minute", null, null), CancellationToken.None);

        Assert.Equal("minute", result.Value.Granularity);
        Assert.Equal(0.1m, Assert.Single(result.Value.Items).ConsumptionKwh); // 6 kW for 1 minute
        await _consumption.DidNotReceive().GetHourlyAsync(
            Arg.Any<Guid>(), Arg.Any<Guid?>(), Arg.Any<DateTime>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_HourGranularity_SumsMetersInSameHour()
    {
        _consumption.GetHourlyAsync(Arg.Any<Guid>(), Arg.Any<Guid?>(), Arg.Any<DateTime>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns([
                new HourConsumption { MeterId = Guid.NewGuid(), BucketStart = Start, AvgPowerWatts = 1000 },
                new HourConsumption { MeterId = Guid.NewGuid(), BucketStart = Start, AvgPowerWatts = 500 }
            ]);

        Result<ConsumptionResponse> result = await _handler.Handle(
            new GetConsumptionQuery(Guid.NewGuid(), Guid.NewGuid(), null, "hour", null, null), CancellationToken.None);

        Assert.Equal(1.5m, Assert.Single(result.Value.Items).ConsumptionKwh);
    }
}
