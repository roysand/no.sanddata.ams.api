using System;
using Microsoft.Extensions.Logging;

namespace Infrastructure.Logging;

internal static class LogMessages
{
    // Infra/Logging event ids 2000 - 2099
    private static readonly Action<ILogger, string, string, string, Exception?> _requestReceived =
        LoggerMessage.Define<string, string, string>(
            LogLevel.Information,
            new EventId(2000, nameof(RequestReceived)),
            "Request {Method} {Path} {Attributes}");

    private static readonly Action<ILogger, int, string, Exception?> _responseSent =
        LoggerMessage.Define<int, string>(
            LogLevel.Information,
            new EventId(2001, nameof(ResponseSent)),
            "Response {StatusCode} {Attributes}");

    // Error-level response message for failed requests
    private static readonly Action<ILogger, int, string, string, Exception?> _responseError =
        LoggerMessage.Define<int, string, string>(
            LogLevel.Error,
            new EventId(2002, nameof(ResponseError)),
            "Response error {StatusCode} {Attributes} {ResponseBody}");

    private static readonly Action<ILogger, Exception?> _migrationsStarting =
        LoggerMessage.Define(
            LogLevel.Information,
            new EventId(2010, nameof(MigrationsStarting)),
            "Applying database migrations (RunMigrationsAtStartup=true)");

    private static readonly Action<ILogger, Exception?> _migrationsCompleted =
        LoggerMessage.Define(
            LogLevel.Information,
            new EventId(2011, nameof(MigrationsCompleted)),
            "Database migrations applied");

    private static readonly Action<ILogger, Exception?> _migrationsSkipped =
        LoggerMessage.Define(
            LogLevel.Information,
            new EventId(2012, nameof(MigrationsSkipped)),
            "Database migrations skipped at startup (RunMigrationsAtStartup=false)");

    public static void MigrationsStarting(ILogger logger) => _migrationsStarting(logger, null);

    public static void MigrationsCompleted(ILogger logger) => _migrationsCompleted(logger, null);

    public static void MigrationsSkipped(ILogger logger) => _migrationsSkipped(logger, null);

    public static void RequestReceived(ILogger logger, string method, string path, string attributesJson)
        => _requestReceived(logger, method, path, attributesJson, null);

    public static void ResponseSent(ILogger logger, int statusCode, string attributesJson)
        => _responseSent(logger, statusCode, attributesJson, null);

    public static void ResponseError(ILogger logger, int statusCode, string attributesJson, string responseBody)
        => _responseError(logger, statusCode, attributesJson, responseBody ?? string.Empty, null);
}
