using System.Security.Claims;
using Application.CQRS;
using Domain.Common;
using FastEndpoints;
using Features.Locations.Commands;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http;

namespace Features.Locations.Endpoints;

public class RotateLocationKeyEndpoint : EndpointWithoutRequest<RotatedKeyResponse>
{
    private readonly IDispatcher _dispatcher;

    public RotateLocationKeyEndpoint(IDispatcher dispatcher) => _dispatcher = dispatcher;

    public override void Configure()
    {
        Post("/api/admin/locations/{id}/api-key/rotate");
        Tags("Locations");
        Description(b => b.WithTags("Locations"));
        AuthSchemes(JwtBearerDefaults.AuthenticationScheme);
        Roles(RoleNames.Admin);
        Summary(s =>
        {
            s.Summary = "Rotate a location's sensor key (admin)";
            s.Description = "Generates a new key and invalidates the old one immediately; the sensor stops working until it " +
                             "is given the new key. The new key is returned ONLY in this response. Also reactivates a " +
                             "deactivated key and restarts its two-year lifetime.";
            s.Response(200, "Key rotated; the response contains the new key, shown once");
            s.Response(401, "Not signed in");
            s.Response(403, "Administrator role required");
            s.Response(404, "Location not found");
        });
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var userId = Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);

        Result<RotatedKeyResponse> result =
            await _dispatcher.Send(new RotateLocationKeyCommand(userId, Route<Guid>("id")), ct);

        if (!result.IsSuccess)
        {
            AddError(result.Error.Description, result.Error.Code);
            ThrowIfAnyErrors(result.Error.Type == ErrorType.NotFound ? 404 : 400);
        }

        Response = result.Value;
    }
}
