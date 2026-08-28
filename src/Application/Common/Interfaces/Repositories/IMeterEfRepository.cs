using Domain.Common.Entities;

namespace Application.Common.Interfaces.Repositories;

public interface IMeterEfRepository<T> : IEfRepository<T> where T : class
{
    Task<Meter?> FindByDeviceIdAsync(Guid locationId, string deviceId, CancellationToken cancellationToken);
}
