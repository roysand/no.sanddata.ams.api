using Application.CQRS;
using Domain.Common;
using Features.EvChargers.Queries;

namespace Features.EvChargers.Commands;

/// <summary>Connects or replaces the charger of a location, by its Zaptec charger id (spec 016, US1).</summary>
public sealed record ConnectEvChargerCommand(Guid ActingUserId, Guid LocationId, Guid ChargerId)
    : ICommand<Result<EvChargerSettingsResponse>>;

/// <summary>Removes the connection; stored sessions are kept and the "count with the house" setting turns off.</summary>
public sealed record RemoveEvChargerCommand(Guid ActingUserId, Guid LocationId) : ICommand<Result<EvChargerRemoved>>;

public sealed record EvChargerRemoved(Guid LocationId);

/// <summary>The checkbox: count the charger's kWh with the house's (spec 016, US3). Owner of the location or Admin.</summary>
public sealed record SetIncludeEvChargerCommand(Guid ActingUserId, bool IsAdmin, Guid LocationId, bool Include)
    : ICommand<Result<EvChargerIncludeResponse>>;

public sealed record EvChargerIncludeResponse(Guid LocationId, bool IncludeInTotals);
