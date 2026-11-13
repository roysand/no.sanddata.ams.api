using System.Security.Claims;
using Application.CQRS;
using Domain.Common;
using FastEndpoints;
using Features.NorgesPris.Queries;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http;

namespace Features.NorgesPris.Endpoints;

public class ListNorgesPrisRatesEndpoint : EndpointWithoutRequest<IReadOnlyList<NorgesPrisRateResponse>>
{
    private readonly IDispatcher _dispatcher;

    public ListNorgesPrisRatesEndpoint(IDispatcher dispatcher) => _dispatcher = dispatcher;

    public override void Configure()
    {
        Get("/api/admin/norgespris");
        Tags("NorgesPris");
        Description(b => b.WithTags("NorgesPris"));
        AuthSchemes(JwtBearerDefaults.AuthenticationScheme);
        Roles(RoleNames.Admin);
        Summary(s =>
        {
            s.Summary = "List the Norgespris rates (admin)";
            s.Description = "Every rate, oldest start date first. Status is Superseded, InEffect (exactly one) or " +
                             "Upcoming, from today's date in Norway. Only Upcoming rates can be deleted.";
            s.Response(200, "Rates retrieved successfully");
            s.Response(401, "Not signed in");
            s.Response(403, "Administrator role required");
        });
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var userId = Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);

        Result<IReadOnlyList<NorgesPrisRateResponse>> result =
            await _dispatcher.Send(new ListNorgesPrisRatesQuery(userId), ct);

        if (!result.IsSuccess)
        {
            AddError(result.Error.Description, result.Error.Code);
            ThrowIfAnyErrors(400);
        }

        Response = result.Value;
    }
}
