using Domain.Common.Entities;

namespace Application.Common.Interfaces.Repositories;

public interface ILocationRepository<T> : IRepository<T> where T : class
{
    Task<bool> IsUserAssociatedAsync(Guid userId, Guid locationId, CancellationToken cancellationToken);

    Task<IReadOnlyList<Location>> GetForUserAsync(Guid userId, CancellationToken cancellationToken);
}
