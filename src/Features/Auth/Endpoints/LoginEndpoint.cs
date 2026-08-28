using Application.CQRS;
using Domain.Common;
using FastEndpoints;
using Features.Auth.Commands;

namespace Features.Auth.Endpoints;

public class LoginEndpoint : Endpoint<LoginRequest, LoginResponse>
{
    private readonly IDispatcher _dispatcher;

    public LoginEndpoint(IDispatcher dispatcher) => _dispatcher = dispatcher;

    public override void Configure()
    {
        Post("/api/auth/login");
        AllowAnonymous();
        Summary(s =>
        {
            s.Summary = "User login";
            s.Description = "Authenticate user and receive JWT token";
            s.ExampleRequest = new LoginRequest("user@example.com", "password123");
        });
    }

    public override async Task HandleAsync(LoginRequest req, CancellationToken ct)
    {
        var command = new LoginCommand(req.Email, req.Password);
        Result<LoginResponse> result = await _dispatcher.Send(command, ct);

        if (!result.IsSuccess)
        {
            AddError(result.Error.Description, result.Error.Code);
            ThrowIfAnyErrors(result.Error.Type switch
            {
                ErrorType.NotFound => 401,
                ErrorType.Validation => 400,
                _ => 400
            });
        }

        Response = result.Value;
    }
}

public record LoginRequest(string Email, string Password);
