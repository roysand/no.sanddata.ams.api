using Application.Common.Interfaces.Repositories;
using Application.CQRS;
using Domain.Common;
using Domain.Common.Entities;
using Features.Locations.Logging;
using Features.Locations.Mappers;
using Features.Locations.Queries;
using Microsoft.Extensions.Logging;

namespace Features.Locations.Handlers;

public class GetMyLocationsQueryHandler : IQueryHandler<GetMyLocationsQuery, Result<IReadOnlyList<LocationSummaryResponse>>>
{
    private readonly ILocationRepository<Location> _locationRepository;
    private readonly ILogger<GetMyLocationsQueryHandler> _logger;

    public GetMyLocationsQueryHandler(ILocationRepository<Location> locationRepository, ILogger<GetMyLocationsQueryHandler> logger)
    {
        _locationRepository = locationRepository;
        _logger = logger;
    }

    public async Task<Result<IReadOnlyList<LocationSummaryResponse>>> Handle(GetMyLocationsQuery query, CancellationToken ct)
    {
        IReadOnlyList<Location> locations = await _locationRepository.GetForUserAsync(query.UserId, ct);

        LogMessages.LocationsListed(_logger, query.UserId, locations.Count);

        return Result.Success<IReadOnlyList<LocationSummaryResponse>>(locations.Select(LocationMapper.ToResponse).ToList());
    }
}
