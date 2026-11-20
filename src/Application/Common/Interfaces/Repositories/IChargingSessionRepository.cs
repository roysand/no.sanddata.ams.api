using Application.EvChargers;
using Domain.Common.Entities;

namespace Application.Common.Interfaces.Repositories;

/// <summary>A location's charging sessions and their kWh per hour (spec 016).</summary>
public interface IChargingSessionRepository
{
    /// <summary>
    /// Replaces each session (by Zaptec id) and its hours, and deletes the ones marked Discard. Does not save: the
    /// caller saves once, together with the charger's new watermark.
    /// </summary>
    Task UpsertAsync(
        Guid locationId, Guid chargerId, IReadOnlyList<ChargingSessionToStore> sessions, CancellationToken cancellationToken);

    /// <summary>The location's charger kWh per UTC hour in [from, to), ordered by hour; hours without charging are absent.</summary>
    Task<IReadOnlyList<EvChargerHourEnergy>> GetHourlyEnergyAsync(
        Guid locationId, DateTime fromUtc, DateTime toUtc, CancellationToken cancellationToken);

    /// <summary>Sessions that ended in [from, to), newest first, not tracked.</summary>
    Task<IReadOnlyList<ChargingSession>> GetSessionsAsync(
        Guid locationId, DateTime fromUtc, DateTime toUtc, CancellationToken cancellationToken);

    Task<bool> AnyForLocationAsync(Guid locationId, CancellationToken cancellationToken);

    Task<int> SaveChangesAsync(CancellationToken cancellationToken);
}
