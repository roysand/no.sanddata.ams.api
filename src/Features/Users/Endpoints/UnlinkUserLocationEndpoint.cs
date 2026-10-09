using Application.CQRS;
using Domain.Common;
using FastEndpoints;
using Features.Users.Commands;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http;

namespace Features.Users.Endpoints;

public class UnlinkUserLocationEndpoint : EndpointWithoutRequest
{
    private readonly IDispatcher _dispatcher;

    public UnlinkUserLocationEndpoint(IDispatcher dispatcher) => _dispatcher = dispatcher;

    public override void Configure()
    {
        Delete("/api/users/{id}/locations/{locationId}");
        Tags("Users");
        Description(b => b.WithTags("Users"));
        AuthSchemes(JwtBearerDefaults.AuthenticationScheme);
        Roles(RoleNames.Admin);
        Summary(s =>
        {
            s.Summary = "Unlink a user from a location";
            s.Description = "The user can no longer see the location's data. Idempotent.";
            s.Response(204, "User unlinked (or was not linked)");
            s.Response(401, "Not signed in");
            s.Response(403, "Administrator role required");
            s.Response(404, "User or location not found");
            s.Response(409, "The user is the location's last owner");
        });
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        Result<UserLocationChangeResponse> result = await _dispatcher.Send(
            new UnlinkUserLocationCommand(Route<Guid>("id"), Route<Guid>("locationId"), Caller.From(User)), ct);

        if (!result.IsSuccess)
        {
            AddError(result.Error.Description, result.Error.Code);
            ThrowIfAnyErrors(result.Error.Type switch
            {
                ErrorType.NotFound => 404,
                ErrorType.Conflict => 409,
                _ => 400
            });
        }

        HttpContext.Response.StatusCode = 204;
    }
}

