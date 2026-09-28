using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Infrastructure.Logging;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;

namespace Infrastructure.Middleware;

public class RequestResponseLoggingMiddleware
{
    private static readonly HashSet<string> SensitiveHeaders = new(StringComparer.OrdinalIgnoreCase)
    {
        "Authorization",
        "X-API-Key",
    };

    private readonly RequestDelegate _next;
    private readonly ILogger<RequestResponseLoggingMiddleware> _logger;
    private readonly RequestLoggingOptions _options;

    public RequestResponseLoggingMiddleware(RequestDelegate next, ILogger<RequestResponseLoggingMiddleware> logger, IOptions<RequestLoggingOptions> options)
    {
        _next = next ?? throw new ArgumentNullException(nameof(next));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _options = options?.Value ?? new RequestLoggingOptions();
    }

    public async Task InvokeAsync(HttpContext context)
    {
        IDictionary<string, object?> attributes = BuildAttributeMap(context);
        string attributesJson = JsonSerializer.Serialize(attributes);

        LogMessages.RequestReceived(_logger, context.Request.Method, context.Request.Path + context.Request.QueryString, attributesJson);

        // Optionally capture request body
        string? requestBody = null;
        if (_options.LogRequestBody)
        {
            context.Request.EnableBuffering();
            using var reader = new StreamReader(context.Request.Body, Encoding.UTF8, leaveOpen: true);
            requestBody = await reader.ReadToEndAsync();
            context.Request.Body.Position = 0;
            attributes["RequestBody"] = requestBody;
            attributesJson = JsonSerializer.Serialize(attributes);
        }

        // Capture response by swapping the Body stream
        Stream originalBodyStream = context.Response.Body;
        await using var responseBody = new MemoryStream();
        context.Response.Body = responseBody;

        try
        {
            await _next(context);

            context.Response.Body.Seek(0, SeekOrigin.Begin);
            string? responseText = null;
            if (_options.LogResponseBody)
            {
                using var sr = new StreamReader(context.Response.Body, Encoding.UTF8, leaveOpen: true);
                responseText = await sr.ReadToEndAsync();
                context.Response.Body.Seek(0, SeekOrigin.Begin);
                attributes["ResponseBody"] = responseText;
                attributesJson = JsonSerializer.Serialize(attributes);
            }

            // If response indicates failure, log as error with more detail
            if (context.Response.StatusCode >= 400)
            {
                LogMessages.ResponseError(_logger, context.Response.StatusCode, attributesJson, responseText ?? string.Empty);
            }
            else
            {
                LogMessages.ResponseSent(_logger, context.Response.StatusCode, attributesJson);
            }

            // copy response back to original stream
            context.Response.Body.Seek(0, SeekOrigin.Begin);
            await context.Response.Body.CopyToAsync(originalBodyStream);
        }
        catch (Exception ex)
        {
            // Log exception details and rethrow
            attributes["ExceptionType"] = ex.GetType().FullName;
            attributes["ExceptionMessage"] = ex.Message;
            attributesJson = JsonSerializer.Serialize(attributes);

            LogMessages.ResponseError(_logger, 500, attributesJson, string.Empty);

            throw;
        }
        finally
        {
            context.Response.Body = originalBodyStream;
        }
    }

    private IDictionary<string, object?> BuildAttributeMap(HttpContext context)
    {
        var attributes = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["Path"] = context.Request.Path.ToString(),
            ["Method"] = context.Request.Method,
            ["QueryString"] = context.Request.QueryString.HasValue ? context.Request.QueryString.Value : string.Empty,
            ["UserAgent"] = context.Request.Headers.TryGetValue("User-Agent", out StringValues ua) ? ua.ToString() : string.Empty,
            ["RemoteIp"] = context.Connection.RemoteIpAddress?.ToString() ?? string.Empty,
            ["Headers"] = context.Request.Headers.ToDictionary(
                h => h.Key,
                h => (object?)(SensitiveHeaders.Contains(h.Key) ? _options.MaskValue : h.Value.ToString()))
        };

        // Claims
        ClaimsPrincipal? user = context.User;
        if (user?.Identity?.IsAuthenticated == true)
        {
            string? userId = user.Claims.FirstOrDefault(c => c.Type == System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            string? email = user.Claims.FirstOrDefault(c => c.Type == System.Security.Claims.ClaimTypes.Email)?.Value;
            attributes["UserId"] = userId ?? string.Empty;
            attributes["Email"] = email ?? string.Empty;
        }
        else
        {
            attributes["UserId"] = string.Empty;
            attributes["Email"] = string.Empty;
        }

        return attributes;
    }
}

