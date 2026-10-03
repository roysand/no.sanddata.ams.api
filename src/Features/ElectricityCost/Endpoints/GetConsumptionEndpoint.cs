using System.Security.Claims;
using Application.CQRS;
using Domain.Common;
using FastEndpoints;
using Features.ElectricityCost.Queries;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http;

namespace Features.ElectricityCost.Endpoints;

public class GetConsumptionEndpoint : Endpoint<GetConsumptionRequest, ConsumptionResponse>
{
    private readonly IDispatcher _dispatcher;

    public GetConsumptionEndpoint(IDispatcher dispatcher) => _dispatcher = dispatcher;

    public override void Configure()
    {
        Get("/api/consumption");
        Tags("ElectricityCost");
        Description(b => b.WithTags("ElectricityCost"));
        AuthSchemes(JwtBearerDefaults.AuthenticationScheme);
        Summary(s =>
        {
            s.Summary = "Get energy consumption per minute or hour";
            s.Description = "Returns kWh consumed per minute or per hour for one of the caller's own " +
                             "locations, optionally narrowed to a meter. Does not require price data. " +
                             "Defaults to the last 24 hours.";
            s.Response(200, "Consumption retrieved successfully");
            s.Response(404, "Location or meter not found");
        });
    }

    public override async Task HandleAsync(GetConsumptionRequest req, CancellationToken ct)
    {
        var userId = Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);

        Result<ConsumptionResponse> result = await _dispatcher.Send(
            new GetConsumptionQuery(userId, req.LocationId, req.MeterId, req.Granularity, req.From, req.To), ct);

        if (!result.IsSuccess)
        {
            AddError(result.Error.Description, result.Error.Code);
            ThrowIfAnyErrors(result.Error.Type == ErrorType.NotFound ? 404 : 400);
        }

        Response = result.Value;
    }
}

public record GetConsumptionRequest(
    Guid LocationId,
    string Granularity,
    Guid? MeterId = null,
    DateTime? From = null,
    DateTime? To = null);
