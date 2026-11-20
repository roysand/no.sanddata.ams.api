using Application.Common.Interfaces.Repositories;
using Domain.Common.Entities;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Database.Repositories;

public class EvChargerEfRepository : GenericEfRepository<EvCharger>, IEvChargerRepository<EvCharger>
{
    public EvChargerEfRepository(ApplicationDbContext applicationDbContext) : base(applicationDbContext)
    {
    }

    public async Task<EvCharger?> GetByLocationAsync(Guid locationId, CancellationToken cancellationToken) =>
        await _context.EvCharger.FirstOrDefaultAsync(c => c.LocationId == locationId, cancellationToken);

    public async Task<EvCharger?> GetByChargerIdAsync(Guid chargerId, CancellationToken cancellationToken) =>
        await _context.EvCharger.FirstOrDefaultAsync(c => c.ChargerId == chargerId, cancellationToken);

    public async Task<IReadOnlySet<Guid>> GetLocationIdsWithChargerAsync(
        IReadOnlyCollection<Guid> locationIds, CancellationToken cancellationToken) =>
        (await _context.EvCharger
            .AsNoTracking()
            .Where(c => locationIds.Contains(c.LocationId))
            .Select(c => c.LocationId)
            .ToListAsync(cancellationToken))
        .ToHashSet();

    public async Task<IReadOnlyList<EvCharger>> GetForActiveLocationsAsync(CancellationToken cancellationToken) =>
        await _context.EvCharger
            .Where(c => _context.Location.Any(l => l.Id == c.LocationId && l.IsActive))
            .ToListAsync(cancellationToken);
}
