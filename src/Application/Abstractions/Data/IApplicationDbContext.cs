using Domain.Common.Entities;
using Microsoft.EntityFrameworkCore;

namespace Application.Abstractions.Data;

public interface IApplicationDbContext
{
    DbSet<ApiKey> ApiKey { get; }
    DbSet<User> User { get; }
    DbSet<Location> Location { get; }
    DbSet<UserLocation> UserLocation { get; }
    DbSet<Role> Role { get; }
    DbSet<UserRole> UserRole { get; }
    DbSet<Measurement> Measurement { get; }
    DbSet<Meter> Meter { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}

