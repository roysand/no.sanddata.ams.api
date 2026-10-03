using Application.CQRS;
using Domain.Common;
using FastEndpoints;
using Features.Users.Commands;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http;

namespace Features.Users.Endpoints;

public class DeleteUserEndpoint : EndpointWithoutRequest
{
    private readonly IDispatcher _dispatcher;

    public DeleteUserEndpoint(IDispatcher dispatcher) => _dispatcher = dispatcher;

    public override void Configure()
    {
        Delete("/api/users/{id}");
        Tags("Users");
        Description(b => b.WithTags("Users"));
        AuthSchemes(JwtBearerDefaults.AuthenticationScheme);
        Roles(RoleNames.Admin);
        Summary(s =>
        {
            s.Summary = "Delete user";
            s.Description = "Delete a user by their ID. The user will be permanently removed.";
            s.Response(204, "User deleted successfully");
            s.Response(401, "Not signed in");
            s.Response(403, "Administrator role required");
            s.Response(409, "The last active administrator cannot be deleted");
            s.Response(404, "User not found");
        });
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        Guid id = Route<Guid>("id");
        var command = new DeleteUserCommand(id, Caller.From(User));
        Result<DeleteUserResponse> result = await _dispatcher.Send(command, ct);

        if (!result.IsSuccess)
        {
            AddError(result.Error.Description, result.Error.Code);
            ThrowIfAnyErrors(result.Error.Type == ErrorType.Conflict ? 409 : 404);
        }

        // Success - send 204 No Content
        HttpContext.Response.StatusCode = 204;
    }
}
