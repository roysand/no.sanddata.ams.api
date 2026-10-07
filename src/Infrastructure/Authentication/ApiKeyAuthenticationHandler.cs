using System.Security.Claims;
using System.Text.Encodings.Web;
using Application.Common.ApiKeys;
using Application.Common.Interfaces.Repositories;
using Domain.Common.Entities;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;

namespace Infrastructure.Authentication;

public class ApiKeyAuthenticationHandler : AuthenticationHandler<ApiKeyAuthenticationOptions>
{
    private readonly IApiKeyRepository<ApiKey> _apiKeyRepository;

    public ApiKeyAuthenticationHandler(
        IOptionsMonitor<ApiKeyAuthenticationOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        IApiKeyRepository<ApiKey> apiKeyRepository)
        : base(options, logger, encoder) =>
        _apiKeyRepository = apiKeyRepository;

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(Options.ApiKeyHeaderName, out StringValues apiKeyHeaderValues))
        {
            return AuthenticateResult.NoResult();
        }

        string? providedApiKey = apiKeyHeaderValues.FirstOrDefault();
        if (string.IsNullOrWhiteSpace(providedApiKey))
        {
            return AuthenticateResult.Fail("Invalid API Key");
        }

        // Only the fingerprint is looked up; the key itself is never stored, logged or put in a claim.
        ApiKey? apiKey = await _apiKeyRepository.FindActiveByKeyHashAsync(
            ApiKeyCrypto.Hash(providedApiKey), CancellationToken.None);
        if (apiKey is null)
        {
            return AuthenticateResult.Fail("Invalid or expired API Key");
        }

        Claim[] claims = new[]
        {
            new Claim(ClaimTypes.Name, "ApiKeyUser"),
            new Claim("ApiKeyId", apiKey.Id.ToString()),
            new Claim("LocationId", apiKey.Location.Id.ToString())
        };

        var identity = new ClaimsIdentity(claims, Scheme.Name);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, Scheme.Name);

        return AuthenticateResult.Success(ticket);
    }
}
