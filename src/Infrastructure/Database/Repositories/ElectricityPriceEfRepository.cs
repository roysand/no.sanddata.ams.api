using Application.Common.Interfaces.Repositories;
using Domain.Common.Entities;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Database.Repositories;

public class ElectricityPriceEfRepository : GenericEfRepository<ElectricityPrice>, IElectricityPriceRepository<ElectricityPrice>
{
    public ElectricityPriceEfRepository(ApplicationDbContext applicationDbContext) : base(applicationDbContext)
    {
    }

    public async Task<ElectricityPrice?> GetByRegionAndHourAsync(string priceRegion, DateTime hourStartUtc, CancellationToken cancellationToken) =>
        await _context.ElectricityPrice
            .FirstOrDefaultAsync(p => p.PriceRegion == priceRegion && p.HourStartUtc == hourStartUtc, cancellationToken);

    public async Task<IReadOnlyList<ElectricityPrice>> GetByRegionAndHourRangeAsync(
        string priceRegion, DateTime fromUtc, DateTime toUtc, CancellationToken cancellationToken) =>
        await _context.ElectricityPrice
            .Where(p => p.PriceRegion == priceRegion && p.HourStartUtc >= fromUtc && p.HourStartUtc < toUtc)
            .ToListAsync(cancellationToken);
}
