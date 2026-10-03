using Application.CQRS;
using Domain.Common;
using FastEndpoints;
using Features.Users.Commands;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http;

namespace Features.Users.Endpoints;

public class RevokeAdminEndpoint : EndpointWithoutRequest
{
    private readonly IDispatcher _dispatcher;

    public RevokeAdminEndpoint(IDispatcher dispatcher) => _dispatcher = dispatcher;

    public override void Configure()
    {
        Delete("/api/users/{id}/roles/admin");
        Tags("Users");
        Description(b => b.WithTags("Users"));
        AuthSchemes(JwtBearerDefaults.AuthenticationScheme);
        Roles(RoleNames.Admin);
        Summary(s =>
        {
            s.Summary = "Revoke the Admin role";
            s.Description = "Makes the user an ordinary user again. Idempotent. The last active administrator cannot be demoted. " +
                             "Takes effect at the user's next sign-in or token refresh.";
            s.Response(204, "Admin role revoked (or not held)");
            s.Response(401, "Not signed in");
            s.Response(403, "Administrator role required");
            s.Response(404, "User not found");
            s.Response(409, "The last active administrator cannot be demoted");
        });
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        Result<AdminRoleChangeResponse> result =
            await _dispatcher.Send(new RevokeAdminCommand(Route<Guid>("id"), Caller.From(User)), ct);

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

