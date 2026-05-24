using System.Collections.Concurrent;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace RequestLogging.Tests;

public class RequestLoggingTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public RequestLoggingTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(builder =>
        {
            // Configure in-memory config for RequestLogging
            builder.ConfigureAppConfiguration((context, cfg) =>
            {
                var dict = new System.Collections.Generic.Dictionary<string, string?>
                {
                    ["RequestLogging:AttributesToLog:0"] = "Path",
                    ["RequestLogging:AttributesToLog:1"] = "Method",
                    ["RequestLogging:MaskValue"] = "***",
                    ["RequestLogging:LogRequestBody"] = "false",
                    ["RequestLogging:LogResponseBody"] = "false",
                };
                cfg.AddInMemoryCollection(dict);
            });
        });
    }

    [Fact]
    public async Task Middleware_Logs_Request_And_Masks_Unlisted_Attributes()
    {
        var logs = new ConcurrentBag<string>();

        var factory = _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureLogging(ltb =>
            {
                ltb.ClearProviders();
                ltb.AddProvider(new TestLoggerProvider(logs));
            });
        });

        var client = factory.CreateClient();
        var resp = await client.GetAsync("/weatherforecast");
        resp.EnsureSuccessStatusCode();

        // Wait briefly to ensure logs flushed
        await Task.Delay(100);

        var anyRequest = logs.Any(l => l.Contains("Request ") && l.Contains("/weatherforecast"));
        var anyResponse = logs.Any(l => l.Contains("Response ") && l.Contains("/weatherforecast") == false || l.Contains("Response ")); // just check presence

        Assert.True(anyRequest, "Expected request log entry containing /weatherforecast");

        // Check that masked UserId is present
        var hasMaskedUserId = logs.Any(l => l.Contains("\"UserId\":\"***\""));
        Assert.True(hasMaskedUserId, "Expected masked UserId in logged attributes");
    }

    private class TestLoggerProvider : ILoggerProvider
    {
        private readonly ConcurrentBag<string> _logs;

        public TestLoggerProvider(ConcurrentBag<string> logs) => _logs = logs;

        public ILogger CreateLogger(string categoryName) => new TestLogger(_logs);

        public void Dispose() { }

        private class TestLogger : ILogger
        {
            private readonly ConcurrentBag<string> _logs;
            public TestLogger(ConcurrentBag<string> logs) => _logs = logs;
            IDisposable ILogger.BeginScope<TState>(TState state) => NoopDisposable.Instance;
            public bool IsEnabled(LogLevel logLevel) => true;
            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, System.Exception? exception, System.Func<TState, System.Exception?, string> formatter)
            {
                _logs.Add(formatter(state, exception));
            }
        }
    }

    private sealed class NoopDisposable : System.IDisposable
    {
        public static readonly NoopDisposable Instance = new NoopDisposable();
        private NoopDisposable() { }
        public void Dispose() { }
    }
}

