using Microsoft.Extensions.Logging;

namespace Features.Tests.Middleware;

/// <summary>One log call, as the logger saw it.</summary>
internal sealed record CapturedLog(
    LogLevel Level,
    EventId EventId,
    string Message,
    IReadOnlyDictionary<string, object?> Properties,
    Exception? Exception);

/// <summary>The entries a <see cref="CapturingLoggerProvider"/> has recorded, from every logger it created.</summary>
internal sealed class CapturedLogs
{
    private readonly List<CapturedLog> _entries = [];
    private readonly Lock _gate = new();

    public IReadOnlyList<CapturedLog> All
    {
        get
        {
            lock (_gate)
            {
                return [.. _entries];
            }
        }
    }

    public void Add(CapturedLog entry)
    {
        lock (_gate)
        {
            _entries.Add(entry);
        }
    }

    /// <summary>
    /// Whether any entry's message or property values contain <paramref name="text"/>. The attached exception is not
    /// searched: the log is allowed to carry the exception, a message or property is not.
    /// </summary>
    public bool ContainsText(string text) => All.Any(entry =>
        entry.Message.Contains(text, StringComparison.Ordinal)
        || entry.Properties.Values.Any(value => value?.ToString()?.Contains(text, StringComparison.Ordinal) == true));
}

/// <summary>Records every log call in memory, so tests can check event ids, levels and properties.</summary>
internal sealed class CapturingLoggerProvider : ILoggerProvider
{
    public CapturedLogs Logs { get; } = new();

    public ILogger CreateLogger(string categoryName) => new CapturingLogger(Logs);

    public void Dispose()
    {
    }

    /// <summary>A typed logger for a unit test, and the entries it records.</summary>
    public static (ILogger<T> Logger, CapturedLogs Logs) CreateLogger<T>()
    {
        var provider = new CapturingLoggerProvider();
        ILoggerFactory factory = LoggerFactory.Create(builder => builder
            .SetMinimumLevel(LogLevel.Trace)
            .AddProvider(provider));
        return (factory.CreateLogger<T>(), provider.Logs);
    }

    private sealed class CapturingLogger(CapturedLogs logs) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            var properties = new Dictionary<string, object?>(StringComparer.Ordinal);
            if (state is IReadOnlyList<KeyValuePair<string, object?>> pairs)
            {
                foreach (KeyValuePair<string, object?> pair in pairs)
                {
                    if (pair.Key != "{OriginalFormat}")
                    {
                        properties[pair.Key] = pair.Value;
                    }
                }
            }

            logs.Add(new CapturedLog(logLevel, eventId, formatter(state, exception), properties, exception));
        }
    }
}
