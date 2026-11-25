using Application.EvChargers;
using Domain.Common;

namespace Application.Common.Interfaces.External;

/// <summary>
/// Reads a Zaptec charger through Zaptec's cloud API with the server's own sign-in (spec 016). Read only on purpose
/// (FR-017): there is no method that changes the charger. Never throws for HTTP, timeout or parsing failures; they
/// come back as <see cref="EvChargerErrors"/> values.
/// </summary>
public interface IEvChargerClient
{
    /// <summary>False when the server has no Zaptec sign-in configured; every call then fails with NotConfigured.</summary>
    bool IsConfigured { get; }

    /// <summary>The chargers the sign-in can see (first 100).</summary>
    Task<Result<IReadOnlyList<ZaptecCharger>>> GetChargersAsync(CancellationToken cancellationToken);

    /// <summary>Completed sessions of the charger that ended in [<paramref name="fromUtc"/>, <paramref name="toUtc"/>).</summary>
    Task<Result<IReadOnlyList<ZaptecSession>>> GetCompletedSessionsAsync(
        Guid chargerId, DateTime fromUtc, DateTime toUtc, CancellationToken cancellationToken);

    /// <summary>
    /// The charger's latest reported values (spec 021, research R4). Gives up after 3 seconds and doesn't wait and
    /// retry after a 429 (both become <see cref="EvChargerErrors.Unavailable"/>); signs in again once after a 401.
    /// </summary>
    Task<Result<ZaptecChargerState>> GetStateAsync(Guid chargerId, CancellationToken cancellationToken);
}
