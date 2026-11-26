using Microsoft.Extensions.Logging;

namespace Features.WaterHeaters.Logging;

/// <summary>
/// Water heater (OSO): event ids 1800 - 1899 (1800 - 1819 here, the OSO client 1820 - 1829, the sampler 1830 - 1849).
/// Ids and reason codes only: the OSO subscription key and the device id are never logged. Reason codes are the
/// error codes without the prefix: Rejected, Unavailable, InvalidResponse, KeyUnreadable, EncryptionNotConfigured,
/// DeviceInUse, KeyExpiryOutOfRange, HighDemandNotOffered, NotFound, Unexpected.
/// </summary>
internal static class LogMessages
{
    private static readonly Action<ILogger, Guid, Guid, bool, Exception?> _connected =
        LoggerMessage.Define<Guid, Guid, bool>(
            LogLevel.Information,
            new EventId(1800, nameof(WaterHeaterConnected)),
            "Water heater connected to location {LocationId} by {ActingUserId} (replaced: {Replaced})");

    private static readonly Action<ILogger, Guid, Guid, string, Exception?> _connectRefused =
        LoggerMessage.Define<Guid, Guid, string>(
            LogLevel.Warning,
            new EventId(1801, nameof(WaterHeaterConnectRefused)),
            "Water heater connection to location {LocationId} by {ActingUserId} refused (reason: {Reason})");

    private static readonly Action<ILogger, Guid, Guid, Exception?> _removed =
        LoggerMessage.Define<Guid, Guid>(
            LogLevel.Information,
            new EventId(1802, nameof(WaterHeaterRemoved)),
            "Water heater removed from location {LocationId} by {ActingUserId}");

    private static readonly Action<ILogger, Guid, Guid, Exception?> _stateRead =
        LoggerMessage.Define<Guid, Guid>(
            LogLevel.Information,
            new EventId(1803, nameof(WaterHeaterStateRead)),
            "Water heater state of location {LocationId} read by {ActingUserId}");

    private static readonly Action<ILogger, Guid, string, Exception?> _stateFailed =
        LoggerMessage.Define<Guid, string>(
            LogLevel.Warning,
            new EventId(1804, nameof(WaterHeaterStateFailed)),
            "Water heater state of location {LocationId} could not be read (reason: {Reason})");

    private static readonly Action<ILogger, Guid, Guid, Exception?> _accessDenied =
        LoggerMessage.Define<Guid, Guid>(
            LogLevel.Warning,
            new EventId(1805, nameof(LocationAccessDenied)),
            "Water heater of location {LocationId} requested by {ActingUserId}, who may not see it (reason: NotFound)");

    private static readonly Action<ILogger, Guid, Guid, DateOnly, Exception?> _keyExpiryChanged =
        LoggerMessage.Define<Guid, Guid, DateOnly>(
            LogLevel.Information,
            new EventId(1806, nameof(WaterHeaterKeyExpiryChanged)),
            "Water heater key expiry of location {LocationId} set to {ExpiresOn} by {ActingUserId}");

    private static readonly Action<ILogger, Guid, Guid, bool, bool, Exception?> _highDemandSwitched =
        LoggerMessage.Define<Guid, Guid, bool, bool>(
            LogLevel.Information,
            new EventId(1807, nameof(WaterHeaterHighDemandSwitched)),
            "Water heater High Demand of location {LocationId} switched by {ActingUserId} " +
            "(on: {On}, state read: {StateRead})");

    private static readonly Action<ILogger, Guid, Guid, bool, string, Exception?> _highDemandFailed =
        LoggerMessage.Define<Guid, Guid, bool, string>(
            LogLevel.Warning,
            new EventId(1808, nameof(WaterHeaterHighDemandFailed)),
            "Water heater High Demand of location {LocationId} not switched by {ActingUserId} " +
            "(on: {On}, reason: {Reason})");

    private static readonly Action<ILogger, Guid, string, Exception?> _sampleFailing =
        LoggerMessage.Define<Guid, string>(
            LogLevel.Warning,
            new EventId(1830, nameof(WaterHeaterSampleFailing)),
            "Water heater power of location {LocationId} could not be sampled (reason: {Reason})");

    private static readonly Action<ILogger, Guid, string, int, Exception?> _sampleStillFailing =
        LoggerMessage.Define<Guid, string, int>(
            LogLevel.Warning,
            new EventId(1831, nameof(WaterHeaterSampleStillFailing)),
            "Water heater power of location {LocationId} still not sampled (reason: {Reason}) after {Minutes} minutes");

