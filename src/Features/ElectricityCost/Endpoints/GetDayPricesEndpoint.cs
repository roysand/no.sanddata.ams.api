using System.Security.Claims;
using Application.CQRS;
using Domain.Common;
using FastEndpoints;
using Features.ElectricityCost.Queries;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http;

namespace Features.ElectricityCost.Endpoints;

public class GetDayPricesEndpoint : Endpoint<GetDayPricesRequest, DayPricesResponse>
{
    private readonly IDispatcher _dispatcher;

    public GetDayPricesEndpoint(IDispatcher dispatcher) => _dispatcher = dispatcher;

    public override void Configure()
    {
        Get("/api/electricity-cost/prices");
        Tags("ElectricityCost");
        Description(b => b.WithTags("ElectricityCost"));
        AuthSchemes(JwtBearerDefaults.AuthenticationScheme);
        Summary(s =>
        {
            s.Summary = "Get the hourly price per kWh for one Norwegian day";
            s.Description = "Returns one entry for every real hour of the Oslo calendar day (23, 24 or 25), with the " +
                             "NOK rate per kWh for the location's enrolled pricing model, including hours with no " +
                             "readings and hours still to come. The rate is null when price data is not available yet. " +
                             "Defaults to today.";
            s.Response(200, "Day prices retrieved successfully");
            s.Response(404, "Location not found");
        });
    }

    public override async Task HandleAsync(GetDayPricesRequest req, CancellationToken ct)
    {
        var userId = Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);

        Result<DayPricesResponse> result =
            await _dispatcher.Send(new GetDayPricesQuery(userId, req.LocationId, req.Date), ct);

        if (!result.IsSuccess)
        {
            AddError(result.Error.Description, result.Error.Code);
            ThrowIfAnyErrors(result.Error.Type == ErrorType.NotFound ? 404 : 400);
        }

        Response = result.Value;
    }
}

public record GetDayPricesRequest(Guid LocationId, DateTime? Date = null);
