using System.Linq.Expressions;
using Application.Common.Interfaces.Repositories;
using Domain.Common;
using Domain.Common.Entities;
using Features.Meters.Commands;
using Features.Meters.Handlers;
using NSubstitute;

namespace Features.Tests.Meters;

public class CreateMeterCommandHandlerTests
{
    private readonly IMeterRepository<Meter> _meters = Substitute.For<IMeterRepository<Meter>>();
    private readonly ILocationRepository<Location> _locations = Substitute.For<ILocationRepository<Location>>();
    private readonly CreateMeterCommandHandler _handler;
    private readonly Guid _userId = Guid.NewGuid();
    private readonly Guid _locationId = Guid.NewGuid();

    public CreateMeterCommandHandlerTests() => _handler = new CreateMeterCommandHandler(_meters, _locations);

    private void LinkUser(bool linked) =>
        _locations.IsUserAssociatedAsync(_userId, _locationId, Arg.Any<CancellationToken>()).Returns(linked);

    [Fact]
    public async Task Handle_LinkedUser_RegistersTheMeter()
    {
        LinkUser(true);
        _meters.ExistsAsync(Arg.Any<Expression<Func<Meter, bool>>>(), Arg.Any<CancellationToken>()).Returns(false);

        Result<MeterResponse> result = await _handler.Handle(
            new CreateMeterCommand(_userId, _locationId, "dev1", "Main"), CancellationToken.None);

        Assert.True(result.IsSuccess);
        _meters.Received(1).Insert(Arg.Is<Meter>(m => m.LocationId == _locationId && m.DeviceId == "dev1"));
    }

    [Fact]
    public async Task Handle_UserNotLinkedToTheLocation_ReturnsLocationNotFoundAndRegistersNothing()
    {
        LinkUser(false);

        Result<MeterResponse> result = await _handler.Handle(
            new CreateMeterCommand(_userId, _locationId, "dev1", null), CancellationToken.None);

        Assert.Equal("Location.NotFound", result.Error.Code);
        _meters.DidNotReceive().Insert(Arg.Any<Meter>());
    }

    [Fact]
    public async Task Handle_DuplicateDeviceId_ReturnsConflict()
    {
        LinkUser(true);
        _meters.ExistsAsync(Arg.Any<Expression<Func<Meter, bool>>>(), Arg.Any<CancellationToken>()).Returns(true);

        Result<MeterResponse> result = await _handler.Handle(
            new CreateMeterCommand(_userId, _locationId, "dev1", null), CancellationToken.None);

        Assert.Equal("Meter.DeviceIdExists", result.Error.Code);
        Assert.Equal(ErrorType.Conflict, result.Error.Type);
    }
}
