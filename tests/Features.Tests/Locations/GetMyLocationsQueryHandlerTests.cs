using Application.Common.Interfaces.Repositories;
using Domain.Common;
using Domain.Common.Entities;
using Features.Locations.Handlers;
using Features.Locations.Queries;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Features.Tests.Locations;

public class GetMyLocationsQueryHandlerTests
{
    private readonly ILocationRepository<Location> _locationRepository = Substitute.For<ILocationRepository<Location>>();
    private readonly GetMyLocationsQueryHandler _handler;

    public GetMyLocationsQueryHandlerTests() => _handler = new GetMyLocationsQueryHandler(_locationRepository, Substitute.For<ILogger<GetMyLocationsQueryHandler>>());

    private void Returns(params LocationWithRole[] locations) =>
        _locationRepository.GetForUserWithRoleAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(locations);

    [Fact]
    public async Task Handle_UserWithNoLocations_ReturnsEmptyList()
    {
        Returns();

        Result<IReadOnlyList<LocationSummaryResponse>> result =
            await _handler.Handle(new GetMyLocationsQuery(Guid.NewGuid()), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value);
    }

    [Fact]
    public async Task Handle_UserWithLocations_ReturnsThemMappedWithMetersAndTheFactsTheEditScreenNeeds()
    {
        var location = new Location(Guid.NewGuid(), "Home", "Test address", "SN-1", "NO3", true, true);
        Returns(new LocationWithRole(location, LocationRole.Owner));

        Result<IReadOnlyList<LocationSummaryResponse>> result =
            await _handler.Handle(new GetMyLocationsQuery(Guid.NewGuid()), CancellationToken.None);

        Assert.True(result.IsSuccess);
        LocationSummaryResponse summary = Assert.Single(result.Value);
        Assert.Equal(location.Id, summary.Id);
        Assert.Equal("Home", summary.Name);
        Assert.Equal("SN-1", summary.SerialNumber);
        Assert.Equal("NO3", summary.Zone);
        Assert.True(summary.HasNorgesPriceAgreement);
        Assert.True(summary.IsActive);
        Assert.Equal("Owner", summary.Role);
        Assert.Empty(summary.Meters);
    }

    [Fact]
    public async Task Handle_SharedLocation_IsReturnedWithTheViewerRole()
    {
        var shared = new Location(Guid.NewGuid(), "Shared", "Addr", "SN-2", "NO1", true, false);
        Returns(new LocationWithRole(shared, LocationRole.Viewer));

        Result<IReadOnlyList<LocationSummaryResponse>> result =
            await _handler.Handle(new GetMyLocationsQuery(Guid.NewGuid()), CancellationToken.None);

        Assert.Equal("Viewer", Assert.Single(result.Value).Role);
    }

    [Fact]
    public async Task Handle_InactiveLocationOfAnOwner_ShowsAsInactive()
    {
        // The repository returns it because the user owns it; the response says it is switched off.
        var inactive = new Location(Guid.NewGuid(), "Off", "Addr", "SN-3", "NO1", false, false);
        Returns(new LocationWithRole(inactive, LocationRole.Owner));

        Result<IReadOnlyList<LocationSummaryResponse>> result =
            await _handler.Handle(new GetMyLocationsQuery(Guid.NewGuid()), CancellationToken.None);

        LocationSummaryResponse summary = Assert.Single(result.Value);
        Assert.False(summary.IsActive);
        Assert.Equal("Owner", summary.Role);
    }

    [Fact]
    public void Response_HasNoPropertyThatCouldCarryAKeyOrItsHash()
    {
        string[] names = typeof(LocationSummaryResponse).GetProperties().Select(p => p.Name).ToArray();

        Assert.DoesNotContain(names, n => n.Contains("Key", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(names, n => n.Contains("Hash", StringComparison.OrdinalIgnoreCase));
    }
}
