using Domain.Common.Entities;

namespace Application.Common.Interfaces.Repositories;

public interface IApiKeyEfRepository<T> : IEfRepository<T> where T : class
{
    Task<ApiKey?> FindActiveByKeyAsync(string key, CancellationToken cancellationToken);
}
