using Application.Common.ApiKeys;
using Application.Common.Interfaces.Repositories;
using Domain.Common;
using Domain.Common.Entities;
using Features.Locations.Commands;
using Features.Locations.Handlers;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Features.Tests.Locations;

public class CreateLocationCommandHandlerTests
{
    private readonly ILocationRepository<Location> _locations = Substitute.For<ILocationRepository<Location>>();
    private readonly CreateLocationCommandHandler _handler;
    private Location? _inserted;

    public CreateLocationCommandHandlerTests()
    {
        _handler = new CreateLocationCommandHandler(_locations, Substitute.For<ILogger<CreateLocationCommandHandler>>());
        _locations.Insert(Arg.Do<Location>(l => _inserted = l));
    }

    private static CreateLocationCommand Command(string name = "Cabin", string serial = "SN-1", bool isActive = true) =>
        new(Guid.NewGuid(), name, "Hyttevegen 1", serial, "NO1", HasNorgesPriceAgreement: false, isActive);

    [Fact]
    public async Task Handle_CreatesTheLocationWithItsKeyInOneSave()
    {
        Result<CreatedLocationResponse> result = await _handler.Handle(Command(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.NotNull(_inserted);
        Assert.Equal("Cabin", _inserted.Name);
        Assert.NotNull(_inserted.ApiKey);
        await _locations.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ReturnsAKeyWhoseHashIsTheOneStored()
    {
        Result<CreatedLocationResponse> result = await _handler.Handle(Command(), CancellationToken.None);

        string plainKey = result.Value.ApiKey;
        Assert.Equal(64, plainKey.Length);
        Assert.Equal(ApiKeyCrypto.Hash(plainKey), _inserted!.ApiKey.KeyHash);
        Assert.Equal(plainKey[^4..], _inserted.ApiKey.KeyHint);
    }

    [Fact]
    public async Task Handle_NeverStoresThePlainKeyAnywhereOnTheEntities()
    {
        Result<CreatedLocationResponse> result = await _handler.Handle(Command(), CancellationToken.None);

        string plainKey = result.Value.ApiKey;
        ApiKey stored = _inserted!.ApiKey;
        Assert.DoesNotContain(plainKey, stored.KeyHash);
        Assert.DoesNotContain(plainKey, stored.KeyHint);
        Assert.DoesNotContain(plainKey, stored.Description);
        Assert.DoesNotContain(plainKey, _inserted.Name);
    }

    [Fact]
    public async Task Handle_KeyExpiresInAboutTwoYearsAndIsActive()
    {
        await _handler.Handle(Command(), CancellationToken.None);

        TimeSpan lifetime = _inserted!.ApiKey.ExpiresAt - DateTime.UtcNow;
        Assert.InRange(lifetime.TotalDays, 729, 731);
        Assert.True(_inserted.ApiKey.IsActive);
    }

    [Fact]
    public async Task Handle_DescriptionNamesTheLocation_AndIsTruncatedToTheColumnSize()
    {
        await _handler.Handle(Command(name: "Cabin"), CancellationToken.None);
        Assert.Equal("Sensor key for Cabin", _inserted!.ApiKey.Description);

        await _handler.Handle(Command(name: new string('x', 100), serial: "SN-2"), CancellationToken.None);
        Assert.Equal(100, _inserted.ApiKey.Description.Length);
    }

    [Fact]
    public async Task Handle_ResponseShowsKeyInfoButNoKey()
    {
        Result<CreatedLocationResponse> result = await _handler.Handle(Command(), CancellationToken.None);

        Assert.Equal("Active", result.Value.Location.ApiKey.Status);
        Assert.Equal(result.Value.ApiKey[^4..], result.Value.Location.ApiKey.Hint);
        Assert.Empty(result.Value.Location.Meters);
    }

    [Fact]
    public async Task Handle_CanCreateAnInactiveLocation()
    {
        Result<CreatedLocationResponse> result = await _handler.Handle(Command(isActive: false), CancellationToken.None);

        Assert.False(result.Value.Location.IsActive);
        Assert.False(_inserted!.IsActive);
    }

    [Fact]
    public async Task Handle_DuplicateSerialNumber_ReturnsConflictAndSavesNothing()
    {
        _locations.SerialNumberExistsAsync("SN-1", null, Arg.Any<CancellationToken>()).Returns(true);

        Result<CreatedLocationResponse> result = await _handler.Handle(Command(serial: "SN-1"), CancellationToken.None);

        Assert.Equal("Location.SerialNumberExists", result.Error.Code);
        Assert.Equal(ErrorType.Conflict, result.Error.Type);
        _locations.DidNotReceive().Insert(Arg.Any<Location>());
        await _locations.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }
}
