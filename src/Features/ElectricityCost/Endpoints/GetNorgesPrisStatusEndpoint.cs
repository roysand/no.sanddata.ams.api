using System.Security.Claims;
using Application.CQRS;
using Domain.Common;
using FastEndpoints;
using Features.ElectricityCost.Queries;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http;

namespace Features.ElectricityCost.Endpoints;

public class GetNorgesPrisStatusEndpoint : EndpointWithoutRequest<NorgesPrisStatusResponse>
{
    private readonly IDispatcher _dispatcher;

    public GetNorgesPrisStatusEndpoint(IDispatcher dispatcher) => _dispatcher = dispatcher;

    public override void Configure()
    {
        Get("/api/electricity-cost/norgespris-status");
        Tags("ElectricityCost");
        Description(b => b.WithTags("ElectricityCost"));
        AuthSchemes(JwtBearerDefaults.AuthenticationScheme);
        Summary(s =>
        {
            s.Summary = "Get the Norgespris limit status of my locations";
            s.Description = "Returns, for every active location in the caller's location list that has a Norgespris " +
                             "agreement, the month's limit, the kWh so far (with the car's when it is counted with " +
                             "the house), the projected kWh for the month, whether and by how much it is over the " +
                             "limit, the Norgespris price per kWh including VAT, and when the limit resets.";
            s.Response(200, "Norgespris status retrieved successfully");
        });
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var userId = Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);

        Result<NorgesPrisStatusResponse> result = await _dispatcher.Send(new GetNorgesPrisStatusQuery(userId), ct);

        if (!result.IsSuccess)
        {
            AddError(result.Error.Description, result.Error.Code);
            ThrowIfAnyErrors(400);
        }

        Response = result.Value;
    }
}
