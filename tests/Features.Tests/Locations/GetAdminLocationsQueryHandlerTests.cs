using Application.Common.ApiKeys;
using Application.Common.Interfaces.Repositories;
using Domain.Common;
using Domain.Common.Entities;
using Features.Locations.Handlers;
using Features.Locations.Queries;
using NSubstitute;

namespace Features.Tests.Locations;

public class GetAdminLocationsQueryHandlerTests
{
    private readonly ILocationRepository<Location> _locations = Substitute.For<ILocationRepository<Location>>();
    private readonly IUserLocationRepository<UserLocation> _links = Substitute.For<IUserLocationRepository<UserLocation>>();
    private readonly GetAdminLocationsQueryHandler _handler;

    public GetAdminLocationsQueryHandlerTests()
    {
        _handler = new GetAdminLocationsQueryHandler(_locations, _links);
        _links.GetForLocationsAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(Array.Empty<LocationUserInfo>());
    }

    private static Location LocationWithKey(string name, bool keyActive, DateTime expiresAt, bool locationActive = true)
    {
        var location = new Location(Guid.NewGuid(), name, "Addr", $"SN-{name}", "NO1", locationActive, false);
        location.AssignApiKey(new ApiKey(Guid.NewGuid(), ApiKeyCrypto.Hash(name), "abcd", $"Sensor key for {name}", keyActive, expiresAt));
        return location;
    }

    private async Task<IReadOnlyList<AdminLocationResponse>> RunAsync(params Location[] locations)
    {
        _locations.GetAllWithKeyAsync(Arg.Any<CancellationToken>()).Returns(locations);
        Result<IReadOnlyList<AdminLocationResponse>> result =
            await _handler.Handle(new GetAdminLocationsQuery(Guid.NewGuid()), CancellationToken.None);
        return result.Value;
    }

    [Fact]
    public async Task Handle_ReturnsEveryLocationEvenIfInactive()
    {
        IReadOnlyList<AdminLocationResponse> list = await RunAsync(
            LocationWithKey("A", true, DateTime.UtcNow.AddDays(10)),
            LocationWithKey("B", true, DateTime.UtcNow.AddDays(10), locationActive: false));

        Assert.Equal(2, list.Count);
        Assert.Contains(list, l => !l.IsActive);
    }

    [Fact]
    public async Task Handle_ListsWhoHasAccessToEachLocationWithTheirRole()
    {
        Location shared = LocationWithKey("Shared", true, DateTime.UtcNow.AddDays(10));
        Location alone = LocationWithKey("Alone", true, DateTime.UtcNow.AddDays(10));
        var owner = Guid.NewGuid();
        var viewer = Guid.NewGuid();
        _links.GetForLocationsAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns([
                new LocationUserInfo(shared.Id, owner, "owner@example.com", "Olga", "Owner", LocationRole.Owner),
                new LocationUserInfo(shared.Id, viewer, "viewer@example.com", "Vera", "Viewer", LocationRole.Viewer)
            ]);

        IReadOnlyList<AdminLocationResponse> list = await RunAsync(shared, alone);

        AdminLocationResponse sharedResponse = list.Single(l => l.Id == shared.Id);
        Assert.Equal(2, sharedResponse.Users.Count);
        Assert.Equal("Owner", sharedResponse.Users.Single(u => u.UserId == owner).Role);
        Assert.Equal("Viewer", sharedResponse.Users.Single(u => u.UserId == viewer).Role);
        Assert.Equal("viewer@example.com", sharedResponse.Users.Single(u => u.UserId == viewer).Email);
        Assert.Empty(list.Single(l => l.Id == alone.Id).Users);
    }

    [Fact]
    public async Task Handle_AsksForEveryonesAccessInOneQuery()
    {
        await RunAsync(
            LocationWithKey("A", true, DateTime.UtcNow.AddDays(10)),
            LocationWithKey("B", true, DateTime.UtcNow.AddDays(10)));

        await _links.Received(1).GetForLocationsAsync(
            Arg.Is<IReadOnlyCollection<Guid>>(ids => ids.Count == 2), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ComputesKeyStatus()
    {
        IReadOnlyList<AdminLocationResponse> list = await RunAsync(
            LocationWithKey("Active", true, DateTime.UtcNow.AddDays(10)),
            LocationWithKey("Expired", true, DateTime.UtcNow.AddDays(-1)),
            LocationWithKey("Off", false, DateTime.UtcNow.AddDays(10)),
            LocationWithKey("OffAndExpired", false, DateTime.UtcNow.AddDays(-1)));

        Assert.Equal("Active", list.Single(l => l.Name == "Active").ApiKey.Status);
        Assert.Equal("Expired", list.Single(l => l.Name == "Expired").ApiKey.Status);
        Assert.Equal("Deactivated", list.Single(l => l.Name == "Off").ApiKey.Status);
        Assert.Equal("Deactivated", list.Single(l => l.Name == "OffAndExpired").ApiKey.Status);
    }

    [Fact]
    public async Task Handle_ShowsOnlyNonSecretKeyInformation()
    {
        IReadOnlyList<AdminLocationResponse> list = await RunAsync(LocationWithKey("A", true, DateTime.UtcNow.AddDays(10)));

        ApiKeyInfoResponse key = list[0].ApiKey;
        Assert.Equal("abcd", key.Hint);
        Assert.Equal("Sensor key for A", key.Description);
    }

    [Fact]
    public void ResponseTypes_HaveNoPropertyThatCouldCarryAKeyOrItsHash()
    {
        string[] names = typeof(ApiKeyInfoResponse).GetProperties()
            .Concat(typeof(AdminLocationResponse).GetProperties())
            .Select(p => p.Name).ToArray();

        Assert.DoesNotContain(names, n => n.Contains("Hash", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(names, n => n.Equals("Key", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(names, n => n.Equals("PlainKey", StringComparison.OrdinalIgnoreCase));
    }
}
