using Application.Common.Interfaces.Repositories;
using Domain.Common.Entities;

namespace Infrastructure.Database.Repositories;

public class RoleEfRepository : GenericEfRepository<Role>, IRoleEfRepository<Role>
{
    public RoleEfRepository(ApplicationDbContext applicationDbContext) : base(applicationDbContext)
    {
    }
}
