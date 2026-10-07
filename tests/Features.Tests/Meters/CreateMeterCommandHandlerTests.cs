using System.Linq.Expressions;
using Application.Common.Interfaces.Repositories;
using Domain.Common;
using Domain.Common.Entities;
using Features.Meters.Commands;
using Features.Meters.Handlers;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Features.Tests.Meters;

public class CreateMeterCommandHandlerTests
{
    private readonly IMeterRepository<Meter> _meters = Substitute.For<IMeterRepository<Meter>>();
    private readonly ILocationRepository<Location> _locations = Substitute.For<ILocationRepository<Location>>();
    private readonly CreateMeterCommandHandler _handler;
    private readonly Guid _userId = Guid.NewGuid();
    private readonly Guid _locationId = Guid.NewGuid();

    public CreateMeterCommandHandlerTests() =>
        _handler = new CreateMeterCommandHandler(_meters, _locations, Substitute.For<ILogger<CreateMeterCommandHandler>>());

    private void LinkUser(bool linked) =>
        _locations.IsUserAssociatedAsync(_userId, _locationId, Arg.Any<CancellationToken>()).Returns(linked);

    private void LocationExists(bool exists) =>
        _locations.ExistsAsync(Arg.Any<Expression<Func<Location, bool>>>(), Arg.Any<CancellationToken>()).Returns(exists);

    private CreateMeterCommand Command(bool isAdmin = false) => new(_userId, isAdmin, _locationId, "dev1", "Main");

    [Fact]
    public async Task Handle_LinkedUser_RegistersTheMeter()
    {
        LinkUser(true);
        _meters.ExistsAsync(Arg.Any<Expression<Func<Meter, bool>>>(), Arg.Any<CancellationToken>()).Returns(false);

        Result<MeterResponse> result = await _handler.Handle(Command(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        _meters.Received(1).Insert(Arg.Is<Meter>(m => m.LocationId == _locationId && m.DeviceId == "dev1"));
    }

    [Fact]
    public async Task Handle_UserNotLinkedToTheLocation_ReturnsLocationNotFoundAndRegistersNothing()
    {
        LinkUser(false);
        LocationExists(true); // exists, but a regular user does not get to know that

        Result<MeterResponse> result = await _handler.Handle(Command(), CancellationToken.None);

        Assert.Equal("Location.NotFound", result.Error.Code);
        _meters.DidNotReceive().Insert(Arg.Any<Meter>());
    }

    [Fact]
    public async Task Handle_AdminNotLinkedToTheLocation_StillRegistersTheMeter()
    {
        LinkUser(false);
        LocationExists(true);

        Result<MeterResponse> result = await _handler.Handle(Command(isAdmin: true), CancellationToken.None);

        Assert.True(result.IsSuccess);
        _meters.Received(1).Insert(Arg.Any<Meter>());
        await _locations.DidNotReceive().IsUserAssociatedAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_AdminForAnUnknownLocation_ReturnsLocationNotFound()
    {
        LocationExists(false);

        Result<MeterResponse> result = await _handler.Handle(Command(isAdmin: true), CancellationToken.None);

        Assert.Equal("Location.NotFound", result.Error.Code);
        _meters.DidNotReceive().Insert(Arg.Any<Meter>());
    }

    [Fact]
    public async Task Handle_DuplicateDeviceId_ReturnsConflict()
    {
        LinkUser(true);
        _meters.ExistsAsync(Arg.Any<Expression<Func<Meter, bool>>>(), Arg.Any<CancellationToken>()).Returns(true);

        Result<MeterResponse> result = await _handler.Handle(Command(), CancellationToken.None);

        Assert.Equal("Meter.DeviceIdExists", result.Error.Code);
        Assert.Equal(ErrorType.Conflict, result.Error.Type);
    }

    [Fact]
    public async Task Handle_AdminDuplicateDeviceId_ReturnsConflict()
    {
        LocationExists(true);
        _meters.ExistsAsync(Arg.Any<Expression<Func<Meter, bool>>>(), Arg.Any<CancellationToken>()).Returns(true);

        Result<MeterResponse> result = await _handler.Handle(Command(isAdmin: true), CancellationToken.None);

        Assert.Equal("Meter.DeviceIdExists", result.Error.Code);
    }
}
