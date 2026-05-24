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

    public static void TestEvent(ILogger logger, string detail)
        => _testEvent(logger, detail, null);
}

