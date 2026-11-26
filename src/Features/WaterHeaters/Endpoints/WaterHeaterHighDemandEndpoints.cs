using System.Security.Claims;
using Application.CQRS;
using Domain.Common;
using FastEndpoints;
using Features.WaterHeaters.Commands;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http;

namespace Features.WaterHeaters.Endpoints;

/// <summary>
/// The two High Demand switches (spec 022, contracts/endpoints.md): <c>PUT</c> turns it on and <c>DELETE</c> turns it
/// off. Admin only. The only input is the route's location id, read with <c>Route&lt;Guid&gt;</c> as in
/// <see cref="RemoveWaterHeaterEndpoint"/>, so there is no validator (research R5).
/// </summary>
internal static class HighDemandSwitch
{
    public const string Route = "/api/admin/locations/{locationId}/water-heater/high-demand";

    public static void Describe(EndpointSummary s, bool on)
    {
        s.Summary = on
            ? "Turn the water heater's High Demand on (admin)"
            : "Turn the water heater's High Demand off (admin)";
        s.Description = (on
                ? "Sends OSO's High Demand switch, which OSO keeps on for 24 hours. "
                : "Sends OSO's High Demand switch off; turning it off when it's off is not an error. ") +
            "The call is OSO's own but undocumented, and is sent once, never retried. The answer gives the heater's " +
            "state read back just after the switch (state.specialMode), or state null when that read failed; the " +
            "switch was made either way.";
        s.Response(200, "Switched");
        s.Response(401, "Not signed in");
        s.Response(403, "Administrator role required");
        s.Response(404, "Location not found (Location.NotFound), or no water heater connected " +
                        "(WaterHeater.NotConnected)");
        s.Response(502, "OSO rejected the stored key or device id (WaterHeater.Rejected), no longer offers " +
                        "High Demand (WaterHeater.HighDemandNotOffered), answered in an unexpected way " +
                        "(WaterHeater.InvalidResponse), or the stored key cannot be read (WaterHeater.KeyUnreadable)");
        s.Response(503, "OSO unavailable or no answer within 10 seconds (WaterHeater.Unavailable); whether the " +
                        "heater changed is unknown, so read the state");
    }
}

/// <summary>What both switches share: route, auth and sending the command.</summary>
public abstract class HighDemandSwitchEndpoint(IDispatcher dispatcher, bool on)
    : EndpointWithoutRequest<WaterHeaterHighDemandResponse>
{
    public override void Configure()
    {
        if (on)
        {
            Put(HighDemandSwitch.Route);
        }
        else
        {
            Delete(HighDemandSwitch.Route);
        }

        Tags("WaterHeater");
        Description(b => b.WithTags("WaterHeater"));
        AuthSchemes(JwtBearerDefaults.AuthenticationScheme);
        Roles(RoleNames.Admin);
        Summary(s => HighDemandSwitch.Describe(s, on));
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var userId = Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);

        Result<WaterHeaterHighDemandResponse> result = await dispatcher.Send(
            new SetWaterHeaterHighDemandCommand(userId, Route<Guid>("locationId"), on), ct);

        if (!result.IsSuccess)
        {
            AddError(result.Error.Description, result.Error.Code);
            ThrowIfAnyErrors(WaterHeaterErrorStatus.Of(result.Error, onConnect: false));
        }

        Response = result.Value;
    }
}

public class TurnOnWaterHeaterHighDemandEndpoint(IDispatcher dispatcher)
    : HighDemandSwitchEndpoint(dispatcher, on: true);

public class TurnOffWaterHeaterHighDemandEndpoint(IDispatcher dispatcher)
    : HighDemandSwitchEndpoint(dispatcher, on: false);
