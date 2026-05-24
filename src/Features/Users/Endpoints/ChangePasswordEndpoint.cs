using Application.CQRS;
using Domain.Common;
using FastEndpoints;
using Features.Users.Commands;

namespace Features.Users.Endpoints;

public class ChangePasswordEndpoint : Endpoint<ChangePasswordRequest>
{
    private readonly IDispatcher _dispatcher;

    public ChangePasswordEndpoint(IDispatcher dispatcher) => _dispatcher = dispatcher;

    public override void Configure()
    {
        Put("/api/users/{id}/password");
        AllowAnonymous();
        Summary(s =>
        {
            s.Summary = "Change user password";
            s.Description = "Allow users to change their password by providing current and new password";
            s.ExampleRequest = new ChangePasswordRequest(Guid.NewGuid(), "OldPassword123!", "NewSecurePass456!");
            s.Response(200, "Password changed successfully");
            s.Response(404, "User not found");
            s.Response(400, "Invalid request or current password incorrect");
        });
    }

    public override async Task HandleAsync(ChangePasswordRequest req, CancellationToken ct)
    {
        var command = new ChangePasswordCommand(req.Id, req.CurrentPassword, req.NewPassword);
        Result<ChangePasswordResponse> result = await _dispatcher.Send(command, ct);

        if (!result.IsSuccess)
        {
            AddError(result.Error.Code, result.Error.Description);
            ThrowIfAnyErrors(result.Error.Type switch
            {
                ErrorType.NotFound => 404,
                ErrorType.Validation => 400,
                _ => 400
            });
        }

        // Success - FastEndpoints will automatically send 200 OK
    }
}

public record ChangePasswordRequest(
    Guid Id,
    string CurrentPassword,
    string NewPassword
);
