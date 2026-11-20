using System.Security.Claims;
using Application.CQRS;
using Domain.Common;
using FastEndpoints;
using Features.EvChargers.Commands;
using Features.EvChargers.Queries;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http;

namespace Features.EvChargers.Endpoints;

/// <summary>HTTP status of an EV charger error (spec 016, contracts/endpoints.md).</summary>
public static class EvChargerErrorStatus
{
    /// <param name="error">The failed result's error.</param>
    /// <param name="onConnect">
    /// True on connect, where a rejected sign-in makes the request impossible to carry out (400); elsewhere it is a
    /// server-side problem with its settings (503).
    /// </param>
    public static int Of(Error error, bool onConnect) => error.Code switch
    {
        "EvCharger.SignInRejected" => onConnect ? 400 : 503,
        "EvCharger.InvalidResponse" => 502,
        "EvCharger.Unavailable" or "EvCharger.NotConfigured" => 503,
        "EvCharger.Forbidden" => 403,
        _ => error.Type switch
        {
            ErrorType.NotFound => 404,
            ErrorType.Conflict => 409,
            _ => 400
        }
    };
}

public class ListZaptecChargersEndpoint : EndpointWithoutRequest<IReadOnlyList<ZaptecChargerResponse>>
{
    private readonly IDispatcher _dispatcher;

    public ListZaptecChargersEndpoint(IDispatcher dispatcher) => _dispatcher = dispatcher;

    public override void Configure()
    {
        Get("/api/admin/ev-chargers");
        Tags("EvCharger");
        Description(b => b.WithTags("EvCharger"));
        AuthSchemes(JwtBearerDefaults.AuthenticationScheme);
        Roles(RoleNames.Admin);
        Summary(s =>
        {
            s.Summary = "List the Zaptec chargers the server's sign-in can see (admin)";
            s.Description = "Asks Zaptec with the login set up on the server (Zaptec__Username / Zaptec__Password). " +
                             "Nothing is stored; pick a chargerId from here to connect it to a location.";
            s.Response(200, "The chargers");
            s.Response(401, "Not signed in");
            s.Response(403, "Administrator role required");
            s.Response(502, "Zaptec answered in an unexpected way (EvCharger.InvalidResponse)");
            s.Response(503, "No Zaptec sign-in on the server (EvCharger.NotConfigured), Zaptec rejected it " +
                            "(EvCharger.SignInRejected), or Zaptec is unavailable (EvCharger.Unavailable)");
        });
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        Result<IReadOnlyList<ZaptecChargerResponse>> result = await _dispatcher.Send(new ListZaptecChargersQuery(), ct);

        if (!result.IsSuccess)
        {
            AddError(result.Error.Description, result.Error.Code);
            ThrowIfAnyErrors(EvChargerErrorStatus.Of(result.Error, onConnect: false));
        }

        Response = result.Value;
    }
}

/// <param name="LocationId">The location, from the route.</param>
/// <param name="ChargerId">The Zaptec charger id, from <c>GET /api/admin/ev-chargers</c>.</param>
public record ConnectEvChargerRequest(Guid LocationId, Guid ChargerId);

public class ConnectEvChargerEndpoint : Endpoint<ConnectEvChargerRequest, EvChargerSettingsResponse>
{
    private readonly IDispatcher _dispatcher;

    public ConnectEvChargerEndpoint(IDispatcher dispatcher) => _dispatcher = dispatcher;

    public override void Configure()
    {
        Put("/api/admin/locations/{locationId}/ev-charger");
        Tags("EvCharger");
        Description(b => b.WithTags("EvCharger"));
        AuthSchemes(JwtBearerDefaults.AuthenticationScheme);
        Roles(RoleNames.Admin);
        Summary(s =>
        {
            s.Summary = "Connect a Zaptec charger to a location, or replace it (admin)";
            s.Description = "Checks with Zaptec that the server's sign-in can see the charger before saving. Only the " +
                             "charger's and installation's ids and names are stored. Connecting another charger " +
                             "replaces the old one and reads its history back to the start of the previous month.";
            s.Response(200, "Connected");
            s.Response(400, "Missing chargerId, Zaptec does not know the charger (EvCharger.UnknownCharger), or Zaptec " +
                            "rejected the server's sign-in (EvCharger.SignInRejected)");
            s.Response(401, "Not signed in");
            s.Response(403, "Administrator role required");
            s.Response(404, "Location not found");
            s.Response(409, "The charger is connected to another location (EvCharger.ChargerInUse)");
            s.Response(502, "Zaptec answered in an unexpected way (EvCharger.InvalidResponse)");
            s.Response(503, "No Zaptec sign-in on the server (EvCharger.NotConfigured) or Zaptec unavailable " +
                            "(EvCharger.Unavailable)");
        });
    }

