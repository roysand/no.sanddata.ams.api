using Microsoft.Extensions.Logging;

namespace Features.Locations.Logging;

internal static class LogMessages
{
    // Locations: event ids 1400 - 1499
    private static readonly Action<ILogger, Guid, int, Exception?> _locationsListed =
        LoggerMessage.Define<Guid, int>(
            LogLevel.Information,
            new EventId(1400, nameof(LocationsListed)),
            "Locations listed for {UserId}: {Count} found");

    public static void LocationsListed(ILogger logger, Guid userId, int count)
        => _locationsListed(logger, userId, count, null);
}
