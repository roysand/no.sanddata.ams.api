using Application.Common.Interfaces.Repositories;
using Domain.Common.Entities;

namespace Infrastructure.Database.Repositories;

public class UserLocationEfRepository : GenericEfRepository<UserLocation>, IUserLocationEfRepository<UserLocation>
{
    public UserLocationEfRepository(ApplicationDbContext applicationDbContext) : base(applicationDbContext)
    {
    }
}