    public override async Task HandleAsync(ConnectEvChargerRequest req, CancellationToken ct)
    {
        var userId = Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);

        Result<EvChargerSettingsResponse> result =
            await _dispatcher.Send(new ConnectEvChargerCommand(userId, req.LocationId, req.ChargerId), ct);

        if (!result.IsSuccess)
        {
            AddError(result.Error.Description, result.Error.Code);
            ThrowIfAnyErrors(EvChargerErrorStatus.Of(result.Error, onConnect: true));
        }

        Response = result.Value;
    }
}

public class GetEvChargerSettingsEndpoint : EndpointWithoutRequest<EvChargerSettingsResponse>
{
    private readonly IDispatcher _dispatcher;

    public GetEvChargerSettingsEndpoint(IDispatcher dispatcher) => _dispatcher = dispatcher;

    public override void Configure()
    {
        Get("/api/admin/locations/{locationId}/ev-charger");
        Tags("EvCharger");
        Description(b => b.WithTags("EvCharger"));
        AuthSchemes(JwtBearerDefaults.AuthenticationScheme);
        Roles(RoleNames.Admin);
        Summary(s =>
        {
            s.Summary = "Read the EV charger connection of a location (admin)";
            s.Description = "The charger and installation, when it was connected, up to when charging data was last " +
                             "read, and the 'count with the house' setting. Does not call Zaptec.";
            s.Response(200, "Connection found");
            s.Response(401, "Not signed in");
            s.Response(403, "Administrator role required");
            s.Response(404, "Location not found, or no charger connected (EvCharger.NotConnected)");
        });
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        Result<EvChargerSettingsResponse> result =
            await _dispatcher.Send(new GetEvChargerSettingsQuery(Route<Guid>("locationId")), ct);

        if (!result.IsSuccess)
        {
            AddError(result.Error.Description, result.Error.Code);
            ThrowIfAnyErrors(EvChargerErrorStatus.Of(result.Error, onConnect: false));
        }

        Response = result.Value;
    }
}

public class RemoveEvChargerEndpoint : EndpointWithoutRequest
{
    private readonly IDispatcher _dispatcher;

    public RemoveEvChargerEndpoint(IDispatcher dispatcher) => _dispatcher = dispatcher;

    public override void Configure()
    {
        Delete("/api/admin/locations/{locationId}/ev-charger");
        Tags("EvCharger");
        Description(b => b.WithTags("EvCharger"));
        AuthSchemes(JwtBearerDefaults.AuthenticationScheme);
        Roles(RoleNames.Admin);
        Summary(s =>
        {
            s.Summary = "Remove the EV charger connection of a location (admin)";
            s.Description = "Stops reading the charger. Charging sessions already stored are kept, and the location's " +
                             "'count with the house' setting is turned off.";
            s.Response(204, "Removed");
            s.Response(401, "Not signed in");
            s.Response(403, "Administrator role required");
            s.Response(404, "Location not found, or no charger connected (EvCharger.NotConnected)");
        });
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var userId = Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);

        Result<EvChargerRemoved> result =
            await _dispatcher.Send(new RemoveEvChargerCommand(userId, Route<Guid>("locationId")), ct);

        if (!result.IsSuccess)
        {
            AddError(result.Error.Description, result.Error.Code);
            ThrowIfAnyErrors(EvChargerErrorStatus.Of(result.Error, onConnect: false));
        }

        HttpContext.Response.StatusCode = StatusCodes.Status204NoContent;
    }
}
