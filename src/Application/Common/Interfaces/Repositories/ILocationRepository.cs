using Domain.Common;
using Domain.Common.Entities;

namespace Application.Common.Interfaces.Repositories;

/// <summary>A location together with the role the asking user has there.</summary>
public record LocationWithRole(Location Location, LocationRole Role);

public interface ILocationRepository<T> : IRepository<T> where T : class
{
    Task<bool> IsUserAssociatedAsync(Guid userId, Guid locationId, CancellationToken cancellationToken);

    /// <summary>
    /// The user's locations with their role and readers. Owners get every location they own, active or not
    /// (so they can switch it on again); viewers only get active ones.
    /// </summary>
    Task<IReadOnlyList<LocationWithRole>> GetForUserWithRoleAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>How many locations (active or not) the user owns. Viewing a shared location does not count.</summary>
    Task<int> CountOwnedForUserAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>Every location (active or not) with its key and readers, for administrators.</summary>
    Task<IReadOnlyList<Location>> GetAllWithKeyAsync(CancellationToken cancellationToken);

    /// <summary>One location with its key and readers, tracked for update (admin editing).</summary>
    Task<Location?> GetByIdWithKeyAsync(Guid locationId, CancellationToken cancellationToken);

    /// <summary>True if another location (not <paramref name="exceptLocationId"/>) already uses the serial number.</summary>
    Task<bool> SerialNumberExistsAsync(string serialNumber, Guid? exceptLocationId, CancellationToken cancellationToken);
}
