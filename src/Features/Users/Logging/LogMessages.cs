using System;
using Microsoft.Extensions.Logging;

namespace Features.Users.Logging;

internal static class LogMessages
{
    // Users: event ids 1000 - 1099
    private static readonly Action<ILogger, Guid, string, Exception?> _userCreated =
        LoggerMessage.Define<Guid, string>(
            LogLevel.Information,
            new EventId(1000, nameof(UserCreated)),
            "User created: {UserId} {Email}");

    public static void UserCreated(ILogger logger, Guid userId, string email)
        => _userCreated(logger, userId, email, null);
}

