using Microsoft.Extensions.Logging;

namespace Infrastructure.External.Logging;

/// <summary>
/// Zaptec client: event ids 1920 - 1929 (EvCharger area, 1900 - 1999). Entries name the call only ("token",
/// "chargers" or "sessions"). The token request holds the password and every other request the token, so no URL,
/// header, body, exception or message is ever logged.
/// </summary>
internal static class ZaptecLogMessages
{
    private static readonly Action<ILogger, string, int, Exception?> _requestFailed =
        LoggerMessage.Define<string, int>(
            LogLevel.Warning,
            new EventId(1920, nameof(RequestFailed)),
            "Zaptec request ({Call}) failed with HTTP {StatusCode}");

    private static readonly Action<ILogger, string, Exception?> _unavailable =
        LoggerMessage.Define<string>(
            LogLevel.Warning,
            new EventId(1921, nameof(Unavailable)),
            "Zaptec could not be reached ({Call}) (reason: Unavailable)");

    private static readonly Action<ILogger, string, Exception?> _invalidResponse =
        LoggerMessage.Define<string>(
            LogLevel.Warning,
            new EventId(1922, nameof(InvalidResponse)),
            "Zaptec answer ({Call}) could not be used (reason: InvalidResponse)");

    private static readonly Action<ILogger, Exception?> _signInRejected =
        LoggerMessage.Define(
            LogLevel.Warning,
            new EventId(1923, nameof(SignInRejected)),
            "Zaptec rejected the server's sign-in (reason: SignInRejected)");

    private static readonly Action<ILogger, string, Exception?> _rateLimited =
        LoggerMessage.Define<string>(
            LogLevel.Information,
            new EventId(1924, nameof(RateLimited)),
            "Zaptec asked to slow down ({Call}); retrying once (reason: RateLimited)");

    public static void RequestFailed(ILogger logger, string call, int statusCode) =>
        _requestFailed(logger, call, statusCode, null);

    public static void Unavailable(ILogger logger, string call) => _unavailable(logger, call, null);

    public static void InvalidResponse(ILogger logger, string call) => _invalidResponse(logger, call, null);

    public static void SignInRejected(ILogger logger) => _signInRejected(logger, null);

    public static void RateLimited(ILogger logger, string call) => _rateLimited(logger, call, null);
}
