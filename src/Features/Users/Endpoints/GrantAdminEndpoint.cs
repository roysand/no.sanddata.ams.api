using Application.CQRS;
using Domain.Common;
using FastEndpoints;
using Features.Users.Commands;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http;

namespace Features.Users.Endpoints;

public class GrantAdminEndpoint : EndpointWithoutRequest
{
    private readonly IDispatcher _dispatcher;

    public GrantAdminEndpoint(IDispatcher dispatcher) => _dispatcher = dispatcher;

    public override void Configure()
    {
        Put("/api/users/{id}/roles/admin");
        Tags("Users");
        Description(b => b.WithTags("Users"));
        AuthSchemes(JwtBearerDefaults.AuthenticationScheme);
        Roles(RoleNames.Admin);
        Summary(s =>
        {
            s.Summary = "Grant the Admin role";
            s.Description = "Makes the user an administrator. Idempotent. Takes effect at the user's next sign-in or token refresh.";
            s.Response(204, "Admin role granted (or already held)");
            s.Response(401, "Not signed in");
            s.Response(403, "Administrator role required");
            s.Response(404, "User not found");
        });
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        Result<AdminRoleChangeResponse> result =
            await _dispatcher.Send(new GrantAdminCommand(Route<Guid>("id"), Caller.From(User)), ct);

        if (!result.IsSuccess)
        {
            AddError(result.Error.Description, result.Error.Code);
            ThrowIfAnyErrors(result.Error.Type == ErrorType.NotFound ? 404 : 400);
        }

        HttpContext.Response.StatusCode = 204;
    }
}

