using Domain.Common.Entities;

namespace Application.Common.Interfaces.Repositories;

public interface IMeterRepository<T> : IRepository<T> where T : class
{
    Task<Meter?> FindByDeviceIdAsync(Guid locationId, string deviceId, CancellationToken cancellationToken);
}
