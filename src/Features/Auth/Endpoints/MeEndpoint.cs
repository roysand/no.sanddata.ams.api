using System.Security.Claims;
using FastEndpoints;

namespace Features.Auth.Endpoints;

public class MeEndpoint : EndpointWithoutRequest<MeResponse>
{
    public override void Configure()
    {
        Get("/api/auth/me");
        Summary(s =>
        {
            s.Summary = "Get current user";
            s.Description = "Returns the email and roles of the authenticated user";
        });
    }

    public override Task HandleAsync(CancellationToken ct)
    {
        string email = User.FindFirst(ClaimTypes.Email)?.Value ?? string.Empty;
        string[] roles = User.FindAll(ClaimTypes.Role).Select(c => c.Value).ToArray();

        Response = new MeResponse(email, roles);
        return Task.CompletedTask;
    }
}

public record MeResponse(string Email, string[] Roles);
