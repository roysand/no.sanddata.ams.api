using Application.CQRS;
using Domain.Common;
using FastEndpoints;
using Features.Users.Commands;

namespace Features.Users.Endpoints;

public class DeleteUserEndpoint : EndpointWithoutRequest
{
    private readonly IDispatcher _dispatcher;

    public DeleteUserEndpoint(IDispatcher dispatcher) => _dispatcher = dispatcher;

    public override void Configure()
    {
        Delete("/api/users/{id}");
        AllowAnonymous();
        Summary(s =>
        {
            s.Summary = "Delete user";
            s.Description = "Delete a user by their ID. The user will be permanently removed.";
            s.Response(204, "User deleted successfully");
            s.Response(404, "User not found");
        });
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        Guid id = Route<Guid>("id");
        var command = new DeleteUserCommand(id);
        Result<DeleteUserResponse> result = await _dispatcher.Send(command, ct);

        if (!result.IsSuccess)
        {
            AddError(result.Error.Code, result.Error.Description);
            ThrowIfAnyErrors(404);
        }

        // Success - send 204 No Content
        HttpContext.Response.StatusCode = 204;
    }
}
