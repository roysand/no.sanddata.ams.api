using Application.CQRS;
using Domain.Common;
using FastEndpoints;
using Features.Users.Commands;

namespace Features.Users.Endpoints;

public class UpdateUserEndpoint : Endpoint<UpdateUserRequest, UpdateUserResponse>
{
    private readonly IDispatcher _dispatcher;

    public UpdateUserEndpoint(IDispatcher dispatcher) => _dispatcher = dispatcher;

    public override void Configure()
    {
        Put("/api/users/{id}");
        AllowAnonymous();
        Summary(s =>
        {
            s.Summary = "Update user";
            s.Description = "Update an existing user's information";
            s.ExampleRequest = new UpdateUserRequest(Guid.NewGuid(), "John", "Doe", "john.doe@example.com", true);
            s.Response(200, "User updated successfully");
            s.Response(404, "User not found");
            s.Response(409, "Email already exists");
            s.Response(400, "Invalid request data");
        });
    }

    public override async Task HandleAsync(UpdateUserRequest req, CancellationToken ct)
    {
        var command = new UpdateUserCommand(req.Id, req.FirstName, req.LastName, req.Email, req.IsActive);
        Result<UpdateUserResponse> result = await _dispatcher.Send(command, ct);

        if (!result.IsSuccess)
        {
            AddError(result.Error.Code, result.Error.Description);
            ThrowIfAnyErrors(result.Error.Type switch
            {
                ErrorType.NotFound => 404,
                ErrorType.Conflict => 409,
                ErrorType.Validation => 400,
                _ => 400
            });
        }

        Response = result.Value;
    }
}

public record UpdateUserRequest(
    Guid Id,
    string FirstName,
    string LastName,
    string Email,
    bool IsActive
);
