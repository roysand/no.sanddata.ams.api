using Application.Common.Interfaces.Repositories;
using Domain.Common.Entities;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Database.Repositories;

public class MeasurementEfRepository : GenericEfRepository<Measurement>, IMeasurementRepository<Measurement>
{
    public MeasurementEfRepository(ApplicationDbContext applicationDbContext) : base(applicationDbContext)
    {
    }

    public async Task<(IReadOnlyList<Measurement> Items, int TotalCount)> GetPagedAsync(
        Guid locationId, Guid? meterId, DateTime from, DateTime to, int page, int pageSize,
        CancellationToken cancellationToken)
    {
        IQueryable<Measurement> query = _context.Measurement
            .Where(m => m.LocationId == locationId && m.Timestamp >= from && m.Timestamp <= to);

        if (meterId is not null)
        {
            query = query.Where(m => m.MeterId == meterId);
        }

        int totalCount = await query.CountAsync(cancellationToken);

        List<Measurement> items = await query
            .OrderBy(m => m.Timestamp)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return (items, totalCount);
    }

    public async Task<Measurement?> GetLatestAsync(Guid locationId, Guid? meterId, CancellationToken cancellationToken)
    {
        IQueryable<Measurement> query = _context.Measurement.Where(m => m.LocationId == locationId);

        if (meterId is not null)
        {
            query = query.Where(m => m.MeterId == meterId);
        }

        return await query
            .OrderByDescending(m => m.Timestamp)
            .FirstOrDefaultAsync(cancellationToken);
    }
}
