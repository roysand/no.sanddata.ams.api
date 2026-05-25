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

    private static readonly Action<ILogger, string, Exception?> _loginFailed =
        LoggerMessage.Define<string>(
            LogLevel.Warning,
            new EventId(1101, nameof(LoginFailed)),
            "Login failed for: {Email}");

    // Login failed with reason code and optional numeric id
    private static readonly Action<ILogger, string, string, int, Exception?> _loginFailedWithReasonAndId =
        LoggerMessage.Define<string, string, int>(
            LogLevel.Warning,
            new EventId(1103, nameof(LoginFailed)),
            "Login failed for: {Email} Reason: {ReasonCode} ReasonId: {ReasonId}");

    private static readonly Action<ILogger, Guid, Exception?> _tokenRefreshed =
        LoggerMessage.Define<Guid>(
            LogLevel.Information,
            new EventId(1102, nameof(TokenRefreshed)),
            "Token refreshed for: {UserId}");

    private static readonly Action<ILogger, string, string, int, Exception?> _refreshTokenFailed =
        LoggerMessage.Define<string, string, int>(
            LogLevel.Warning,
            new EventId(1104, nameof(RefreshTokenFailed)),
            "Refresh token failed for: {RefreshToken} Reason: {ReasonCode} ReasonId: {ReasonId}");

    public static void UserLoggedIn(ILogger logger, string email)
        => _userLoggedIn(logger, email, null);

    public static void LoginFailed(ILogger logger, string email)
        => _loginFailed(logger, email, null);

    public static void LoginFailed(ILogger logger, string email, string reasonCode)
        => _loginFailedWithReasonAndId(logger, email, reasonCode ?? string.Empty, 0, null);

    public static void LoginFailed(ILogger logger, string email, string reasonCode, int reasonId)
        => _loginFailedWithReasonAndId(logger, email, reasonCode ?? string.Empty, reasonId, null);

    public static void TokenRefreshed(ILogger logger, Guid userId)
        => _tokenRefreshed(logger, userId, null);

    public static void RefreshTokenFailed(ILogger logger, string refreshToken, string reasonCode, int reasonId)
        => _refreshTokenFailed(logger, refreshToken ?? string.Empty, reasonCode ?? string.Empty, reasonId, null);
}
