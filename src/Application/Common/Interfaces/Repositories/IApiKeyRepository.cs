using Domain.Common.Entities;

namespace Application.Common.Interfaces.Repositories;

public interface IApiKeyRepository<T> : IRepository<T> where T : class
{
    Task<ApiKey?> FindActiveByKeyAsync(string key, CancellationToken cancellationToken);
}
