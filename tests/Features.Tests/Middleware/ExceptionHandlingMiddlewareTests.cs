using System.Text;
using System.Text.Json;
using Infrastructure.Middleware;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;

namespace Features.Tests.Middleware;

/// <summary>
/// The global exception handler on its own (spec 013): what the caller receives and what is logged for each kind of
/// failure. The real middleware order is covered by <see cref="ExceptionPipelineTests"/>.
/// </summary>
public class ExceptionHandlingMiddlewareTests
{
    private const string FixedMessage = "An unexpected error occurred.";
    private const int UnhandledExceptionEventId = 2020;

    [Fact]
    public async Task UnexpectedException_Returns500WithFixedBody()
    {
        Outcome outcome = await RunAsync(_ => throw new Exception("boom"));

        Assert.Equal(StatusCodes.Status500InternalServerError, outcome.Context.Response.StatusCode);
        Assert.Equal("application/problem+json", outcome.Context.Response.ContentType);
        Assert.Equal("no-cache, no-store", outcome.Context.Response.Headers.CacheControl.ToString());

        using var body = JsonDocument.Parse(outcome.Body);
        string[] properties = [.. body.RootElement.EnumerateObject().Select(p => p.Name)];
        Assert.Equal(["statusCode", "message", "traceId"], properties);
        Assert.Equal(500, body.RootElement.GetProperty("statusCode").GetInt32());
        Assert.Equal(FixedMessage, body.RootElement.GetProperty("message").GetString());
        Assert.Equal(outcome.Context.TraceIdentifier, body.RootElement.GetProperty("traceId").GetString());
    }

