using Domain.Common.Entities;
using Features.Locations.Queries;
using Features.Meters.Mappers;

namespace Features.Locations.Mappers;

public static class LocationMapper
{
    public static LocationSummaryResponse ToResponse(Location location) =>
        new(location.Id, location.Name, location.Address, location.Zone,
            location.Meters.Select(MeterMapper.ToResponse).ToList());
}