    private static readonly Action<ILogger, Guid, int, Exception?> _sampleRecovered =
        LoggerMessage.Define<Guid, int>(
            LogLevel.Information,
            new EventId(1832, nameof(WaterHeaterSampleRecovered)),
            "Water heater power of location {LocationId} sampled again after {Minutes} minutes of failures");

    private static readonly Action<ILogger, Guid, string, Exception?> _litresFailing =
        LoggerMessage.Define<Guid, string>(
            LogLevel.Warning,
            new EventId(1834, nameof(WaterHeaterLitresFailing)),
            "Water heater litres of location {LocationId} could not be read (reason: {Reason}); power is still sampled");

    private static readonly Action<ILogger, Guid, string, int, Exception?> _litresStillFailing =
        LoggerMessage.Define<Guid, string, int>(
            LogLevel.Warning,
            new EventId(1835, nameof(WaterHeaterLitresStillFailing)),
            "Water heater litres of location {LocationId} still not read (reason: {Reason}) after {Minutes} minutes");

    private static readonly Action<ILogger, Guid, int, Exception?> _litresRecovered =
        LoggerMessage.Define<Guid, int>(
            LogLevel.Information,
            new EventId(1836, nameof(WaterHeaterLitresRecovered)),
            "Water heater litres of location {LocationId} read again after {Minutes} minutes of failures");

    private static readonly Action<ILogger, string, Exception?> _samplingCycleFailed =
        LoggerMessage.Define<string>(
            LogLevel.Error,
            new EventId(1833, nameof(WaterHeaterSamplingCycleFailed)),
            "Water heater sampling cycle failed unexpectedly with {ExceptionType} (reason: Unexpected)");

    public static void WaterHeaterConnected(ILogger logger, Guid actingUserId, Guid locationId, bool replaced)
        => _connected(logger, locationId, actingUserId, replaced, null);

    public static void WaterHeaterConnectRefused(ILogger logger, Guid actingUserId, Guid locationId, string reason)
        => _connectRefused(logger, locationId, actingUserId, reason, null);

    public static void WaterHeaterRemoved(ILogger logger, Guid actingUserId, Guid locationId)
        => _removed(logger, locationId, actingUserId, null);

    public static void WaterHeaterStateRead(ILogger logger, Guid actingUserId, Guid locationId)
        => _stateRead(logger, locationId, actingUserId, null);

    public static void WaterHeaterStateFailed(ILogger logger, Guid locationId, string reason)
        => _stateFailed(logger, locationId, reason, null);

    public static void LocationAccessDenied(ILogger logger, Guid actingUserId, Guid locationId)
        => _accessDenied(logger, locationId, actingUserId, null);

    public static void WaterHeaterKeyExpiryChanged(ILogger logger, Guid actingUserId, Guid locationId, DateOnly expiresOn)
        => _keyExpiryChanged(logger, locationId, actingUserId, expiresOn, null);

    public static void WaterHeaterHighDemandSwitched(
        ILogger logger, Guid actingUserId, Guid locationId, bool on, bool stateRead)
        => _highDemandSwitched(logger, locationId, actingUserId, on, stateRead, null);

    public static void WaterHeaterHighDemandFailed(
        ILogger logger, Guid actingUserId, Guid locationId, bool on, string reason)
        => _highDemandFailed(logger, locationId, actingUserId, on, reason, null);

    public static void WaterHeaterSampleFailing(ILogger logger, Guid locationId, string reason)
        => _sampleFailing(logger, locationId, reason, null);

    public static void WaterHeaterSampleStillFailing(ILogger logger, Guid locationId, string reason, int minutes)
        => _sampleStillFailing(logger, locationId, reason, minutes, null);

    public static void WaterHeaterSampleRecovered(ILogger logger, Guid locationId, int minutes)
        => _sampleRecovered(logger, locationId, minutes, null);

    public static void WaterHeaterLitresFailing(ILogger logger, Guid locationId, string reason)
        => _litresFailing(logger, locationId, reason, null);

    public static void WaterHeaterLitresStillFailing(ILogger logger, Guid locationId, string reason, int minutes)
        => _litresStillFailing(logger, locationId, reason, minutes, null);

    public static void WaterHeaterLitresRecovered(ILogger logger, Guid locationId, int minutes)
        => _litresRecovered(logger, locationId, minutes, null);

    public static void WaterHeaterSamplingCycleFailed(ILogger logger, string exceptionType)
        => _samplingCycleFailed(logger, exceptionType, null);

    /// <summary>The reason code of an error: its code without the "WaterHeater." or "Location." prefix.</summary>
    public static string Reason(Domain.Common.Error error) =>
        error.Code[(error.Code.IndexOf('.', StringComparison.Ordinal) + 1)..];
}
