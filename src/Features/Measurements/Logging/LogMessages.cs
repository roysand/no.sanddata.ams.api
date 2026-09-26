using Microsoft.Extensions.Logging;

namespace Features.Measurements.Logging;

internal static class LogMessages
{
    // Measurements: event ids 1300 - 1399
    private static readonly Action<ILogger, Guid, Guid, Exception?> _measurementsQueried =
        LoggerMessage.Define<Guid, Guid>(
            LogLevel.Information,
            new EventId(1300, nameof(MeasurementsQueried)),
            "Measurements queried by {UserId} for location {LocationId}");

    private static readonly Action<ILogger, Guid, Guid, Exception?> _latestMeasurementQueried =
        LoggerMessage.Define<Guid, Guid>(
            LogLevel.Information,
            new EventId(1301, nameof(LatestMeasurementQueried)),
            "Latest measurement queried by {UserId} for location {LocationId}");

    private static readonly Action<ILogger, Guid, Guid, Exception?> _locationAccessDenied =
        LoggerMessage.Define<Guid, Guid>(
            LogLevel.Warning,
            new EventId(1302, nameof(LocationAccessDenied)),
            "User {UserId} denied access to location {LocationId}");

    public static void MeasurementsQueried(ILogger logger, Guid userId, Guid locationId)
        => _measurementsQueried(logger, userId, locationId, null);

    public static void LatestMeasurementQueried(ILogger logger, Guid userId, Guid locationId)
        => _latestMeasurementQueried(logger, userId, locationId, null);

    public static void LocationAccessDenied(ILogger logger, Guid userId, Guid locationId)
        => _locationAccessDenied(logger, userId, locationId, null);
}
