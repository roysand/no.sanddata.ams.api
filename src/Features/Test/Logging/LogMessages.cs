using System;
using Microsoft.Extensions.Logging;

namespace Features.Test.Logging;

internal static class LogMessages
{
    // Test: event ids 1200 - 1299
    private static readonly Action<ILogger, string, Exception?> _testEvent =
        LoggerMessage.Define<string>(
            LogLevel.Debug,
            new EventId(1200, nameof(TestEvent)),
            "Test event: {Detail}");

    private static readonly Action<ILogger, Guid, string, Exception?> _testStarted =
        LoggerMessage.Define<Guid, string>(
            LogLevel.Information,
            new EventId(1201, nameof(TestStarted)),
            "Test started: {TestId} {Name}");

    private static readonly Action<ILogger, Guid, double, Exception?> _testCompleted =
        LoggerMessage.Define<Guid, double>(
            LogLevel.Information,
            new EventId(1202, nameof(TestCompleted)),
            "Test completed: {TestId} {DurationMs}");

    private static readonly Action<ILogger, Guid, string, Exception?> _testFailed =
        LoggerMessage.Define<Guid, string>(
            LogLevel.Error,
            new EventId(1203, nameof(TestFailed)),
            "Test failed: {TestId} {Error}");

    public static void TestEvent(ILogger logger, string detail)
        => _testEvent(logger, detail, null);

    public static void TestStarted(ILogger logger, Guid testId, string name)
        => _testStarted(logger, testId, name, null);

    public static void TestCompleted(ILogger logger, Guid testId, double durationMs)
        => _testCompleted(logger, testId, durationMs, null);

    public static void TestFailed(ILogger logger, Guid testId, string error)
        => _testFailed(logger, testId, error, null);
}
