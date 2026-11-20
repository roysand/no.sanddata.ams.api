using Microsoft.Extensions.Logging;

namespace Features.EvChargers.Logging;

/// <summary>
/// EV charger (Zaptec): event ids 1900 - 1999 (1900 - 1909 the connection and setting, 1910 - 1919 the read job, the
/// Zaptec client 1920 - 1929). Ids and reason codes only: the Zaptec user name, password and tokens are never logged.
/// Reason codes are the error codes without the prefix: NotConfigured, SignInRejected, Unavailable, InvalidResponse,
/// UnknownCharger, ChargerInUse, NotConnected, NotFound, Unexpected.
/// </summary>
internal static class LogMessages
{
    private static readonly Action<ILogger, Guid, Guid, bool, Exception?> _connected =
        LoggerMessage.Define<Guid, Guid, bool>(
            LogLevel.Information,
            new EventId(1900, nameof(EvChargerConnected)),
            "EV charger connected to location {LocationId} by {ActingUserId} (replaced: {Replaced})");

    private static readonly Action<ILogger, Guid, Guid, string, Exception?> _connectRefused =
        LoggerMessage.Define<Guid, Guid, string>(
            LogLevel.Warning,
            new EventId(1901, nameof(EvChargerConnectRefused)),
            "EV charger connection to location {LocationId} by {ActingUserId} refused (reason: {Reason})");

    private static readonly Action<ILogger, Guid, Guid, Exception?> _removed =
        LoggerMessage.Define<Guid, Guid>(
            LogLevel.Information,
            new EventId(1902, nameof(EvChargerRemoved)),
            "EV charger removed from location {LocationId} by {ActingUserId}");

    private static readonly Action<ILogger, Guid, bool, Guid, Exception?> _includeChanged =
        LoggerMessage.Define<Guid, bool, Guid>(
            LogLevel.Information,
            new EventId(1903, nameof(EvChargerIncludeChanged)),
            "EV charger of location {LocationId} counted with the house: {Include} (by {ActingUserId})");

    private static readonly Action<ILogger, Guid, string, Exception?> _readFailing =
        LoggerMessage.Define<Guid, string>(
            LogLevel.Warning,
            new EventId(1910, nameof(EvChargerReadFailing)),
            "Reading the EV charger of location {LocationId} failed (reason: {Reason})");

    private static readonly Action<ILogger, Guid, string, int, Exception?> _readStillFailing =
        LoggerMessage.Define<Guid, string, int>(
            LogLevel.Warning,
            new EventId(1911, nameof(EvChargerReadStillFailing)),
            "Reading the EV charger of location {LocationId} still fails (reason: {Reason}) after {Minutes} minutes");

    private static readonly Action<ILogger, Guid, int, Exception?> _readRecovered =
        LoggerMessage.Define<Guid, int>(
            LogLevel.Information,
            new EventId(1912, nameof(EvChargerReadRecovered)),
            "EV charger of location {LocationId} read again after {Minutes} minutes of failures");

    private static readonly Action<ILogger, string, Exception?> _readCycleFailed =
        LoggerMessage.Define<string>(
            LogLevel.Error,
            new EventId(1913, nameof(EvChargerReadCycleFailed)),
            "EV charger read cycle failed unexpectedly with {ExceptionType} (reason: Unexpected)");

    private static readonly Action<ILogger, Exception?> _notConfigured =
        LoggerMessage.Define(
            LogLevel.Information,
            new EventId(1914, nameof(EvChargerNotConfigured)),
            "No Zaptec sign-in is set up on the server; EV chargers are not read (reason: NotConfigured)");

    private static readonly Action<ILogger, Guid, int, Exception?> _sessionsStored =
        LoggerMessage.Define<Guid, int>(
            LogLevel.Debug,
            new EventId(1915, nameof(EvChargerSessionsStored)),
            "Stored {Count} charging sessions for location {LocationId}");

    public static void EvChargerConnected(ILogger logger, Guid actingUserId, Guid locationId, bool replaced) =>
        _connected(logger, locationId, actingUserId, replaced, null);

    public static void EvChargerConnectRefused(ILogger logger, Guid actingUserId, Guid locationId, string reason) =>
        _connectRefused(logger, locationId, actingUserId, reason, null);

    public static void EvChargerRemoved(ILogger logger, Guid actingUserId, Guid locationId) =>
        _removed(logger, locationId, actingUserId, null);

    public static void EvChargerIncludeChanged(ILogger logger, Guid locationId, bool include, Guid actingUserId) =>
        _includeChanged(logger, locationId, include, actingUserId, null);

    public static void EvChargerReadFailing(ILogger logger, Guid locationId, string reason) =>
        _readFailing(logger, locationId, reason, null);

    public static void EvChargerReadStillFailing(ILogger logger, Guid locationId, string reason, int minutes) =>
        _readStillFailing(logger, locationId, reason, minutes, null);

    public static void EvChargerReadRecovered(ILogger logger, Guid locationId, int minutes) =>
        _readRecovered(logger, locationId, minutes, null);

    public static void EvChargerReadCycleFailed(ILogger logger, string exceptionType) =>
        _readCycleFailed(logger, exceptionType, null);

    public static void EvChargerNotConfigured(ILogger logger) => _notConfigured(logger, null);

    public static void EvChargerSessionsStored(ILogger logger, Guid locationId, int count) =>
        _sessionsStored(logger, locationId, count, null);

    public static string Reason(Domain.Common.Error error) =>
        error.Code[(error.Code.IndexOf('.', StringComparison.Ordinal) + 1)..];
}
