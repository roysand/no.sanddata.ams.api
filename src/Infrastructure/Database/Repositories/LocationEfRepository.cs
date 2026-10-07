using Application.Common.Interfaces.Repositories;
using Domain.Common.Entities;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Database.Repositories;

public class LocationEfRepository : GenericEfRepository<Location>, ILocationRepository<Location>
{
    public LocationEfRepository(ApplicationDbContext applicationDbContext) : base(applicationDbContext)
    {
    }

    // An inactive location behaves as nonexistent for regular users everywhere this check is used
    // (location list, measurements, consumption, cost, readers). Admin management does not use it.
    public async Task<bool> IsUserAssociatedAsync(Guid userId, Guid locationId, CancellationToken cancellationToken) =>
        await _context.Location
            .AnyAsync(l => l.Id == locationId && l.IsActive && l.Users.Any(u => u.Id == userId), cancellationToken);

    public async Task<IReadOnlyList<Location>> GetForUserAsync(Guid userId, CancellationToken cancellationToken) =>
        await _context.Location
            .Where(l => l.IsActive && l.Users.Any(u => u.Id == userId))
            .Include(l => l.Meters)
            .ToListAsync(cancellationToken);

    public async Task<Location?> GetByIdWithKeyAsync(Guid locationId, CancellationToken cancellationToken) =>
        await _context.Location
            .Include(l => l.ApiKey)
            .Include(l => l.Meters)
            .FirstOrDefaultAsync(l => l.Id == locationId, cancellationToken);

    public async Task<IReadOnlyList<Location>> GetAllWithKeyAsync(CancellationToken cancellationToken) =>
        await _context.Location
            .AsNoTracking()
            .Include(l => l.ApiKey)
            .Include(l => l.Meters)
            .OrderBy(l => l.Name)
            .ToListAsync(cancellationToken);

    public async Task<bool> SerialNumberExistsAsync(
        string serialNumber, Guid? exceptLocationId, CancellationToken cancellationToken) =>
        await _context.Location
            .AnyAsync(l => l.SerialNumber == serialNumber && l.Id != exceptLocationId, cancellationToken);
}
