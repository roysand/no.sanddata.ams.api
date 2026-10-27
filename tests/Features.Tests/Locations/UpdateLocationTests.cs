using Application.Common.ApiKeys;
using Application.Common.Interfaces.Repositories;
using Domain.Common;
using Domain.Common.Entities;
using Features.Locations.Commands;
using Features.Locations.Endpoints;
using Features.Locations.Handlers;
using Features.Locations.Queries;
using Features.Locations.Validators;
using FluentValidation.Results;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Features.Tests.Locations;

public class UpdateLocationTests
{
    private readonly ILocationRepository<Location> _locations = Substitute.For<ILocationRepository<Location>>();
    private readonly IUserLocationRepository<UserLocation> _links = Substitute.For<IUserLocationRepository<UserLocation>>();
    private readonly UpdateLocationCommandHandler _handler;
    private readonly Location _location = new(Guid.NewGuid(), "Home", "Addr", "SN-1", "NO1", true, false);

    public UpdateLocationTests()
    {
        _links.GetForLocationsAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(Array.Empty<LocationUserInfo>());
        _handler = new UpdateLocationCommandHandler(_locations, _links, Substitute.For<ILogger<UpdateLocationCommandHandler>>());
        _location.AssignApiKey(new ApiKey(Guid.NewGuid(), ApiKeyCrypto.Hash("k"), "k", "Sensor key for Home", true,
            DateTime.UtcNow.AddDays(30)));
        _locations.GetByIdWithKeyAsync(_location.Id, Arg.Any<CancellationToken>()).Returns(_location);
    }

    private UpdateLocationCommand Command(
        string name = "Cabin", string serial = "SN-2", string zone = "NO3", bool norges = true, bool active = true) =>
        new(Guid.NewGuid(), _location.Id, name, "New address", serial, zone, norges, active);

    [Fact]
    public async Task Handle_ReturnsWhoHasAccessWithTheirRole()
    {
        var owner = Guid.NewGuid();
        _links.GetForLocationsAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns([new LocationUserInfo(_location.Id, owner, "owner@example.com", "Olga", "Owner", LocationRole.Owner)]);

        Result<AdminLocationResponse> result = await _handler.Handle(Command(), CancellationToken.None);

        LocationUserResponse user = Assert.Single(result.Value.Users);
        Assert.Equal(owner, user.UserId);
        Assert.Equal("Owner", user.Role);
    }

    [Fact]
    public async Task Handle_UpdatesDetailsAndLeavesTheKeyAlone()
    {
        string hashBefore = _location.ApiKey.KeyHash;

        Result<AdminLocationResponse> result = await _handler.Handle(Command(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("Cabin", _location.Name);
        Assert.Equal("New address", _location.Address);
        Assert.Equal("SN-2", _location.SerialNumber);
        Assert.Equal("NO3", _location.Zone);
        Assert.True(_location.HasNorgesPriceAgreement);
        Assert.Equal(hashBefore, _location.ApiKey.KeyHash);
        await _locations.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_CanDeactivateAndReactivate()
    {
        Result<AdminLocationResponse> off = await _handler.Handle(Command(active: false), CancellationToken.None);
        Assert.False(off.Value.IsActive);
        Assert.False(_location.IsActive);

        Result<AdminLocationResponse> on = await _handler.Handle(Command(active: true), CancellationToken.None);
        Assert.True(on.Value.IsActive);
    }

    [Fact]
    public async Task Handle_UnknownLocation_ReturnsNotFound()
    {
        UpdateLocationCommand command = Command() with { LocationId = Guid.NewGuid() };

        Result<AdminLocationResponse> result = await _handler.Handle(command, CancellationToken.None);

        Assert.Equal("Location.NotFound", result.Error.Code);
    }

    [Fact]
    public async Task Handle_SerialNumberOfAnotherLocation_ReturnsConflictAndSavesNothing()
    {
        _locations.SerialNumberExistsAsync("SN-2", _location.Id, Arg.Any<CancellationToken>()).Returns(true);

        Result<AdminLocationResponse> result = await _handler.Handle(Command(), CancellationToken.None);

        Assert.Equal("Location.SerialNumberExists", result.Error.Code);
        Assert.Equal("Home", _location.Name);
        await _locations.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_KeepingItsOwnSerialNumber_IsAllowed()
    {
        // The repository excludes the location itself, so its own serial number is not a conflict.
        _locations.SerialNumberExistsAsync("SN-1", _location.Id, Arg.Any<CancellationToken>()).Returns(false);

        Result<AdminLocationResponse> result = await _handler.Handle(Command(serial: "SN-1"), CancellationToken.None);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task Handle_ResponseCarriesKeyInfoButNoKey()
    {
        Result<AdminLocationResponse> result = await _handler.Handle(Command(), CancellationToken.None);

        Assert.Equal("Active", result.Value.ApiKey.Status);
        Assert.Equal("k", result.Value.ApiKey.Hint);
    }

    // Validators

    private static UpdateLocationRequest Request(string name = "Cabin", string zone = "NO1") =>
        new(Guid.NewGuid(), name, "Addr", "SN-1", zone, false, true);

    [Fact]
    public void Validator_ValidRequest_Passes() =>
        Assert.True(new UpdateLocationValidator().Validate(Request()).IsValid);

    [Theory]
    [InlineData("NO6")]
    [InlineData("")]
    public void Validator_BadZone_FailsWithTheZoneCode(string zone)
    {
        ValidationResult result = new UpdateLocationValidator().Validate(Request(zone: zone));

        Assert.Contains(result.Errors, e => e.ErrorCode == "Validation.InvalidZone");
    }

    [Fact]
    public void Validator_EmptyIdOrName_Fails()
    {
        Assert.False(new UpdateLocationValidator().Validate(Request() with { Id = Guid.Empty }).IsValid);
        Assert.False(new UpdateLocationValidator().Validate(Request(name: " ")).IsValid);
    }

    [Fact]
    public void KeyActiveValidator_RequiresALocationId()
    {
        Assert.False(new SetLocationKeyActiveValidator().Validate(new SetLocationKeyActiveRequest(Guid.Empty, true)).IsValid);
        Assert.True(new SetLocationKeyActiveValidator().Validate(new SetLocationKeyActiveRequest(Guid.NewGuid(), false)).IsValid);
    }
}
