using Application.Common.Interfaces.Repositories;
using Domain.Common.Entities;

namespace Infrastructure.Database.Repositories;

public class UserEfRepository : GenericEfRepository<User>, IUserEfRepository<User>
{
    public UserEfRepository(ApplicationDbContext applicationDbContext) : base(applicationDbContext)
    {
    }
}
