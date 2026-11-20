using Domain.Common.Entities;

namespace Application.Common.Interfaces.Repositories;

public interface IEvChargerRepository<T> : IRepository<T> where T : class
{
    /// <summary>The charger connected to the location, tracked; null when none is connected.</summary>
    Task<EvCharger?> GetByLocationAsync(Guid locationId, CancellationToken cancellationToken);

    /// <summary>The connection of this Zaptec charger, tracked; null when it is not connected.</summary>
    Task<EvCharger?> GetByChargerIdAsync(Guid chargerId, CancellationToken cancellationToken);

    /// <summary>Which of these locations have a charger connected, so the location list can say so.</summary>
    Task<IReadOnlySet<Guid>> GetLocationIdsWithChargerAsync(
        IReadOnlyCollection<Guid> locationIds, CancellationToken cancellationToken);

    /// <summary>Every charger whose location is active, tracked: the read job moves their watermark.</summary>
    Task<IReadOnlyList<EvCharger>> GetForActiveLocationsAsync(CancellationToken cancellationToken);
}
