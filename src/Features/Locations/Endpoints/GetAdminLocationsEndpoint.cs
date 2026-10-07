using System.Security.Claims;
using Application.CQRS;
using Domain.Common;
using FastEndpoints;
using Features.Locations.Queries;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http;

namespace Features.Locations.Endpoints;

public class GetAdminLocationsEndpoint : EndpointWithoutRequest<IReadOnlyList<AdminLocationResponse>>
{
    private readonly IDispatcher _dispatcher;

    public GetAdminLocationsEndpoint(IDispatcher dispatcher) => _dispatcher = dispatcher;

    public override void Configure()
    {
        Get("/api/admin/locations");
        Tags("Locations");
        Description(b => b.WithTags("Locations"));
        AuthSchemes(JwtBearerDefaults.AuthenticationScheme);
        Roles(RoleNames.Admin);
        Summary(s =>
        {
            s.Summary = "List all locations (admin)";
            s.Description = "Every location, including ones the caller is not linked to, with the non-secret facts about its " +
                             "sensor key (hint, expiry, status) and its readers. The key itself is never returned.";
            s.Response(200, "Locations retrieved successfully");
            s.Response(401, "Not signed in");
            s.Response(403, "Administrator role required");
        });
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var userId = Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);

        Result<IReadOnlyList<AdminLocationResponse>> result =
            await _dispatcher.Send(new GetAdminLocationsQuery(userId), ct);

        if (!result.IsSuccess)
        {
            AddError(result.Error.Description, result.Error.Code);
            ThrowIfAnyErrors(400);
        }

        Response = result.Value;
    }
}
