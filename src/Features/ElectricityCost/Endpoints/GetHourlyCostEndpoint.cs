using System.Security.Claims;
using Application.CQRS;
using Domain.Common;
using FastEndpoints;
using Features.ElectricityCost.Queries;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http;

namespace Features.ElectricityCost.Endpoints;

public class GetHourlyCostEndpoint : Endpoint<GetHourlyCostRequest, PagedHourlyCostResponse>
{
    private readonly IDispatcher _dispatcher;

    public GetHourlyCostEndpoint(IDispatcher dispatcher) => _dispatcher = dispatcher;

    public override void Configure()
    {
        Get("/api/electricity-cost/hourly");
        Tags("ElectricityCost");
        Description(b => b.WithTags("ElectricityCost"));
        AuthSchemes(JwtBearerDefaults.AuthenticationScheme);
        Summary(s =>
        {
            s.Summary = "Get hourly electricity cost for a location";
            s.Description = "Returns consumption and cost per hour for one of the caller's own locations, " +
                             "for the enrolled pricing model and the comparison model. Defaults to the last 24 hours.";
            s.Response(200, "Hourly cost retrieved successfully");
            s.Response(404, "Location not found");
        });
    }

    public override async Task HandleAsync(GetHourlyCostRequest req, CancellationToken ct)
    {
        var userId = Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);

        Result<PagedHourlyCostResponse> result = await _dispatcher.Send(
            new GetHourlyCostQuery(userId, req.LocationId, req.From, req.To, req.Page, req.PageSize), ct);

        if (!result.IsSuccess)
        {
            AddError(result.Error.Description, result.Error.Code);
            ThrowIfAnyErrors(result.Error.Type == ErrorType.NotFound ? 404 : 400);
        }

        Response = result.Value;
    }
}

public record GetHourlyCostRequest(
    Guid LocationId,
    DateTime? From = null,
    DateTime? To = null,
    int Page = 1,
    int PageSize = 500);
