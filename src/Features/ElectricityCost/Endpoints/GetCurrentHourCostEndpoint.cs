using System.Security.Claims;
using Application.CQRS;
using Domain.Common;
using FastEndpoints;
using Features.ElectricityCost.Queries;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http;

namespace Features.ElectricityCost.Endpoints;

public class GetCurrentHourCostEndpoint : Endpoint<GetCurrentHourCostRequest, CurrentHourCostResponse>
{
    private readonly IDispatcher _dispatcher;

    public GetCurrentHourCostEndpoint(IDispatcher dispatcher) => _dispatcher = dispatcher;

    public override void Configure()
    {
        Get("/api/electricity-cost/current");
        Tags("ElectricityCost");
        Description(b => b.WithTags("ElectricityCost"));
        AuthSchemes(JwtBearerDefaults.AuthenticationScheme);
        Summary(s =>
        {
            s.Summary = "Get the cost accrued so far in the current hour";
            s.Description = "Returns consumption and cost so far in the current (open) hour for one of the " +
                             "caller's own locations, for the enrolled pricing model and the comparison model. " +
                             "A model is null when its price data is not yet available.";
            s.Response(200, "Current-hour cost retrieved successfully");
            s.Response(404, "Location not found");
        });
    }

    public override async Task HandleAsync(GetCurrentHourCostRequest req, CancellationToken ct)
    {
        var userId = Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);

        Result<CurrentHourCostResponse> result =
            await _dispatcher.Send(new GetCurrentHourCostQuery(userId, req.LocationId), ct);

        if (!result.IsSuccess)
        {
            AddError(result.Error.Description, result.Error.Code);
            ThrowIfAnyErrors(result.Error.Type == ErrorType.NotFound ? 404 : 400);
        }

        Response = result.Value;
    }
}

public record GetCurrentHourCostRequest(Guid LocationId);
