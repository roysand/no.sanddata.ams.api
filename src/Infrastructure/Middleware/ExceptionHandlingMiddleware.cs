using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Infrastructure.Middleware;

/// <summary>
/// Middleware to handle exceptions globally and return appropriate HTTP responses.
/// </summary>
public class ExceptionHandlingMiddleware
{
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
        catch (Exception ex)
        {
            _logger.LogError(ex, "An unhandled exception occurred.");
            await HandleExceptionAsync(context, ex);
        }
    }

    private static Task HandleExceptionAsync(HttpContext context, Exception exception)
    {
        HttpStatusCode code = HttpStatusCode.InternalServerError; // 500 by default

        // Determine the HTTP status code based on exception type
        switch (exception)
        {
            case ArgumentException _:
                code = HttpStatusCode.BadRequest; // 400
                break;
            case InvalidOperationException _:
                code = HttpStatusCode.BadRequest; // 400
                break;
            case KeyNotFoundException _:
                code = HttpStatusCode.NotFound; // 404
                break;
            case UnauthorizedAccessException _:
                code = HttpStatusCode.Unauthorized; // 401
                break;
            // Add more exception type mappings as needed
        }

        string result = JsonSerializer.Serialize(new { error = exception.Message });
        context.Response.ContentType = "application/json";
        context.Response.StatusCode = (int)code;
        return context.Response.WriteAsync(result);
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
