using System.Security.Claims;
using System.Text.Encodings.Web;
using Application.CQRS;
using FastEndpoints;
using Features.Users.Endpoints;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Features.Tests.Http;

/// <summary>
/// The real FastEndpoints pipeline (routing, binding, validation, authorisation) in memory, with a stubbed
/// dispatcher and a sign-in that always succeeds as an administrator. Catches what handler tests cannot,
/// such as a request being refused before any handler runs.
/// </summary>
internal sealed class EndpointHost : IAsyncDisposable
{
    private readonly WebApplication _app;

    public IDispatcher Dispatcher { get; }
    public HttpClient Client { get; }

    private EndpointHost(WebApplication app, IDispatcher dispatcher)
    {
        _app = app;
        Dispatcher = dispatcher;
        Client = app.GetTestClient();
    }

    public static async Task<EndpointHost> StartAsync(Action<IDispatcher> arrange)
    {
        var dispatcher = Substitute.For<IDispatcher>();
        arrange(dispatcher);

        WebApplicationBuilder builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        builder.Services.AddSingleton(dispatcher);
        builder.Services
            .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddScheme<AuthenticationSchemeOptions, AdminAuthHandler>(JwtBearerDefaults.AuthenticationScheme, null);
        builder.Services.AddAuthorization();
        builder.Services.AddFastEndpoints(options =>
            options.Assemblies = [typeof(CreateUserEndpoint).Assembly]);

        WebApplication app = builder.Build();
        app.UseAuthentication();
        app.UseAuthorization();
        app.UseFastEndpoints();
        await app.StartAsync();

        return new EndpointHost(app, dispatcher);
    }

    public async ValueTask DisposeAsync()
    {
        Client.Dispose();
        await _app.DisposeAsync();
    }

    private sealed class AdminAuthHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var identity = new ClaimsIdentity(
                [
                    new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()),
                    new Claim(ClaimTypes.Role, Domain.Common.RoleNames.Admin)
                ],
                Scheme.Name);
            return Task.FromResult(
                AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name)));
        }
    }
}
