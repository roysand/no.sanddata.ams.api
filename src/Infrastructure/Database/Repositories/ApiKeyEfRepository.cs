using Application.Common.Interfaces.Repositories;
using Domain.Common.Entities;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Database.Repositories;

public class ApiKeyEfRepository : GenericEfRepository<ApiKey>, IApiKeyEfRepository<ApiKey>
{
    public ApiKeyEfRepository(ApplicationDbContext applicationDbContext) : base(applicationDbContext)
    {
    }

    public async Task<ApiKey?> FindActiveByKeyAsync(string key, CancellationToken cancellationToken) =>
        await _context.Set<ApiKey>()
            .Include(a => a.Location)
            .FirstOrDefaultAsync(a => a.Key == key && a.IsActive && a.ExpiresAt > DateTime.UtcNow, cancellationToken);
}
