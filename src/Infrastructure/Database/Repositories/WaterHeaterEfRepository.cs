using Application.Common.Interfaces.Repositories;
using Domain.Common.Entities;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Database.Repositories;

public class WaterHeaterEfRepository : GenericEfRepository<WaterHeater>, IWaterHeaterRepository<WaterHeater>
{
    public WaterHeaterEfRepository(ApplicationDbContext applicationDbContext) : base(applicationDbContext)
    {
    }

    public async Task<WaterHeater?> GetByLocationAsync(Guid locationId, CancellationToken cancellationToken) =>
        await _context.WaterHeater.FirstOrDefaultAsync(w => w.LocationId == locationId, cancellationToken);

    public async Task<WaterHeater?> GetByDeviceIdAsync(string deviceId, CancellationToken cancellationToken)
    {
        string lowered = deviceId.ToLowerInvariant();
        return await _context.WaterHeater.FirstOrDefaultAsync(w => w.DeviceId == lowered, cancellationToken);
    }

    public async Task<IReadOnlySet<Guid>> GetLocationIdsWithHeaterAsync(
        IReadOnlyCollection<Guid> locationIds, CancellationToken cancellationToken) =>
        (await _context.WaterHeater
            .AsNoTracking()
            .Where(w => locationIds.Contains(w.LocationId))
            .Select(w => w.LocationId)
            .ToListAsync(cancellationToken))
        .ToHashSet();

    public async Task<IReadOnlyList<WaterHeater>> GetForActiveLocationsAsync(CancellationToken cancellationToken) =>
        await _context.WaterHeater
            .AsNoTracking()
            .Where(w => _context.Location.Any(l => l.Id == w.LocationId && l.IsActive))
            .ToListAsync(cancellationToken);
}
