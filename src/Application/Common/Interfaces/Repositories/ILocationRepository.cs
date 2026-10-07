using Domain.Common.Entities;

namespace Application.Common.Interfaces.Repositories;

public interface ILocationRepository<T> : IRepository<T> where T : class
{
    Task<bool> IsUserAssociatedAsync(Guid userId, Guid locationId, CancellationToken cancellationToken);

    Task<IReadOnlyList<Location>> GetForUserAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>Every location (active or not) with its key and readers, for administrators.</summary>
    Task<IReadOnlyList<Location>> GetAllWithKeyAsync(CancellationToken cancellationToken);

    /// <summary>One location with its key and readers, tracked for update (admin editing).</summary>
    Task<Location?> GetByIdWithKeyAsync(Guid locationId, CancellationToken cancellationToken);

    /// <summary>True if another location (not <paramref name="exceptLocationId"/>) already uses the serial number.</summary>
    Task<bool> SerialNumberExistsAsync(string serialNumber, Guid? exceptLocationId, CancellationToken cancellationToken);
}
