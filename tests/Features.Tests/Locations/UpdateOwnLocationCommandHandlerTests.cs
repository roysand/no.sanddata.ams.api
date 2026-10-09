using Application.Common.ApiKeys;
using Application.Common.Interfaces.Repositories;
using Domain.Common;
using Domain.Common.Entities;
using Features.Locations.Commands;
using Features.Locations.Handlers;
using Features.Locations.Queries;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Features.Tests.Locations;

public class UpdateOwnLocationCommandHandlerTests
{
    private readonly ILocationRepository<Location> _locations = Substitute.For<ILocationRepository<Location>>();
    private readonly IUserLocationRepository<UserLocation> _links = Substitute.For<IUserLocationRepository<UserLocation>>();
    private readonly UpdateOwnLocationCommandHandler _handler;
    private readonly Guid _userId = Guid.NewGuid();
    private readonly Location _location = new(Guid.NewGuid(), "Home", "Old address", "SN-1", "NO3", true, true);

    public UpdateOwnLocationCommandHandlerTests()
    {
        _handler = new UpdateOwnLocationCommandHandler(
            _locations, _links, Substitute.For<ILogger<UpdateOwnLocationCommandHandler>>());
        _location.AssignApiKey(new ApiKey(Guid.NewGuid(), ApiKeyCrypto.Hash("k"), "k", "Sensor key for Home", true,
            DateTime.UtcNow.AddDays(30)));
        _locations.GetByIdWithKeyAsync(_location.Id, Arg.Any<CancellationToken>()).Returns(_location);
    }

    private void Owns(bool isOwner) =>
        _links.IsOwnerAsync(_userId, _location.Id, Arg.Any<CancellationToken>()).Returns(isOwner);

    private UpdateOwnLocationCommand Command(string name = "Cabin", string address = "New address", bool isActive = true) =>
        new(_userId, _location.Id, name, address, isActive);

    [Fact]
    public async Task Handle_Owner_ChangesNameAddressAndActiveFlag()
    {
        Owns(true);

        Result<LocationSummaryResponse> result = await _handler.Handle(Command(isActive: false), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("Cabin", _location.Name);
        Assert.Equal("New address", _location.Address);
        Assert.False(_location.IsActive);
        Assert.Equal("Cabin", result.Value.Name);
        Assert.False(result.Value.IsActive);
        Assert.Equal("Owner", result.Value.Role);
        await _locations.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_Owner_NeverTouchesTheSystemFieldsOrTheKey()
    {
        Owns(true);
        string hashBefore = _location.ApiKey.KeyHash;

        await _handler.Handle(Command(name: "Anything", address: "Anywhere", isActive: false), CancellationToken.None);

        Assert.Equal("SN-1", _location.SerialNumber);
        Assert.Equal("NO3", _location.Zone);
        Assert.True(_location.HasNorgesPriceAgreement);
        Assert.Equal(hashBefore, _location.ApiKey.KeyHash);
    }

    [Fact]
    public async Task Handle_Owner_CanSwitchAnInactiveLocationOnAgain()
    {
        Owns(true);
        _location.SetActive(false);

        Result<LocationSummaryResponse> result = await _handler.Handle(Command(isActive: true), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(_location.IsActive);
    }

    [Fact]
    public async Task Handle_ViewerOrStranger_GetsNotFound_AndNothingIsLoadedOrSaved()
    {
        Owns(false);

        Result<LocationSummaryResponse> result = await _handler.Handle(Command(), CancellationToken.None);

        Assert.Equal("Location.NotFound", result.Error.Code);
        Assert.Equal(ErrorType.NotFound, result.Error.Type);
        Assert.Equal("Home", _location.Name);
        await _locations.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_MissingLocation_GetsTheSameAnswerAsAViewer()
    {
        var other = Guid.NewGuid();
        _links.IsOwnerAsync(_userId, other, Arg.Any<CancellationToken>()).Returns(true);
        _locations.GetByIdWithKeyAsync(other, Arg.Any<CancellationToken>()).Returns((Location?)null);

        Result<LocationSummaryResponse> result = await _handler.Handle(Command() with { LocationId = other }, CancellationToken.None);

        Assert.Equal("Location.NotFound", result.Error.Code);
    }

    [Fact]
    public void Command_HasNoMemberThatCouldCarryASystemField()
    {
        string[] names = typeof(UpdateOwnLocationCommand).GetProperties().Select(p => p.Name).ToArray();

        Assert.DoesNotContain("SerialNumber", names);
        Assert.DoesNotContain("Zone", names);
        Assert.DoesNotContain("HasNorgesPriceAgreement", names);
        Assert.DoesNotContain(names, n => n.Contains("Key", StringComparison.OrdinalIgnoreCase));
    }
}
