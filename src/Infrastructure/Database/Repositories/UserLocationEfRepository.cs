using Application.Common.Interfaces.Repositories;
using Domain.Common;
using Domain.Common.Entities;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Database.Repositories;

public class UserLocationEfRepository : GenericEfRepository<UserLocation>, IUserLocationRepository<UserLocation>
{
    public UserLocationEfRepository(ApplicationDbContext applicationDbContext) : base(applicationDbContext)
    {
    }

    public async Task<bool> IsOwnerAsync(Guid userId, Guid locationId, CancellationToken cancellationToken) =>
        await _context.UserLocation.AnyAsync(
            ul => ul.UserId == userId && ul.LocationId == locationId && ul.Role == LocationRole.Owner,
            cancellationToken);

    public async Task<int> CountOwnersAsync(Guid locationId, CancellationToken cancellationToken) =>
        await _context.UserLocation.CountAsync(
            ul => ul.LocationId == locationId && ul.Role == LocationRole.Owner, cancellationToken);

    public async Task<IReadOnlyList<UserLinkInfo>> GetForUsersAsync(
        IReadOnlyCollection<Guid> userIds, CancellationToken cancellationToken) =>
        await _context.UserLocation
            .AsNoTracking()
            .Where(ul => userIds.Contains(ul.UserId))
            .Join(_context.Location, ul => ul.LocationId, l => l.Id,
                (ul, l) => new UserLinkInfo(ul.UserId, ul.LocationId, l.Name, ul.Role))
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<LocationUserInfo>> GetForLocationsAsync(
        IReadOnlyCollection<Guid> locationIds, CancellationToken cancellationToken) =>
        await _context.UserLocation
            .AsNoTracking()
            .Where(ul => locationIds.Contains(ul.LocationId))
            .Join(_context.User, ul => ul.UserId, u => u.Id,
                (ul, u) => new LocationUserInfo(
                    ul.LocationId, u.Id, u.Email.Value, u.FirstName, u.LastName, ul.Role))
            .ToListAsync(cancellationToken);
}
