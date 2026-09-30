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

    [Fact]
    public async Task Handle_UserWithNoLocations_ReturnsEmptyList()
    {
        _locationRepository.GetForUserAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(Array.Empty<Location>());
        var query = new GetMyLocationsQuery(Guid.NewGuid());

        Result<IReadOnlyList<LocationSummaryResponse>> result = await _handler.Handle(query, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value);
    }

    [Fact]
    public async Task Handle_UserWithLocations_ReturnsThemMappedWithMeters()
    {
        var location = new Location(Guid.NewGuid(), "Home", "Test address", "SN-1", "NO3", true, false);
        _locationRepository.GetForUserAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns([location]);
        var query = new GetMyLocationsQuery(Guid.NewGuid());

        Result<IReadOnlyList<LocationSummaryResponse>> result = await _handler.Handle(query, CancellationToken.None);

        Assert.True(result.IsSuccess);
        LocationSummaryResponse summary = Assert.Single(result.Value);
        Assert.Equal(location.Id, summary.Id);
        Assert.Equal("Home", summary.Name);
        Assert.Empty(summary.Meters);
    }
}
