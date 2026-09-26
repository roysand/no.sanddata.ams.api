using Application.Common.Interfaces.Repositories;
using Domain.Common.Entities;

namespace Infrastructure.Database.Repositories;

public class UserEfRepository : GenericEfRepository<User>, IUserRepository<User>
{
    public UserEfRepository(ApplicationDbContext applicationDbContext) : base(applicationDbContext)
    {
    }
}
