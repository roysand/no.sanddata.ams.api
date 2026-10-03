using Application.Common.Interfaces.Repositories;
using Domain.Common.Entities;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Database.Repositories;

public class LocationEfRepository : GenericEfRepository<Location>, ILocationRepository<Location>
{
    public LocationEfRepository(ApplicationDbContext applicationDbContext) : base(applicationDbContext)
    {
    }

    public async Task<bool> IsUserAssociatedAsync(Guid userId, Guid locationId, CancellationToken cancellationToken) =>
        await _context.Set<UserLocation>()
            .AnyAsync(ul => ul.UserId == userId && ul.LocationId == locationId, cancellationToken);

    public async Task<IReadOnlyList<Location>> GetForUserAsync(Guid userId, CancellationToken cancellationToken) =>
        await _context.Location
            .Where(l => l.Users.Any(u => u.Id == userId))
            .Include(l => l.Meters)
            .ToListAsync(cancellationToken);

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
