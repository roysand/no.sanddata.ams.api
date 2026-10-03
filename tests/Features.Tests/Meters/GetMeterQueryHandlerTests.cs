using Application.Common.Interfaces.Repositories;
using Domain.Common;
using Domain.Common.Entities;
using Features.Meters.Commands;
using Features.Meters.Handlers;
using Features.Meters.Queries;
using NSubstitute;

namespace Features.Tests.Meters;

public class GetMeterQueryHandlerTests
{
    private readonly IMeterRepository<Meter> _meters = Substitute.For<IMeterRepository<Meter>>();
    private readonly ILocationRepository<Location> _locations = Substitute.For<ILocationRepository<Location>>();
    private readonly GetMeterQueryHandler _handler;
    private readonly Meter _meter = new(Guid.NewGuid(), Guid.NewGuid(), "dev1", null, true);

    public GetMeterQueryHandlerTests()
    {
        _handler = new GetMeterQueryHandler(_meters, _locations);
        _meters.GetByIdAsync(_meter.Id, Arg.Any<CancellationToken>()).Returns(_meter);
    }

    [Fact]
    public async Task Handle_UserLinkedToTheMetersLocation_ReturnsMeter()
    {
        var userId = Guid.NewGuid();
        _locations.IsUserAssociatedAsync(userId, _meter.LocationId, Arg.Any<CancellationToken>()).Returns(true);

        Result<MeterResponse> result = await _handler.Handle(new GetMeterQuery(_meter.Id, userId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(_meter.Id, result.Value.Id);
    }

    [Fact]
    public async Task Handle_UserNotLinkedToTheLocation_LooksLikeAMissingMeter()
    {
        var userId = Guid.NewGuid();
        _locations.IsUserAssociatedAsync(userId, _meter.LocationId, Arg.Any<CancellationToken>()).Returns(false);

        Result<MeterResponse> notLinked = await _handler.Handle(new GetMeterQuery(_meter.Id, userId), CancellationToken.None);
        Result<MeterResponse> missing = await _handler.Handle(new GetMeterQuery(Guid.NewGuid(), userId), CancellationToken.None);

        Assert.Equal("Meter.NotFound", notLinked.Error.Code);
        Assert.Equal(missing.Error.Code, notLinked.Error.Code);
        Assert.Equal(missing.Error.Description, notLinked.Error.Description);
    }
}
