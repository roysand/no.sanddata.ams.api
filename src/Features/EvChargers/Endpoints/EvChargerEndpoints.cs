using System.Security.Claims;
using Application.CQRS;
using Domain.Common;
using FastEndpoints;
using Features.EvChargers.Commands;
using Features.EvChargers.Queries;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http;

namespace Features.EvChargers.Endpoints;

/// <param name="LocationId">The location, from the route.</param>
/// <param name="From">Sessions that ended at or after this time; 30 days before <paramref name="To"/> when left out.</param>
/// <param name="To">Sessions that ended before this time; now when left out.</param>
public record GetChargingSessionsRequest(Guid LocationId, DateTime? From, DateTime? To);

public class GetChargingSessionsEndpoint : Endpoint<GetChargingSessionsRequest, ChargingSessionsResponse>
{
    private readonly IDispatcher _dispatcher;

    public GetChargingSessionsEndpoint(IDispatcher dispatcher) => _dispatcher = dispatcher;

    public override void Configure()
    {
        Get("/api/locations/{locationId}/ev-charger/sessions");
        Tags("EvCharger");
        Description(b => b.WithTags("EvCharger"));
        AuthSchemes(JwtBearerDefaults.AuthenticationScheme);
        Summary(s =>
        {
            s.Summary = "List the location's completed charging sessions";
            s.Description = "Newest first, by end time. Zaptec gives completed sessions only, so a charge in progress " +
                             "appears once it has ended and been read (within 15 minutes). An owner, a viewer or an " +
                             "Admin may read them. The range is at most 366 days.";
            s.Response(200, "The sessions");
            s.Response(400, "from is not before to, or the range is longer than 366 days");
            s.Response(401, "Not signed in");
            s.Response(404, "Location not found");
        });
    }

    public override async Task HandleAsync(GetChargingSessionsRequest req, CancellationToken ct)
    {
        var userId = Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);

        Result<ChargingSessionsResponse> result = await _dispatcher.Send(
            new GetChargingSessionsQuery(userId, User.IsInRole(RoleNames.Admin), req.LocationId, req.From, req.To), ct);

        if (!result.IsSuccess)
        {
            AddError(result.Error.Description, result.Error.Code);
            ThrowIfAnyErrors(EvChargerErrorStatus.Of(result.Error, onConnect: false));
        }

        Response = result.Value;
    }
}

/// <param name="LocationId">The location, from the route.</param>
/// <param name="Include">Count the charger's kWh with the house's.</param>
/// <param name="LocationId">The location, from the route.</param>
public record GetChargerStateRequest(Guid LocationId);

public class GetChargerStateEndpoint : Endpoint<GetChargerStateRequest, ChargerStateResponse>
{
    private readonly IDispatcher _dispatcher;

    public GetChargerStateEndpoint(IDispatcher dispatcher) => _dispatcher = dispatcher;

    public override void Configure()
    {
        Get("/api/locations/{locationId}/ev-charger/state");
        Tags("EvCharger");
        Description(b => b.WithTags("EvCharger"));
        AuthSchemes(JwtBearerDefaults.AuthenticationScheme);
        Summary(s =>
        {
            s.Summary = "The location's car charger state right now";
            s.Description = "Online, the state (noCharger, offline, noCar, connected, charging, finished or " +
                             "unknown), the power while charging, the charge's kWh, and when Zaptec was read. Zaptec " +
                             "is read at most every 3 minutes per charger and the answer is shared (1 minute after a " +
                             "failure). The figures are never stored or counted in any usage, cost or bill. An " +
                             "owner, a viewer or an Admin may read it.";
            s.Response(200, "The state, including noCharger when no charger is connected");
            s.Response(400, "locationId is empty");
            s.Response(401, "Not signed in");
            s.Response(404, "Not linked to the location, or no such location (Location.NotFound)");
            s.Response(502, "Zaptec answered in a way the API can't read (EvCharger.InvalidResponse)");
            s.Response(503, "Zaptec failed, rate-limited, took more than 3 s, rejected the server's login, or no " +
                             "login is set up (EvCharger.Unavailable, SignInRejected, NotConfigured)");
        });
    }

    public override async Task HandleAsync(GetChargerStateRequest req, CancellationToken ct)
    {
        var userId = Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);

        Result<ChargerStateResponse> result = await _dispatcher.Send(
            new GetChargerStateQuery(userId, User.IsInRole(RoleNames.Admin), req.LocationId), ct);

        if (!result.IsSuccess)
        {
            AddError(result.Error.Description, result.Error.Code);
            ThrowIfAnyErrors(EvChargerErrorStatus.Of(result.Error, onConnect: false));
        }

        Response = result.Value;
    }
}

public record SetIncludeEvChargerRequest(Guid LocationId, bool? Include);

public class SetIncludeEvChargerEndpoint : Endpoint<SetIncludeEvChargerRequest, EvChargerIncludeResponse>
{
    private readonly IDispatcher _dispatcher;

    public SetIncludeEvChargerEndpoint(IDispatcher dispatcher) => _dispatcher = dispatcher;

    public override void Configure()
    {
        Put("/api/locations/{locationId}/ev-charger/include");
        Tags("EvCharger");
        Description(b => b.WithTags("EvCharger"));
        AuthSchemes(JwtBearerDefaults.AuthenticationScheme);
        Summary(s =>
        {
            s.Summary = "Count the EV charger with the house, or not (the checkbox)";
            s.Description = "When on, the hourly, current-hour, daily and bill figures add the charger's kWh to " +
                             "totalKwh. consumptionKwh and every cost stay the house's own. An owner of the location " +
                             "or an Admin may change it.";
            s.Response(200, "Saved");
            s.Response(400, "include missing, or turning it on with no charger ever connected " +
                            "(EvCharger.NotConnected)");
            s.Response(401, "Not signed in");
            s.Response(403, "A viewer of the location (EvCharger.Forbidden)");
            s.Response(404, "Location not found");
        });
    }

    public override async Task HandleAsync(SetIncludeEvChargerRequest req, CancellationToken ct)
    {
        var userId = Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);

        Result<EvChargerIncludeResponse> result = await _dispatcher.Send(
            new SetIncludeEvChargerCommand(userId, User.IsInRole(RoleNames.Admin), req.LocationId, req.Include!.Value), ct);

        if (!result.IsSuccess)
        {
            AddError(result.Error.Description, result.Error.Code);
            // Turning it on without a charger is the caller's request that can't be met (400), not a missing resource.
            int status = result.Error.Code == "EvCharger.NotConnected"
                ? 400
                : EvChargerErrorStatus.Of(result.Error, onConnect: false);
            ThrowIfAnyErrors(status);
        }

        Response = result.Value;
    }
}
