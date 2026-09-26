using System.Security.Claims;
using Application.CQRS;
using Domain.Common;
using FastEndpoints;
using Features.Locations.Queries;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http;

namespace Features.Locations.Endpoints;

public class GetLocationsEndpoint : EndpointWithoutRequest<IReadOnlyList<LocationSummaryResponse>>
{
    private readonly IDispatcher _dispatcher;

    public GetLocationsEndpoint(IDispatcher dispatcher) => _dispatcher = dispatcher;

    public override void Configure()
    {
        Get("/api/locations");
        Tags("Locations");
        // FastEndpoints' Tags() only feeds its own Swagger generator (not installed here);
        // Description(b => b.WithTags(...)) is what actually reaches the native
        // Microsoft.AspNetCore.OpenApi document Scalar renders, so both are needed.
        Description(b => b.WithTags("Locations"));
        AuthSchemes(JwtBearerDefaults.AuthenticationScheme);
        Summary(s =>
        {
            s.Summary = "Get my locations";
            s.Description = "Returns the locations the caller is associated with, and the meters registered at each.";
            s.Response(200, "Locations retrieved successfully");
        });
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var userId = Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);

        Result<IReadOnlyList<LocationSummaryResponse>> result = await _dispatcher.Send(new GetMyLocationsQuery(userId), ct);

        if (!result.IsSuccess)
        {
            AddError(result.Error.Description, result.Error.Code);
            ThrowIfAnyErrors(400);
        }

        Response = result.Value;
    }
}
