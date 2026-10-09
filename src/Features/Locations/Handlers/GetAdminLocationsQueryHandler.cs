using Application.Common.Interfaces.Repositories;
using Application.CQRS;
using Domain.Common;
using Domain.Common.Entities;
using Features.Locations.Mappers;
using Features.Locations.Queries;

namespace Features.Locations.Handlers;

public class GetAdminLocationsQueryHandler(
    ILocationRepository<Location> locationRepository,
    IUserLocationRepository<UserLocation> userLocationRepository)
    : IQueryHandler<GetAdminLocationsQuery, Result<IReadOnlyList<AdminLocationResponse>>>
{
    public async Task<Result<IReadOnlyList<AdminLocationResponse>>> Handle(GetAdminLocationsQuery query, CancellationToken ct)
    {
        IReadOnlyList<Location> locations = await locationRepository.GetAllWithKeyAsync(ct);
        // One query for everyone's access, grouped by location here.
        IReadOnlyList<LocationUserInfo> access = await userLocationRepository.GetForLocationsAsync(
            locations.Select(l => l.Id).ToList(), ct);
        ILookup<Guid, LocationUserInfo> accessByLocation = access.ToLookup(a => a.LocationId);
        DateTime now = DateTime.UtcNow;

        return Result.Success<IReadOnlyList<AdminLocationResponse>>(
            locations.Select(l => LocationMapper.ToAdminResponse(l, now, accessByLocation[l.Id])).ToList());
    }
}
