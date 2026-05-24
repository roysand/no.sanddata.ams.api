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

    public static void RequestReceived(ILogger logger, string method, string path, string attributesJson)
        => _requestReceived(logger, method, path, attributesJson, null);

    public static void ResponseSent(ILogger logger, int statusCode, string attributesJson)
        => _responseSent(logger, statusCode, attributesJson, null);
}

