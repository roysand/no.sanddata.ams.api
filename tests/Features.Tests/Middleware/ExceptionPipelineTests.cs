using System.Net;
using Application.Common.Interfaces.Repositories;
using Application.CQRS;
using Domain.Common.Entities;
using FastEndpoints;
using Features.Measurements.Endpoints;
using Features.Tests.Http;
using Infrastructure.Authentication;
using Infrastructure.Logging;
using Infrastructure.Middleware;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace Features.Tests.Middleware;

/// <summary>
/// The API's request steps in the order <c>Program.cs</c> runs them (<see cref="RequestPipelineExtensions"/>), with the
/// real sign-in handlers. Checks that neither the response nor any log message carries the exception text (spec 013,
/// FR-009), and that failures during the API-key check get the same answer (spec 014).
/// </summary>
[Collection(FastEndpointsHostCollection.Name)]
public class ExceptionPipelineTests
{
    private const string FixedBody = "\"message\":\"An unexpected error occurred.\"";
    private const int UnhandledExceptionEventId = 2020;
    private const int ResponseErrorEventId = 2002;
    private const string CorsPolicy = "Test";
    private const string AllowedOrigin = "https://app.example";

    [Fact]
    public async Task UnexpectedException_LeaksNothingInResponseOrRequestLog()
    {
        await using Pipeline pipeline = await Pipeline.StartAsync(
            _ => throw new InvalidOperationException("Failed to connect to 10.0.0.5:5432"));

        HttpResponseMessage response = await pipeline.Client.GetAsync("/api/test");
        string body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Contains(FixedBody, body, StringComparison.Ordinal);
        Assert.DoesNotContain("10.0.0.5", body, StringComparison.Ordinal);
        Assert.False(pipeline.Logs.ContainsText("10.0.0.5"));

        IReadOnlyList<CapturedLog> errors = [.. pipeline.Logs.All.Where(l => l.Level >= LogLevel.Error)];
        Assert.Equal(2, errors.Count);
        Assert.Single(errors, l => l.EventId.Id == UnhandledExceptionEventId);
        CapturedLog responseError = Assert.Single(errors, l => l.EventId.Id == ResponseErrorEventId);
        Assert.Equal(500, responseError.Properties["StatusCode"]);
        Assert.Contains(FixedBody, responseError.Properties["ResponseBody"]?.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task PlannedFailure_PassesThroughUnchanged()
    {
        const string plannedBody =
            "{\"statusCode\":400,\"message\":\"One or more errors occurred!\",\"errors\":{\"email\":[\"Email is required.\"]}}";
        await using Pipeline pipeline = await Pipeline.StartAsync(async context =>
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            context.Response.ContentType = "application/problem+json";
            await context.Response.WriteAsync(plannedBody);
        });

        HttpResponseMessage response = await pipeline.Client.GetAsync("/api/test");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(plannedBody, await response.Content.ReadAsStringAsync());
        Assert.DoesNotContain(pipeline.Logs.All, l => l.EventId.Id == UnhandledExceptionEventId);
    }

    [Fact]
    public async Task StartedResponse_BehindRequestLog_StillGetsTheFixed500()
    {
        await using Pipeline pipeline = await Pipeline.StartAsync(async context =>
        {
            await context.Response.StartAsync();
            await context.Response.WriteAsync("partial");
            throw new InvalidOperationException("late failure at 10.0.0.5");
        });

        HttpResponseMessage response = await pipeline.Client.GetAsync("/api/test");
        string body = await response.Content.ReadAsStringAsync();

        // The request log swaps the body for a buffer, and starting a buffered response does not start the real one.
        // So behind it the handler never sees a started response: it clears the partial body and sends the fixed 500.
        // The abort branch is for a pipeline without that buffer (ExceptionHandlingMiddlewareTests).
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Contains(FixedBody, body, StringComparison.Ordinal);
        Assert.DoesNotContain("partial", body, StringComparison.Ordinal);
        Assert.Single(pipeline.Logs.All, l => l.EventId.Id == UnhandledExceptionEventId);
        Assert.False(pipeline.Logs.ContainsText("10.0.0.5"));
    }

    [Fact]
    public async Task ApiKeyLookupFailure_Returns500AndOneUnhandledException()
    {
        await using Pipeline pipeline = await StartWithFailingKeyLookupAsync();

        HttpResponseMessage? response = null;
        Exception? escaped = await Record.ExceptionAsync(async () =>
            response = await pipeline.Client.SendAsync(SensorUpload("sensor-key")));

        Assert.Null(escaped);
        Assert.NotNull(response);
        string body = await response.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Contains(FixedBody, body, StringComparison.Ordinal);
        Assert.DoesNotContain("10.0.0.5", body, StringComparison.Ordinal);
        CapturedLog unhandled = Assert.Single(pipeline.Logs.All, l => l.EventId.Id == UnhandledExceptionEventId);
        Assert.Equal("/api/measurements", unhandled.Properties["Path"]);
        Assert.False(pipeline.Logs.ContainsText("10.0.0.5"));
    }

    [Fact]
    public async Task UnknownApiKey_IsStillRefused()
    {
        await using Pipeline pipeline = await Pipeline.StartAsync(
            TerminalMustNotRun,
            keys => keys.FindActiveByKeyHashAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
                .Returns(Task.FromResult<ApiKey?>(null)));

        HttpResponseMessage response = await pipeline.Client.SendAsync(SensorUpload("unknown"));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.DoesNotContain(pipeline.Logs.All, l => l.EventId.Id == UnhandledExceptionEventId);
    }

    [Fact]
    public async Task MissingApiKey_IsStillRefused()
    {
        await using Pipeline pipeline = await Pipeline.StartAsync(TerminalMustNotRun);

        HttpResponseMessage response = await pipeline.Client.SendAsync(SensorUpload(apiKey: null));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.DoesNotContain(pipeline.Logs.All, l => l.EventId.Id == UnhandledExceptionEventId);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ApiKeyLookupFailure_RequestLogHasOneEntryWithoutFailureText(bool logResponseBody)
    {
        await using Pipeline pipeline = await StartWithFailingKeyLookupAsync(logResponseBody);

        await pipeline.Client.SendAsync(SensorUpload("sensor-key"));

        IReadOnlyList<CapturedLog> errors = [.. pipeline.Logs.All.Where(l => l.Level >= LogLevel.Error)];
        Assert.Equal(2, errors.Count);
        Assert.Single(errors, l => l.EventId.Id == UnhandledExceptionEventId);
        CapturedLog requestLog = Assert.Single(errors, l => l.EventId.Id == ResponseErrorEventId);
        Assert.Equal(500, requestLog.Properties["StatusCode"]);
        if (logResponseBody)
        {
            Assert.Contains(FixedBody, requestLog.Properties["ResponseBody"]?.ToString(), StringComparison.Ordinal);
        }

        // These keys appear only on the request log's own catch path, which a handled failure must not take.
        string attributes = requestLog.Properties["Attributes"]?.ToString() ?? string.Empty;
        Assert.DoesNotContain("ExceptionType", attributes, StringComparison.Ordinal);
        Assert.DoesNotContain("ExceptionMessage", attributes, StringComparison.Ordinal);
        Assert.False(pipeline.Logs.ContainsText("10.0.0.5"));
    }

    [Fact]
    public async Task ApiKeyLookupFailure_FromAllowedOrigin_KeepsCorsHeader()
    {
        await using Pipeline pipeline = await StartWithFailingKeyLookupAsync();

        HttpResponseMessage response = await pipeline.Client.SendAsync(SensorUpload("sensor-key", AllowedOrigin));

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal(AllowedOrigin, Assert.Single(response.Headers.GetValues("Access-Control-Allow-Origin")));
    }

    [Fact]
    public async Task EndpointFailure_FromAllowedOrigin_KeepsCorsHeader()
    {
        await using Pipeline pipeline = await Pipeline.StartAsync(async context =>
        {
            context.Response.Headers["X-Partial"] = "1";
            await context.Response.WriteAsync("partial");
            throw new InvalidOperationException("boom");
        });
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/test");
        request.Headers.Add("Origin", AllowedOrigin);

        HttpResponseMessage response = await pipeline.Client.SendAsync(request);

        // Clearing the response removed what the endpoint set, but not the CORS headers, which are added on start.
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal(AllowedOrigin, Assert.Single(response.Headers.GetValues("Access-Control-Allow-Origin")));
        Assert.False(response.Headers.Contains("X-Partial"));
    }

    [Fact]
    public async Task Failure_FromDisallowedOrigin_HasNoCorsHeader()
    {
        await using Pipeline pipeline = await StartWithFailingKeyLookupAsync();

        HttpResponseMessage response =
            await pipeline.Client.SendAsync(SensorUpload("sensor-key", "https://evil.example"));

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.False(response.Headers.Contains("Access-Control-Allow-Origin"));
    }

    private static Task TerminalMustNotRun(HttpContext _) =>
        throw new InvalidOperationException("terminal must not run");

    /// <summary>A host whose API-key lookup fails the way a database outage makes it fail.</summary>
    private static Task<Pipeline> StartWithFailingKeyLookupAsync(bool logResponseBody = true) =>
        Pipeline.StartAsync(
            TerminalMustNotRun,
            keys => keys.FindActiveByKeyHashAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
                .ThrowsAsync(new InvalidOperationException("Failed to connect to 10.0.0.5:5432")),
            logResponseBody);

    /// <summary>A sensor's reading upload, as the forwarder sends it.</summary>
    private static HttpRequestMessage SensorUpload(string? apiKey, string? origin = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/measurements")
        {
            Content = new StringContent("{}", System.Text.Encoding.UTF8, "application/json"),
        };
        if (apiKey is not null)
        {
            request.Headers.Add("X-API-Key", apiKey);
        }

        if (origin is not null)
        {
            request.Headers.Add("Origin", origin);
        }

        return request;
    }

    private sealed class Pipeline : IAsyncDisposable
    {
        private readonly WebApplication _app;

        private Pipeline(WebApplication app, CapturedLogs logs)
        {
            _app = app;
            Logs = logs;
            Client = app.GetTestClient();
        }

        public HttpClient Client { get; }

        public CapturedLogs Logs { get; }

        /// <summary>
        /// The API's real request steps (<see cref="RequestPipelineExtensions.UseRequestPipeline"/>), the real JWT and
        /// API-key sign-in handlers and the real FastEndpoints endpoints. Paths FastEndpoints doesn't map, such as
        /// <c>/api/test</c>, fall through to <paramref name="terminal"/>.
        /// </summary>
        public static async Task<Pipeline> StartAsync(
            RequestDelegate terminal,
            Action<IApiKeyRepository<ApiKey>>? arrangeKeys = null,
            bool logResponseBody = true)
        {
            var provider = new CapturingLoggerProvider();
            WebApplicationBuilder builder = WebApplication.CreateBuilder();
            builder.WebHost.UseTestServer();
            builder.Logging.ClearProviders();
            builder.Logging.AddProvider(provider);
            builder.Services.Configure<RequestLoggingOptions>(options =>
            {
                options.AttributesToLog = ["Path", "Method"];
                options.LogResponseBody = logResponseBody;
            });
            builder.Services.AddCors(options => options.AddPolicy(CorsPolicy, policy =>
                policy.WithOrigins(AllowedOrigin).AllowAnyHeader().AllowAnyMethod()));

            // As in AddInfrastructure: JWT is the default (in memory); the API-key scheme reads the key repository.
            builder.Services
                .AddAuthentication(options =>
                {
                    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
                    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
                })
                .AddJwtBearer(options => options.TokenValidationParameters = new TokenValidationParameters
                {
                    IssuerSigningKey = new SymmetricSecurityKey(new byte[32]),
                    ValidateIssuer = false,
                    ValidateAudience = false,
                })
                .AddScheme<ApiKeyAuthenticationOptions, ApiKeyAuthenticationHandler>(
                    ApiKeyAuthenticationOptions.DefaultScheme, _ => { });
            builder.Services.AddAuthorization();

            IApiKeyRepository<ApiKey> keys = Substitute.For<IApiKeyRepository<ApiKey>>();
            arrangeKeys?.Invoke(keys);
            builder.Services.AddSingleton(keys);
            builder.Services.AddSingleton(Substitute.For<IDispatcher>());
            builder.Services.AddFastEndpoints(options =>
                options.Assemblies = [typeof(IngestMeasurementsEndpoint).Assembly]);

            WebApplication app = builder.Build();
            app.UseRequestPipeline(CorsPolicy);
            app.UseFastEndpoints();
            app.Run(terminal);
            await app.StartAsync();

            return new Pipeline(app, provider.Logs);
        }

        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            await _app.DisposeAsync();
        }
    }
}
