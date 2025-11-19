using Application.Common.Interfaces.Repositories;
using Domain.Common.Entities;

namespace Infrastructure.Database.Repositories;

public class UserRoleEfRepository : GenericEfRepository<UserRole>, IUserRoleEfRepository<UserRole>
{
    public UserRoleEfRepository(ApplicationDbContext applicationDbContext) : base(applicationDbContext)
    {
    }
}
