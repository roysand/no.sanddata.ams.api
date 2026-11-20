using System.Diagnostics;
using System.Text.Json;
using Infrastructure.Logging;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Infrastructure.Middleware;

/// <summary>
/// Catches every exception from the endpoints inward. Planned failures are <c>Result</c> values and validation errors,
/// which never get here, so any exception here is a server fault: it is logged with its details, and the caller gets a
/// fixed 500 with a trace id, never the exception text (spec 013).
/// </summary>
public class ExceptionHandlingMiddleware
{
    private const string FixedMessage = "An unexpected error occurred.";

    private readonly RequestDelegate _next;
    private readonly ILogger<ExceptionHandlingMiddleware> _logger;

    public ExceptionHandlingMiddleware(RequestDelegate next, ILogger<ExceptionHandlingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            // The caller left: not a server fault, and nobody is there to read an answer. Any other cancellation
            // (an internal timeout, say) falls through to the catch below.
        }
        catch (Exception ex)
        {
            string traceId = Activity.Current?.Id ?? context.TraceIdentifier;
            LogMessages.UnhandledException(
                _logger,
                ex.GetType().FullName ?? ex.GetType().Name,
                context.Request.Method,
                context.Request.Path.Value ?? string.Empty,
                traceId,
                ex);

            if (context.Response.HasStarted)
            {
                // Status and headers are already sent, so they can't be replaced. Aborting tells the caller the
                // response is incomplete; rethrowing would only log the same failure twice more.
                context.Abort();
                return;
            }

            await WriteErrorAsync(context, traceId);
        }
    }

    private static Task WriteErrorAsync(HttpContext context, string traceId)
    {
        // Drops whatever the endpoint had set or written before it threw: the request log buffers the body, so
        // part of it can already be there.
        context.Response.Clear();
        context.Response.StatusCode = StatusCodes.Status500InternalServerError;
        context.Response.ContentType = "application/problem+json";
        context.Response.Headers.CacheControl = "no-cache, no-store";

        // The API's usual error shape (statusCode, message), which the web app reads, plus the trace id.
        string body = JsonSerializer.Serialize(new
        {
            statusCode = StatusCodes.Status500InternalServerError,
            message = FixedMessage,
            traceId,
        });
        return context.Response.WriteAsync(body);
    }
}

/// <summary>
/// Extension method to add the exception handling middleware to the pipeline.
/// </summary>
public static class ExceptionHandlingMiddlewareExtensions
{
    public static IApplicationBuilder UseExceptionHandling(this IApplicationBuilder builder)
        => builder.UseMiddleware<ExceptionHandlingMiddleware>();
}
