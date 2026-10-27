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
    private readonly IUserLocationRepository<UserLocation> _links = Substitute.For<IUserLocationRepository<UserLocation>>();
    private readonly CreateMeterCommandHandler _handler;
    private readonly Guid _userId = Guid.NewGuid();
    private readonly Guid _locationId = Guid.NewGuid();

    public CreateMeterCommandHandlerTests() =>
        _handler = new CreateMeterCommandHandler(_meters, _locations, _links, Substitute.For<ILogger<CreateMeterCommandHandler>>());

    /// <summary>Whether the user OWNS the location. A viewer is linked too, but may not register meters.</summary>
    private void LinkUser(bool isOwner) =>
        _links.IsOwnerAsync(_userId, _locationId, Arg.Any<CancellationToken>()).Returns(isOwner);

    private void LocationExists(bool exists) =>
        _locations.ExistsAsync(Arg.Any<Expression<Func<Location, bool>>>(), Arg.Any<CancellationToken>()).Returns(exists);

    private CreateMeterCommand Command(bool isAdmin = false) => new(_userId, isAdmin, _locationId, "dev1", "Main");

    [Fact]
    public async Task Handle_OwnerOfTheLocation_RegistersTheMeter()
    {
        LinkUser(true);
        _meters.ExistsAsync(Arg.Any<Expression<Func<Meter, bool>>>(), Arg.Any<CancellationToken>()).Returns(false);

        Result<MeterResponse> result = await _handler.Handle(Command(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        _meters.Received(1).Insert(Arg.Is<Meter>(m => m.LocationId == _locationId && m.DeviceId == "dev1"));
    }

    [Fact]
    public async Task Handle_ViewerOrStranger_ReturnsLocationNotFoundAndRegistersNothing()
    {
        LinkUser(false);
        LocationExists(true); // exists, but a viewer or stranger does not get to know that

        Result<MeterResponse> result = await _handler.Handle(Command(), CancellationToken.None);

        Assert.Equal("Location.NotFound", result.Error.Code);
        _meters.DidNotReceive().Insert(Arg.Any<Meter>());
    }

    [Fact]
    public async Task Handle_AdminWhoOwnsNothing_StillRegistersTheMeter()
    {
        LinkUser(false);
        LocationExists(true);

        Result<MeterResponse> result = await _handler.Handle(Command(isAdmin: true), CancellationToken.None);

        Assert.True(result.IsSuccess);
        _meters.Received(1).Insert(Arg.Any<Meter>());
        await _links.DidNotReceive().IsOwnerAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>());
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
