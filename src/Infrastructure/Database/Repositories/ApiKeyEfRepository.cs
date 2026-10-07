using Application.Common.Interfaces.Repositories;
using Domain.Common.Entities;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Database.Repositories;

public class ApiKeyEfRepository : GenericEfRepository<ApiKey>, IApiKeyRepository<ApiKey>
{
    public ApiKeyEfRepository(ApplicationDbContext applicationDbContext) : base(applicationDbContext)
    {
    }

    public async Task<ApiKey?> FindActiveByKeyHashAsync(string keyHash, CancellationToken cancellationToken) =>
        await _context.Set<ApiKey>()
            .Include(a => a.Location)
            .FirstOrDefaultAsync(
                a => a.KeyHash == keyHash && a.IsActive && a.ExpiresAt > DateTime.UtcNow && a.Location.IsActive,
                cancellationToken);

    public async Task<ApiKey?> FindByLocationIdAsync(Guid locationId, CancellationToken cancellationToken) =>
        await _context.Set<ApiKey>()
            .FirstOrDefaultAsync(a => a.Location.Id == locationId, cancellationToken);
}
