using Application.CQRS;
using Domain.Common;
using FastEndpoints;
using Features.Auth.Commands;

namespace Features.Auth.Endpoints;

public class RefreshTokenEndpoint : Endpoint<RefreshTokenRequest, RefreshTokenResponse>
{
    private readonly IDispatcher _dispatcher;

    public RefreshTokenEndpoint(IDispatcher dispatcher) => _dispatcher = dispatcher;

    public override void Configure()
    {
        Post("/api/auth/refresh");
        AllowAnonymous();
        Summary(s =>
        {
            s.Summary = "Refresh access token";
            s.Description = "Get new access token using refresh token";
            s.ExampleRequest = new RefreshTokenRequest("your-refresh-token-here");
        });
    }

    public override async Task HandleAsync(RefreshTokenRequest req, CancellationToken ct)
    {
        var command = new RefreshTokenCommand(req.RefreshToken);
        Result<RefreshTokenResponse> result = await _dispatcher.Send(command, ct);

        if (!result.IsSuccess)
        {
            AddError(result.Error.Description, result.Error.Code);
            ThrowIfAnyErrors();
        }

        Response = result.Value;
    }
}

public record RefreshTokenRequest(string RefreshToken);
