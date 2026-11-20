using System.Security.Claims;
using Application.CQRS;
using Domain.Common;
using FastEndpoints;
using Features.WaterHeaters.Queries;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http;

namespace Features.WaterHeaters.Endpoints;

/// <param name="LocationId">The location, from the route.</param>
/// <param name="From">The window's start; 3 hours before <paramref name="To"/> when left out. Not after now.</param>
/// <param name="To">The window's end; now when left out. May be in the future: nothing is counted past now.</param>
public record GetWaterHeaterPowerRequest(Guid LocationId, DateTime? From, DateTime? To);

public class GetWaterHeaterPowerEndpoint : Endpoint<GetWaterHeaterPowerRequest, WaterHeaterPowerResponse>
{
    private readonly IDispatcher _dispatcher;

    public GetWaterHeaterPowerEndpoint(IDispatcher dispatcher) => _dispatcher = dispatcher;

    public override void Configure()
    {
        Get("/api/locations/{locationId}/water-heater/power");
        Tags("WaterHeater");
        Description(b => b.WithTags("WaterHeater"));
        AuthSchemes(JwtBearerDefaults.AuthenticationScheme);
        Summary(s =>
        {
            s.Summary = "The water heater's average power per 5 minutes";
            s.Description = "From the stored power samples, in a window of at most 48 hours; the last 3 hours when " +
                             "from and to are left out. from and to in the answer are the window used, in UTC. One " +
                             "item per 5 minutes with any sampled time; a step without samples has no item (draw a " +
                             "gap, never 0). powerKw is the average over the sampled time only, and sampledSeconds " +
                             "(1-300) says how much of the step was covered. No connected heater is needed. For " +
                             "owners and viewers of the location, and Admins.";
            s.Response(200, "The window's 5-minute steps");
            s.Response(400, "from is not before to, the window is longer than 48 hours, or from is in the future");
            s.Response(401, "Not signed in");
            s.Response(404, "Location not found or not visible (Location.NotFound)");
        });
    }

    public override async Task HandleAsync(GetWaterHeaterPowerRequest req, CancellationToken ct)
    {
        var userId = Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);

        Result<WaterHeaterPowerResponse> result = await _dispatcher.Send(
            new GetWaterHeaterPowerQuery(userId, User.IsInRole(RoleNames.Admin), req.LocationId, req.From, req.To),
            ct);

        if (!result.IsSuccess)
        {
            AddError(result.Error.Description, result.Error.Code);
            ThrowIfAnyErrors(WaterHeaterErrorStatus.Of(result.Error, onConnect: false));
        }

        Response = result.Value;
    }
}
