using Application.CQRS;
using Domain.Common;
using Domain.Common.Entities;
using FastEndpoints;
using Features.Users.Commands;
using Features.Users.Mappers;
using Features.Users.Queries;

namespace Features.Users.Endpoints;

public class CreateUserEndpoint : Endpoint<CreateUserRequest, CreateUserResponse>
{
    private readonly IDispatcher _dispatcher;

    public CreateUserEndpoint(IDispatcher dispatcher) => _dispatcher = dispatcher;

    public override void Configure()
    {
        Post("/api/users");
        AllowAnonymous();
        Summary(s =>
        {
            s.Summary = "Create a new user";
            s.Description = "Register a new user in the system. The user will be created with IsActive set to true.";
            s.ExampleRequest = new CreateUserRequest("John", "Doe", "john.doe@example.com", "SecurePass123!");
            s.Response(201, "User created successfully");
            s.Response(409, "User with this email already exists");
            s.Response(400, "Invalid request data");
        });
    }

    public override async Task HandleAsync(CreateUserRequest req, CancellationToken ct)
    {
        CreateUserCommand command = UserMapper.ToCreateCommand(req);
        Result<CreateUserResponse> result = await _dispatcher.Send(command, ct);

        if (!result.IsSuccess)
        {
            AddError(result.Error.Description, result.Error.Code);
            ThrowIfAnyErrors(result.Error.Type switch
            {
                ErrorType.Conflict => 409,
                ErrorType.Validation => 400,
                _ => 400
            });
        }

        // Set 201 Created status and Location header
        HttpContext.Response.StatusCode = 201;
        HttpContext.Response.Headers.Location = $"/api/users/{result.Value.Id}";

        Response = result.Value;
    }
}

public record CreateUserRequest(
    string FirstName,
    string LastName,
    string Email,
    string Password
);
