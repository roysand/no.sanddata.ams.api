using System.Security.Claims;
using System.Text.Encodings.Web;
using Application.Common.Interfaces.Repositories;
using Domain.Common.Entities;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Infrastructure.Authentication;

public class ApiKeyAuthenticationHandler : AuthenticationHandler<ApiKeyAuthenticationOptions>
{
    private readonly IApiKeyEfRepository<ApiKey> _apiKeyRepository;

    public ApiKeyAuthenticationHandler(
        IOptionsMonitor<ApiKeyAuthenticationOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        IApiKeyEfRepository<ApiKey> apiKeyRepository)
        : base(options, logger, encoder)
    {
        _apiKeyRepository = apiKeyRepository;
    }

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(Options.ApiKeyHeaderName, out var apiKeyHeaderValues))
        {
            return AuthenticateResult.NoResult();
        }

        var providedApiKey = apiKeyHeaderValues.FirstOrDefault();
        if (string.IsNullOrWhiteSpace(providedApiKey))
        {
            return AuthenticateResult.Fail("Invalid API Key");
        }

        var apiKeys = await _apiKeyRepository.FindAsync(
            k => k.Key == providedApiKey && k.IsActive && k.ExpiresAt > DateTime.UtcNow,
            CancellationToken.None);

        var apiKey = apiKeys.FirstOrDefault();
        if (apiKey is null)
        {
            return AuthenticateResult.Fail("Invalid or expired API Key");
        }

        var claims = new[]
        {
            new Claim("ApiKey", apiKey.Key),
            new Claim(ClaimTypes.Name, "ApiKeyUser"),
            new Claim("ApiKeyId", apiKey.GetType().GetProperty("Id")?.GetValue(apiKey)?.ToString() ?? string.Empty)
        };

        var identity = new ClaimsIdentity(claims, Scheme.Name);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, Scheme.Name);

        return AuthenticateResult.Success(ticket);
    }
}
