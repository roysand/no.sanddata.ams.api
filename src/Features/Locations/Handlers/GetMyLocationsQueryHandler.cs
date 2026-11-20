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
    private readonly IWaterHeaterRepository<WaterHeater> _waterHeaterRepository;
    private readonly ILogger<GetMyLocationsQueryHandler> _logger;

    public GetMyLocationsQueryHandler(
        ILocationRepository<Location> locationRepository,
        IWaterHeaterRepository<WaterHeater> waterHeaterRepository,
        ILogger<GetMyLocationsQueryHandler> logger)
    {
        _locationRepository = locationRepository;
        _waterHeaterRepository = waterHeaterRepository;
        _logger = logger;
    }

    public async Task<Result<IReadOnlyList<LocationSummaryResponse>>> Handle(GetMyLocationsQuery query, CancellationToken ct)
    {
        IReadOnlyList<LocationWithRole> locations = await _locationRepository.GetForUserWithRoleAsync(query.UserId, ct);
        IReadOnlySet<Guid> withHeater = locations.Count == 0
            ? new HashSet<Guid>()
            : await _waterHeaterRepository.GetLocationIdsWithHeaterAsync(
                locations.Select(l => l.Location.Id).ToList(), ct);

        LogMessages.LocationsListed(_logger, query.UserId, locations.Count);

        return Result.Success<IReadOnlyList<LocationSummaryResponse>>(
            locations.Select(l => LocationMapper.ToResponse(l.Location, l.Role, withHeater.Contains(l.Location.Id)))
                .ToList());
    }
}
