using System.Security.Claims;
using Application.CQRS;
using Domain.Common;
using FastEndpoints;
using Features.ElectricityCost.Queries;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http;

namespace Features.ElectricityCost.Endpoints;

public class GetNorgesPrisSavingEndpoint : Endpoint<GetNorgesPrisSavingRequest, NorgesPrisSavingResponse>
{
    private readonly IDispatcher _dispatcher;

    public GetNorgesPrisSavingEndpoint(IDispatcher dispatcher) => _dispatcher = dispatcher;

    public override void Configure()
    {
        Get("/api/electricity-cost/saving");
        Tags("ElectricityCost");
        Description(b => b.WithTags("ElectricityCost"));
        AuthSchemes(JwtBearerDefaults.AuthenticationScheme);
        Summary(s =>
        {
            s.Summary = "Get what Norgespris saves compared with spot terms";
            s.Description = "For today, the last 7 days, the month so far and the year so far (Oslo calendar days): the cost " +
                             "on spot terms minus the cost on Norgespris terms, summed per hour, in NOK including VAT. Negative " +
                             "means Norgespris cost more. An hour without a spot price or exchange rate is left out and " +
                             "hoursCounted says how many hours were counted out of hoursWithConsumption. For a location " +
                             "without the agreement it is what the agreement would have saved. The monthly Norgespris cap " +
                             "(5,000 kWh, 1,000 for a holiday home) is applied: kWh above it are on spot terms on both " +
                             "sides and add nothing.";
            s.Response(200, "Saving retrieved successfully");
            s.Response(404, "Location not found");
        });
    }

    public override async Task HandleAsync(GetNorgesPrisSavingRequest req, CancellationToken ct)
    {
        var userId = Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);

        Result<NorgesPrisSavingResponse> result =
            await _dispatcher.Send(new GetNorgesPrisSavingQuery(userId, req.LocationId), ct);

        if (!result.IsSuccess)
        {
            AddError(result.Error.Description, result.Error.Code);
            ThrowIfAnyErrors(result.Error.Type == ErrorType.NotFound ? 404 : 400);
        }

        Response = result.Value;
    }
}

public record GetNorgesPrisSavingRequest(Guid LocationId);
