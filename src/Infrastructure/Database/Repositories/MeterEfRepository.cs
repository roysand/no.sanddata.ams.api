using Application.Common.Interfaces.Repositories;
using Domain.Common.Entities;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Database.Repositories;

public class MeterEfRepository : GenericEfRepository<Meter>, IMeterEfRepository<Meter>
{
    public MeterEfRepository(ApplicationDbContext applicationDbContext) : base(applicationDbContext)
    {
    }

    public async Task<Meter?> FindByDeviceIdAsync(Guid locationId, string deviceId, CancellationToken cancellationToken) =>
        await _context.Set<Meter>()
            .FirstOrDefaultAsync(m => m.LocationId == locationId && m.DeviceId == deviceId && m.IsActive, cancellationToken);
}
