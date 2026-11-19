using System.Security.Claims;
using Application.CQRS;
using Domain.Common;
using FastEndpoints;
using Features.WaterHeaters.Queries;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http;

namespace Features.WaterHeaters.Endpoints;

public class GetWaterHeaterStateEndpoint : EndpointWithoutRequest<WaterHeaterStateResponse>
{
    private readonly IDispatcher _dispatcher;

    public GetWaterHeaterStateEndpoint(IDispatcher dispatcher) => _dispatcher = dispatcher;

    public override void Configure()
    {
        Get("/api/locations/{locationId}/water-heater");
        Tags("WaterHeater");
        Description(b => b.WithTags("WaterHeater"));
        AuthSchemes(JwtBearerDefaults.AuthenticationScheme);
        Summary(s =>
        {
            s.Summary = "Read the water heater's current state";
            s.Description = "Read from OSO at the time of the request: tank temperatures, litres of 40 °C water, " +
                             "showers and baths, whether it is heating and at what power, the set point, stored " +
                             "energy, special mode, and how old the reading is (isStale after 15 minutes). A value " +
                             "OSO does not give is null. osoCodes are OSO's undocumented numbers, passed on as they " +
                             "are. For owners and viewers of the location, and Admins.";
            s.Response(200, "State read");
            s.Response(401, "Not signed in");
            s.Response(404, "Location not found or not visible (Location.NotFound), or no water heater connected " +
                            "(WaterHeater.NotConnected)");
            s.Response(502, "OSO rejected the stored key or device id (WaterHeater.Rejected), answered in an " +
                            "unexpected way (WaterHeater.InvalidResponse), or the stored key cannot be read " +
                            "(WaterHeater.KeyUnreadable); an Admin must reconnect the heater for the first and last");
            s.Response(503, "OSO unavailable or no answer within 10 seconds (WaterHeater.Unavailable)");
        });
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var userId = Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);

        Result<WaterHeaterStateResponse> result = await _dispatcher.Send(
            new GetWaterHeaterStateQuery(userId, User.IsInRole(RoleNames.Admin), Route<Guid>("locationId")), ct);

        if (!result.IsSuccess)
        {
            AddError(result.Error.Description, result.Error.Code);
            ThrowIfAnyErrors(WaterHeaterErrorStatus.Of(result.Error, onConnect: false));
        }

        Response = result.Value;
    }
}
