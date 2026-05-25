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

    private static readonly Action<ILogger, Guid, Exception?> _userDeleted =
        LoggerMessage.Define<Guid>(
            LogLevel.Information,
            new EventId(1001, nameof(UserDeleted)),
            "User deleted: {UserId}");

    private static readonly Action<ILogger, Guid, string, Exception?> _userUpdated =
        LoggerMessage.Define<Guid, string>(
            LogLevel.Information,
            new EventId(1002, nameof(UserUpdated)),
            "User updated: {UserId} {Changes}");

    private static readonly Action<ILogger, Guid, Exception?> _userNotFound =
        LoggerMessage.Define<Guid>(
            LogLevel.Warning,
            new EventId(1004, nameof(UserNotFound)),
            "User not found: {UserId}");

    private static readonly Action<ILogger, string, string, int, Exception?> _userUpdateFailed =
        LoggerMessage.Define<string, string, int>(
            LogLevel.Warning,
            new EventId(1005, nameof(UserUpdateFailed)),
            "User update failed for: {Email} Reason: {Reason} ReasonId: {ReasonId}");

    // User creation/update failures
    private static readonly Action<ILogger, string, string, int, Exception?> _userCreateFailed =
        LoggerMessage.Define<string, string, int>(
            LogLevel.Warning,
            new EventId(1003, nameof(UserCreated)),
            "User creation failed for: {Email} Reason: {Reason} ReasonId: {ReasonId}");

    public static void UserCreated(ILogger logger, Guid userId, string email)
        => _userCreated(logger, userId, email, null);

    public static void UserDeleted(ILogger logger, Guid userId)
        => _userDeleted(logger, userId, null);

    public static void UserUpdated(ILogger logger, Guid userId, string changes)
        => _userUpdated(logger, userId, changes, null);

    public static void UserCreateFailed(ILogger logger, string email, string reason)
        => _userCreateFailed(logger, email, reason ?? string.Empty, 0, null);

    public static void UserCreateFailed(ILogger logger, string email, string reason, int reasonId)
        => _userCreateFailed(logger, email, reason ?? string.Empty, reasonId, null);

    public static void UserNotFound(ILogger logger, Guid userId)
        => _userNotFound(logger, userId, null);

    public static void UserUpdateFailed(ILogger logger, string email, string reason, int reasonId)
        => _userUpdateFailed(logger, email ?? string.Empty, reason ?? string.Empty, reasonId, null);
}
