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

public class LocationKeyHandlerTests
{
    private readonly IApiKeyRepository<ApiKey> _keys = Substitute.For<IApiKeyRepository<ApiKey>>();
    private readonly Guid _locationId = Guid.NewGuid();
    private readonly Guid _admin = Guid.NewGuid();
    private readonly string _oldKey = ApiKeyCrypto.Generate().Key;
    private readonly ApiKey _stored;

    public LocationKeyHandlerTests()
    {
        _stored = new ApiKey(Guid.NewGuid(), ApiKeyCrypto.Hash(_oldKey), ApiKeyCrypto.Hint(_oldKey), "Sensor key for Home", true,
            DateTime.UtcNow.AddDays(30));
        _keys.FindByLocationIdAsync(_locationId, Arg.Any<CancellationToken>()).Returns(_stored);
    }

    private RotateLocationKeyCommandHandler Rotate() =>
        new(_keys, Substitute.For<ILogger<RotateLocationKeyCommandHandler>>());

    private SetLocationKeyActiveCommandHandler SetActive() =>
        new(_keys, Substitute.For<ILogger<SetLocationKeyActiveCommandHandler>>());

    [Fact]
    public async Task Rotate_ReplacesTheFingerprintSoTheOldKeyNoLongerMatches()
    {
        Result<RotatedKeyResponse> result =
            await Rotate().Handle(new RotateLocationKeyCommand(_admin, _locationId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.NotEqual(ApiKeyCrypto.Hash(_oldKey), _stored.KeyHash);
        Assert.Equal(ApiKeyCrypto.Hash(result.Value.ApiKey), _stored.KeyHash);
        Assert.Equal(result.Value.ApiKey[^4..], _stored.KeyHint);
        Assert.Equal(result.Value.Hint, _stored.KeyHint);
        await _keys.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Rotate_RestartsTheTwoYearLifetimeAndReactivatesADeactivatedKey()
    {
        _stored.SetActive(false);

        Result<RotatedKeyResponse> result =
            await Rotate().Handle(new RotateLocationKeyCommand(_admin, _locationId), CancellationToken.None);

        Assert.True(_stored.IsActive);
        Assert.InRange((_stored.ExpiresAt - DateTime.UtcNow).TotalDays, 729, 731);
        Assert.Equal(_stored.ExpiresAt, result.Value.ExpiresAt);
    }

    [Fact]
    public async Task Rotate_NewKeyIsNeverStoredInReadableForm()
    {
        Result<RotatedKeyResponse> result =
            await Rotate().Handle(new RotateLocationKeyCommand(_admin, _locationId), CancellationToken.None);

        Assert.DoesNotContain(result.Value.ApiKey, _stored.KeyHash);
        Assert.DoesNotContain(result.Value.ApiKey, _stored.KeyHint);
        Assert.DoesNotContain(result.Value.ApiKey, _stored.Description);
    }

    [Fact]
    public async Task Rotate_UnknownLocation_ReturnsNotFoundAndSavesNothing()
    {
        Result<RotatedKeyResponse> result =
            await Rotate().Handle(new RotateLocationKeyCommand(_admin, Guid.NewGuid()), CancellationToken.None);

        Assert.Equal("Location.NotFound", result.Error.Code);
        await _keys.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SetActive_Deactivate_ChangesOnlyTheFlag()
    {
        string hashBefore = _stored.KeyHash;

        Result<ApiKeyInfoResponse> result = await SetActive().Handle(
            new SetLocationKeyActiveCommand(_admin, _locationId, false), CancellationToken.None);

        Assert.False(_stored.IsActive);
        Assert.Equal(hashBefore, _stored.KeyHash);
        Assert.Equal("Deactivated", result.Value.Status);
        await _keys.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SetActive_Reactivate_RestoresTheSameKey()
    {
        _stored.SetActive(false);

        Result<ApiKeyInfoResponse> result = await SetActive().Handle(
            new SetLocationKeyActiveCommand(_admin, _locationId, true), CancellationToken.None);

        Assert.True(_stored.IsActive);
        Assert.Equal("Active", result.Value.Status);
    }

    [Fact]
    public async Task SetActive_AlreadyInThatState_DoesNotSave()
    {
        Result<ApiKeyInfoResponse> result = await SetActive().Handle(
            new SetLocationKeyActiveCommand(_admin, _locationId, true), CancellationToken.None);

        Assert.True(result.IsSuccess);
        await _keys.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SetActive_UnknownLocation_ReturnsNotFound()
    {
        Result<ApiKeyInfoResponse> result = await SetActive().Handle(
            new SetLocationKeyActiveCommand(_admin, Guid.NewGuid(), false), CancellationToken.None);

        Assert.Equal("Location.NotFound", result.Error.Code);
    }
}
