using Application.CQRS;
using Domain.Common;

namespace Features.EvChargers.Queries;

/// <summary>The chargers the server's Zaptec sign-in can see (Admin). Nothing is stored.</summary>
public sealed record ListZaptecChargersQuery : IQuery<Result<IReadOnlyList<ZaptecChargerResponse>>>;

public sealed record ZaptecChargerResponse(Guid ChargerId, string Name, Guid InstallationId, string InstallationName);

public sealed record GetEvChargerSettingsQuery(Guid LocationId) : IQuery<Result<EvChargerSettingsResponse>>;

/// <summary>The connection as an Admin sees it. It holds no secret: the Zaptec sign-in is in the server's settings.</summary>
/// <param name="ReadUpTo">The end of the last successful read of Zaptec; null before the first.</param>
/// <param name="IncludeInTotals">The location's "count the charger with the house" setting.</param>
public sealed record EvChargerSettingsResponse(
    Guid LocationId,
    Guid ChargerId,
    string ChargerName,
    Guid InstallationId,
    string InstallationName,
    DateTime ConnectedAt,
    DateTime? ReadUpTo,
    bool IncludeInTotals);

/// <summary>A location's completed charging sessions that ended in [From, To) (spec 016, FR-015).</summary>
public sealed record GetChargingSessionsQuery(Guid UserId, bool IsAdmin, Guid LocationId, DateTime? From, DateTime? To)
    : IQuery<Result<ChargingSessionsResponse>>;

/// <param name="Estimated">True when Zaptec gave no per-hour energy and the hours were spread evenly.</param>
public sealed record ChargingSessionResponse(DateTime Start, DateTime End, decimal Kwh, bool Estimated);

public sealed record ChargingSessionsResponse(IReadOnlyList<ChargingSessionResponse> Items);

/// <summary>
/// A location's car charger state right now (spec 021), for an owner, a viewer or an Admin, as the session list.
/// </summary>
public sealed record GetChargerStateQuery(Guid UserId, bool IsAdmin, Guid LocationId)
    : IQuery<Result<ChargerStateResponse>>;

/// <summary>
/// The charger's state right now (spec 021, contracts/endpoints.md). Read from Zaptec at most every 3 minutes and
/// shared; never stored, and never added to any usage, cost, bill or Norgespris figure.
/// </summary>
/// <param name="State">noCharger, offline, noCar, connected, charging, finished or unknown.</param>
/// <param name="Online">Null only with noCharger.</param>
/// <param name="PowerKw">Only while charging.</param>
/// <param name="SessionKwh">The charge's kWh so far: connected, charging, finished or unknown.</param>
/// <param name="PluggedInAt">When the car was plugged in: connected, charging or finished.</param>
/// <param name="ChargingSince">When charging last started (after a pause, when it resumed): charging only.</param>
/// <param name="LastReportedAt">The charger's last check-in.</param>
/// <param name="ReadAt">When Zaptec was read.</param>
public sealed record ChargerStateResponse(
    string State,
    bool? Online,
    decimal? PowerKw,
    decimal? SessionKwh,
    DateTime? PluggedInAt,
    DateTime? ChargingSince,
    DateTime? LastReportedAt,
    DateTime? ReadAt);
