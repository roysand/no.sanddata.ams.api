using Microsoft.Extensions.Logging;

namespace Features.Locations.Logging;

/// <summary>Locations: event ids 1400 - 1499. Ids and the acting user only; never a key or a fingerprint.</summary>
internal static class LogMessages
{
    private static readonly Action<ILogger, Guid, int, Exception?> _locationsListed =
        LoggerMessage.Define<Guid, int>(
            LogLevel.Information,
            new EventId(1400, nameof(LocationsListed)),
            "Locations listed for {UserId}: {Count} found");

    private static readonly Action<ILogger, Guid, Guid, Exception?> _locationCreated =
        LoggerMessage.Define<Guid, Guid>(
            LogLevel.Information,
            new EventId(1401, nameof(LocationCreated)),
            "Location {LocationId} created by {ActingUserId}");

    private static readonly Action<ILogger, Guid, Guid, Exception?> _locationUpdated =
        LoggerMessage.Define<Guid, Guid>(
            LogLevel.Information,
            new EventId(1402, nameof(LocationUpdated)),
            "Location {LocationId} updated by {ActingUserId}");

    private static readonly Action<ILogger, Guid, bool, Guid, Exception?> _locationActiveChanged =
        LoggerMessage.Define<Guid, bool, Guid>(
            LogLevel.Information,
            new EventId(1403, nameof(LocationActiveChanged)),
            "Location {LocationId} active set to {IsActive} by {ActingUserId}");

    private static readonly Action<ILogger, Guid, Guid, Exception?> _keyRotated =
        LoggerMessage.Define<Guid, Guid>(
            LogLevel.Information,
            new EventId(1404, nameof(KeyRotated)),
            "Sensor key of location {LocationId} rotated by {ActingUserId}");

    private static readonly Action<ILogger, Guid, bool, Guid, Exception?> _keyActiveChanged =
        LoggerMessage.Define<Guid, bool, Guid>(
            LogLevel.Information,
            new EventId(1405, nameof(KeyActiveChanged)),
            "Sensor key of location {LocationId} active set to {IsActive} by {ActingUserId}");

    public static void LocationsListed(ILogger logger, Guid userId, int count)
        => _locationsListed(logger, userId, count, null);

    public static void LocationCreated(ILogger logger, Guid locationId, Guid actingUserId)
        => _locationCreated(logger, locationId, actingUserId, null);

    public static void LocationUpdated(ILogger logger, Guid locationId, Guid actingUserId)
        => _locationUpdated(logger, locationId, actingUserId, null);

    public static void LocationActiveChanged(ILogger logger, Guid locationId, bool isActive, Guid actingUserId)
        => _locationActiveChanged(logger, locationId, isActive, actingUserId, null);

    public static void KeyRotated(ILogger logger, Guid locationId, Guid actingUserId)
        => _keyRotated(logger, locationId, actingUserId, null);

    public static void KeyActiveChanged(ILogger logger, Guid locationId, bool isActive, Guid actingUserId)
        => _keyActiveChanged(logger, locationId, isActive, actingUserId, null);
}