    [Fact]
    public async Task ExceptionText_NeverReachesTheBody()
    {
        Outcome outcome = await RunAsync(_ =>
            throw new InvalidOperationException("Host=db.internal;Port=5432", new Exception("inner-secret")));

        Assert.DoesNotContain("db.internal", outcome.Body, StringComparison.Ordinal);
        Assert.DoesNotContain("5432", outcome.Body, StringComparison.Ordinal);
        Assert.DoesNotContain("inner-secret", outcome.Body, StringComparison.Ordinal);
        Assert.DoesNotContain(nameof(InvalidOperationException), outcome.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PartialBody_IsDiscarded()
    {
        Outcome outcome = await RunAsync(async context =>
        {
            context.Response.Headers["X-Partial"] = "1";
            await context.Response.WriteAsync("{\"partial\":");
            throw new Exception("half way");
        });

        Assert.StartsWith("{\"statusCode\":500", outcome.Body, StringComparison.Ordinal);
        Assert.DoesNotContain("partial", outcome.Body, StringComparison.Ordinal);
        Assert.False(outcome.Context.Response.Headers.ContainsKey("X-Partial"));
    }

    public static TheoryData<Func<Exception>> FormerlyClientErrorTypes() =>
    [
        () => new InvalidOperationException("Sequence contains no elements"),
        () => new ArgumentException("bad"),
        () => new ArgumentNullException("value"),
        () => new KeyNotFoundException("missing"),
        () => new UnauthorizedAccessException("denied"),
        () => new NotSupportedException("nope"),
        () => new Exception("outer", new ArgumentException("inner")),
    ];

    [Theory]
    [MemberData(nameof(FormerlyClientErrorTypes))]
    public async Task AnyExceptionType_Returns500(Func<Exception> create)
    {
        Outcome outcome = await RunAsync(_ => throw create());

        Assert.Equal(StatusCodes.Status500InternalServerError, outcome.Context.Response.StatusCode);
        Assert.Single(outcome.Logs.All, l => l.EventId.Id == UnhandledExceptionEventId);
    }

    [Fact]
    public async Task CallerDisconnect_LogsNoErrorAndWritesNothing()
    {
        using var aborted = new CancellationTokenSource();
        await aborted.CancelAsync();

        Outcome outcome = await RunAsync(
            _ => throw new OperationCanceledException(aborted.Token),
            context => context.RequestAborted = aborted.Token);

        Assert.Equal(StatusCodes.Status200OK, outcome.Context.Response.StatusCode);
        Assert.Empty(outcome.Body);
        Assert.DoesNotContain(outcome.Logs.All, l => l.Level >= Microsoft.Extensions.Logging.LogLevel.Error);
    }

    [Fact]
    public async Task InternalCancellation_Returns500()
    {
        Outcome outcome = await RunAsync(_ => throw new TaskCanceledException("HttpClient timeout"));

        Assert.Equal(StatusCodes.Status500InternalServerError, outcome.Context.Response.StatusCode);
        Assert.Single(outcome.Logs.All, l => l.EventId.Id == UnhandledExceptionEventId);
    }

    [Fact]
    public async Task StartedResponse_IsAbortedNotWritten()
    {
        var lifetime = new RecordingLifetimeFeature();

        Outcome outcome = await RunAsync(
            async context =>
            {
                await context.Response.WriteAsync("partial");
                throw new Exception("late");
            },
            context =>
            {
                context.Features.Set<IHttpResponseFeature>(new StartedResponseFeature());
                context.Features.Set<IHttpRequestLifetimeFeature>(lifetime);
            });

        Assert.Equal(1, lifetime.AbortCount);
        Assert.Equal("partial", outcome.Body);
        Assert.NotEqual(StatusCodes.Status500InternalServerError, outcome.Context.Response.StatusCode);
        Assert.Single(outcome.Logs.All, l => l.EventId.Id == UnhandledExceptionEventId);
    }

    [Fact]
    public async Task UnhandledException_LogEntryHasDocumentedShape()
    {
        var thrown = new InvalidOperationException("x");

        Outcome outcome = await RunAsync(
            _ => throw thrown,
            context =>
            {
                context.Request.QueryString = new QueryString("?email=someone@example.com");
                context.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes("{\"password\":\"hunter2\"}"));
            });

        CapturedLog entry = Assert.Single(outcome.Logs.All);
        Assert.Equal(Microsoft.Extensions.Logging.LogLevel.Error, entry.Level);
        Assert.Equal(UnhandledExceptionEventId, entry.EventId.Id);
        Assert.Equal("UnhandledException", entry.EventId.Name);
        Assert.Equal("UnhandledException", entry.Properties["ReasonCode"]);
        Assert.Equal("System.InvalidOperationException", entry.Properties["ExceptionType"]);
        Assert.Equal("POST", entry.Properties["Method"]);
        Assert.Equal("/api/test", entry.Properties["Path"]);
        Assert.Same(thrown, entry.Exception);

        using var body = JsonDocument.Parse(outcome.Body);
        Assert.Equal(body.RootElement.GetProperty("traceId").GetString(), entry.Properties["TraceId"]);

        Assert.False(outcome.Logs.ContainsText("someone@example.com"));
        Assert.False(outcome.Logs.ContainsText("hunter2"));
    }

    private static async Task<Outcome> RunAsync(RequestDelegate next, Action<HttpContext>? arrange = null)
    {
        var context = new DefaultHttpContext();
        context.Request.Method = HttpMethods.Post;
        context.Request.Path = "/api/test";
        context.Response.Body = new MemoryStream();
        arrange?.Invoke(context);

        (Microsoft.Extensions.Logging.ILogger<ExceptionHandlingMiddleware> logger, CapturedLogs logs) =
            CapturingLoggerProvider.CreateLogger<ExceptionHandlingMiddleware>();

        await new ExceptionHandlingMiddleware(next, logger).InvokeAsync(context);

        context.Response.Body.Position = 0;
        string body = await new StreamReader(context.Response.Body, Encoding.UTF8).ReadToEndAsync();
        return new Outcome(context, body, logs);
    }

    private sealed record Outcome(HttpContext Context, string Body, CapturedLogs Logs);

    /// <summary>A response whose status and headers have already gone to the caller.</summary>
    private sealed class StartedResponseFeature : HttpResponseFeature
    {
        public override bool HasStarted => true;
    }

    private sealed class RecordingLifetimeFeature : IHttpRequestLifetimeFeature
    {
        public int AbortCount { get; private set; }

        public CancellationToken RequestAborted { get; set; } = CancellationToken.None;

        public void Abort() => AbortCount++;
    }
}
