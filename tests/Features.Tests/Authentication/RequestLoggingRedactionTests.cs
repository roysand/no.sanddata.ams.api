using System.Text;
using Infrastructure.Logging;
using Infrastructure.Middleware;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Features.Tests.Authentication;

public class RequestLoggingRedactionTests
{
    private sealed class CapturingLogger(List<string> lines) : ILogger<RequestResponseLoggingMiddleware>
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) => lines.Add(formatter(state, exception));
    }

    private const string SecretKey = "super-secret-sensor-key-0123456789";

    [Fact]
    public void RedactHeaders_MasksCredentialsAndKeepsOthers()
    {
        var headers = new HeaderDictionary
        {
            ["Authorization"] = "Bearer abc.def.ghi",
            ["X-API-Key"] = SecretKey,
            ["x-api-key"] = SecretKey,
            ["User-Agent"] = "test-agent"
        };

        Dictionary<string, object?> result = SensitiveData.RedactHeaders(headers, "***");

        Assert.Equal("***", result["Authorization"]);
        Assert.Equal("***", result["X-API-Key"]);
        Assert.Equal("test-agent", result["User-Agent"]);
        Assert.DoesNotContain(result.Values, v => v?.ToString()?.Contains(SecretKey) == true);
    }

    [Theory]
    [InlineData("POST", "/api/admin/locations", true)]
    [InlineData("post", "/API/Admin/Locations/", true)]
    [InlineData("POST", "/api/admin/locations/3fa85f64-5717-4562-b3fc-2c963f66afa6/api-key/rotate", true)]
    [InlineData("GET", "/api/admin/locations", false)]
    [InlineData("PUT", "/api/admin/locations/3fa85f64-5717-4562-b3fc-2c963f66afa6", false)]
    [InlineData("PUT", "/api/admin/locations/3fa85f64-5717-4562-b3fc-2c963f66afa6/api-key", false)]
    [InlineData("POST", "/api/meters", false)]
    [InlineData("POST", "/api/measurements", false)]
    public void RevealsApiKey_OnlyForTheTwoKeyIssuingRequests(string method, string path, bool expected)
    {
        Assert.Equal(expected, SensitiveData.RevealsApiKey(method, new PathString(path)));
    }

    private static async Task<List<string>> RunAsync(string method, string path, string responseBody, bool allowHeaders)
    {
        var lines = new List<string>();
        var logger = new CapturingLogger(lines);

        var options = Options.Create(new RequestLoggingOptions
        {
            AttributesToLog = allowHeaders ? ["Headers", "Path", "Method", "ResponseBody"] : ["Path", "Method"],
            LogResponseBody = true
        });

        var middleware = new RequestResponseLoggingMiddleware(async ctx =>
        {
            await ctx.Response.WriteAsync(responseBody);
        }, logger, options);

        var context = new DefaultHttpContext();
        context.Request.Method = method;
        context.Request.Path = path;
        context.Request.Headers["X-API-Key"] = SecretKey;
        context.Request.Headers["Authorization"] = "Bearer " + SecretKey;
        context.Response.Body = new MemoryStream();

        await middleware.InvokeAsync(context);
        return lines;
    }

    [Fact]
    public async Task Middleware_HeadersAllowed_StillNeverLogsCredentialHeaders()
    {
        List<string> lines = await RunAsync("GET", "/api/locations", "[]", allowHeaders: true);

        Assert.NotEmpty(lines);
        Assert.DoesNotContain(lines, l => l.Contains(SecretKey));
    }

    [Fact]
    public async Task Middleware_ResponseBodyLogging_MasksTheKeyIssuingResponses()
    {
        string body = "{\"apiKey\":\"" + SecretKey + "\"}";

        List<string> create = await RunAsync("POST", "/api/admin/locations", body, allowHeaders: true);
        List<string> rotate = await RunAsync(
            "POST", "/api/admin/locations/3fa85f64-5717-4562-b3fc-2c963f66afa6/api-key/rotate", body, allowHeaders: true);

        Assert.DoesNotContain(create, l => l.Contains(SecretKey));
        Assert.DoesNotContain(rotate, l => l.Contains(SecretKey));
    }

    [Fact]
    public async Task Middleware_ResponseBodyLogging_StillLogsOtherResponses()
    {
        List<string> lines = await RunAsync("GET", "/api/locations", "{\"hello\":\"world\"}", allowHeaders: true);

        Assert.Contains(lines, l => l.Contains("hello"));
    }
}
