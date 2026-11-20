using System.Net;
using Infrastructure.Logging;
using Infrastructure.Middleware;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Features.Tests.Middleware;

/// <summary>
/// The request log and the exception handler in the order <c>Program.cs</c> uses: the request log outside, the
/// handler inside. Checks that neither the response nor any log message carries the exception text (spec 013,
/// FR-009).
/// </summary>
public class ExceptionPipelineTests
{
    private const string FixedBody = "\"message\":\"An unexpected error occurred.\"";
    private const int UnhandledExceptionEventId = 2020;
    private const int ResponseErrorEventId = 2002;

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

        public static async Task<Pipeline> StartAsync(RequestDelegate terminal)
        {
            var provider = new CapturingLoggerProvider();
            WebApplicationBuilder builder = WebApplication.CreateBuilder();
            builder.WebHost.UseTestServer();
            builder.Logging.ClearProviders();
            builder.Logging.AddProvider(provider);
            builder.Services.Configure<RequestLoggingOptions>(options =>
            {
                options.AttributesToLog = ["Path", "Method"];
                options.LogResponseBody = true;
            });

            WebApplication app = builder.Build();
            app.UseMiddleware<RequestResponseLoggingMiddleware>();
            app.UseExceptionHandling();
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
