using Microsoft.Extensions.Logging;

namespace Infrastructure.External.Logging;

/// <summary>
/// OSO water heater client: event ids 1820 - 1829 (WaterHeater area, 1800 - 1899). Entries name the location and the
/// call only. The URL holds the device id and the request holds the key, so neither the URL, the exception nor its
/// message is ever logged.
/// </summary>
internal static class OsoLogMessages
{
    private static readonly Action<ILogger, Guid, string, int, Exception?> _requestFailed =
        LoggerMessage.Define<Guid, string, int>(
            LogLevel.Warning,
            new EventId(1820, nameof(RequestFailed)),
            "OSO request for location {LocationId} ({Call}) failed with HTTP {StatusCode}");

    private static readonly Action<ILogger, Guid, string, Exception?> _unavailable =
        LoggerMessage.Define<Guid, string>(
            LogLevel.Warning,
            new EventId(1821, nameof(Unavailable)),
            "OSO could not be reached for location {LocationId} ({Call}) (reason: Unavailable)");

    private static readonly Action<ILogger, Guid, string, Exception?> _invalidResponse =
        LoggerMessage.Define<Guid, string>(
            LogLevel.Warning,
            new EventId(1822, nameof(InvalidResponse)),
            "OSO answer for location {LocationId} ({Call}) could not be used (reason: InvalidResponse)");

    public static void RequestFailed(ILogger logger, Guid locationId, string call, int statusCode)
        => _requestFailed(logger, locationId, call, statusCode, null);

    public static void Unavailable(ILogger logger, Guid locationId, string call)
        => _unavailable(logger, locationId, call, null);

    public static void InvalidResponse(ILogger logger, Guid locationId, string call)
        => _invalidResponse(logger, locationId, call, null);
}
