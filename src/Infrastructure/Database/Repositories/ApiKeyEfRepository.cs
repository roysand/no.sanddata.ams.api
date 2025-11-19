using Application.Common.Interfaces.Repositories;
using Domain.Common.Entities;

namespace Infrastructure.Database.Repositories;

public class ApiKeyEfRepository : GenericEfRepository<ApiKey>, IApiKeyEfRepository<ApiKey>
{
    public ApiKeyEfRepository(ApplicationDbContext applicationDbContext) : base(applicationDbContext)
    {
    }
}
