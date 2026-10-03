using Application.Common.Interfaces.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Database.Repositories;

public class ConsumptionEfRepository : IConsumptionRepository
{
    private readonly ApplicationDbContext _context;

    public ConsumptionEfRepository(ApplicationDbContext context) => _context = context;

    public async Task<IReadOnlyList<MinuteConsumption>> GetMinuteAsync(
        Guid locationId, Guid? meterId, DateTime fromUtc, DateTime toUtc, CancellationToken cancellationToken)
    {
        IQueryable<MinuteConsumption> query = _context.Set<MinuteConsumption>()
            .Where(m => m.LocationId == locationId && m.BucketStart >= fromUtc && m.BucketStart < toUtc);

        if (meterId is not null)
        {
            query = query.Where(m => m.MeterId == meterId);
        }

        return await query.OrderBy(m => m.BucketStart).ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<HourConsumption>> GetHourlyAsync(
        Guid locationId, Guid? meterId, DateTime fromUtc, DateTime toUtc, CancellationToken cancellationToken)
    {
        IQueryable<HourConsumption> query = _context.Set<HourConsumption>()
            .Where(h => h.LocationId == locationId && h.BucketStart >= fromUtc && h.BucketStart < toUtc);

        if (meterId is not null)
        {
            query = query.Where(h => h.MeterId == meterId);
        }

        return await query.OrderBy(h => h.BucketStart).ToListAsync(cancellationToken);
    }

    public async Task<decimal> GetHourConsumptionKwhAsync(Guid locationId, DateTime hourStartUtc, CancellationToken cancellationToken)
    {
        List<HourConsumption> rows = await _context.Set<HourConsumption>()
            .Where(h => h.LocationId == locationId && h.BucketStart == hourStartUtc)
            .ToListAsync(cancellationToken);

        return rows.Sum(h => h.ConsumptionKwh);
    }
}
