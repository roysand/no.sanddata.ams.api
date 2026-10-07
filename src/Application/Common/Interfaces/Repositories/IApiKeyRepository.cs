using Domain.Common.Entities;

namespace Application.Common.Interfaces.Repositories;

public interface IApiKeyRepository<T> : IRepository<T> where T : class
{
    /// <summary>
    /// The key whose fingerprint matches, if it is active, not expired and its location is active.
    /// The Location is loaded.
    /// </summary>
    Task<ApiKey?> FindActiveByKeyHashAsync(string keyHash, CancellationToken cancellationToken);

    /// <summary>The key of a location, tracked for update (used to rotate or switch it on/off).</summary>
    Task<ApiKey?> FindByLocationIdAsync(Guid locationId, CancellationToken cancellationToken);
}
