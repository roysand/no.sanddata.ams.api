using System.Text.Encodings.Web;
using Application.Common.ApiKeys;
using Application.Common.Interfaces.Repositories;
using Domain.Common.Entities;
using Infrastructure.Authentication;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Features.Tests.Authentication;

public class ApiKeyAuthenticationHandlerTests
{
    private const string PlainKey = "plain-key-for-test-0123456789abcdef";

    private readonly IApiKeyRepository<ApiKey> _repository = Substitute.For<IApiKeyRepository<ApiKey>>();
    private readonly Location _location = new(Guid.NewGuid(), "Home", "Addr", "SN1", "NO1", true, false);

    private async Task<AuthenticateResult> AuthenticateAsync(string? headerValue)
    {
        IOptionsMonitor<ApiKeyAuthenticationOptions> options = Substitute.For<IOptionsMonitor<ApiKeyAuthenticationOptions>>();
        options.Get(Arg.Any<string>()).Returns(new ApiKeyAuthenticationOptions());

        var handler = new ApiKeyAuthenticationHandler(options, NullLoggerFactory.Instance, UrlEncoder.Default, _repository);
        var context = new DefaultHttpContext();
        if (headerValue is not null)
        {
            context.Request.Headers["X-API-Key"] = headerValue;
        }

        await handler.InitializeAsync(
            new AuthenticationScheme("ApiKey", null, typeof(ApiKeyAuthenticationHandler)), context);
        return await handler.AuthenticateAsync();
    }

    private ApiKey StoredKeyFor(string plainKey)
    {
        var key = new ApiKey(Guid.NewGuid(), ApiKeyCrypto.Hash(plainKey), ApiKeyCrypto.Hint(plainKey), "d", true,
            DateTime.UtcNow.AddDays(30));
        typeof(ApiKey).GetProperty(nameof(ApiKey.Location))!.SetValue(key, _location); // EF sets this in real use
        return key;
    }

    [Fact]
    public async Task Authenticate_ValidKey_SucceedsWithLocationClaim()
    {
        _repository.FindActiveByKeyHashAsync(ApiKeyCrypto.Hash(PlainKey), Arg.Any<CancellationToken>())
            .Returns(StoredKeyFor(PlainKey));

        AuthenticateResult result = await AuthenticateAsync(PlainKey);

        Assert.True(result.Succeeded);
        Assert.Equal(_location.Id.ToString(), result.Principal!.FindFirst("LocationId")!.Value);
    }

    [Fact]
    public async Task Authenticate_ValidKey_NoClaimContainsTheKey()
    {
        _repository.FindActiveByKeyHashAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(StoredKeyFor(PlainKey));

        AuthenticateResult result = await AuthenticateAsync(PlainKey);

        Assert.DoesNotContain(result.Principal!.Claims, c => c.Value.Contains(PlainKey));
        Assert.DoesNotContain(result.Principal.Claims, c => c.Type == "ApiKey");
    }

    [Fact]
    public async Task Authenticate_LooksUpTheHashNeverThePlainKey()
    {
        await AuthenticateAsync(PlainKey);

        await _repository.Received(1).FindActiveByKeyHashAsync(ApiKeyCrypto.Hash(PlainKey), Arg.Any<CancellationToken>());
        await _repository.DidNotReceive().FindActiveByKeyHashAsync(PlainKey, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Authenticate_NoMatch_Fails()
    {
        // The repository returns null for a wrong, expired or deactivated key or an inactive location.
        _repository.FindActiveByKeyHashAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns((ApiKey?)null);

        AuthenticateResult result = await AuthenticateAsync("some-other-key");

        Assert.False(result.Succeeded);
        Assert.NotNull(result.Failure);
    }

    [Fact]
    public async Task Authenticate_NoHeader_YieldsNoResult()
    {
        AuthenticateResult result = await AuthenticateAsync(null);

        Assert.True(result.None);
    }

    [Fact]
    public async Task Authenticate_BlankHeader_Fails()
    {
        AuthenticateResult result = await AuthenticateAsync("   ");

        Assert.NotNull(result.Failure);
        await _repository.DidNotReceive().FindActiveByKeyHashAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }
}
