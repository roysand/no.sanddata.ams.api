using Application.Common.Interfaces.Repositories;
using Domain.Common.Entities;

namespace Infrastructure.Database.Repositories;

public class LocationEfRepository : GenericEfRepository<Location>, ILocationRepository<Location>
{
    public LocationEfRepository(ApplicationDbContext applicationDbContext) : base(applicationDbContext)
    {
    }
}
