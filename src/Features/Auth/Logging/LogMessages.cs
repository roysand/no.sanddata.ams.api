using System;
using Microsoft.Extensions.Logging;

namespace Features.Auth.Logging;

internal static class LogMessages
{
    // Auth: event ids 1100 - 1199
    private static readonly Action<ILogger, string, Exception?> _userLoggedIn =
        LoggerMessage.Define<string>(
            LogLevel.Information,
            new EventId(1100, nameof(UserLoggedIn)),
            "User logged in: {Email}");

    public static void UserLoggedIn(ILogger logger, string email)
        => _userLoggedIn(logger, email, null);
}

