using System.Security.Claims;
using Application.CQRS;
using Domain.Common;
using FastEndpoints;
using Features.ElectricityCost.Queries;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http;

namespace Features.ElectricityCost.Endpoints;

public class GetDailyCostEndpoint : Endpoint<GetDailyCostRequest, DailyCostResponse>
{
    private readonly IDispatcher _dispatcher;

    public GetDailyCostEndpoint(IDispatcher dispatcher) => _dispatcher = dispatcher;

    public override void Configure()
    {
        Get("/api/electricity-cost/daily");
        Tags("ElectricityCost");
        Description(b => b.WithTags("ElectricityCost"));
        AuthSchemes(JwtBearerDefaults.AuthenticationScheme);
        Summary(s =>
        {
            s.Summary = "Get daily electricity cost for a location";
            s.Description = "Returns consumption and cost per UTC day (the sum of that day's hourly figures) " +
                             "for one of the caller's own locations. Defaults to the last 7 days.";
            s.Response(200, "Daily cost retrieved successfully");
            s.Response(404, "Location not found");
        });
    }

    public override async Task HandleAsync(GetDailyCostRequest req, CancellationToken ct)
    {
        var userId = Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);

        Result<DailyCostResponse> result =
            await _dispatcher.Send(new GetDailyCostQuery(userId, req.LocationId, req.From, req.To), ct);

        if (!result.IsSuccess)
        {
            AddError(result.Error.Description, result.Error.Code);
            ThrowIfAnyErrors(result.Error.Type == ErrorType.NotFound ? 404 : 400);
        }

        Response = result.Value;
    }
}

public record GetDailyCostRequest(Guid LocationId, DateTime? From = null, DateTime? To = null);
