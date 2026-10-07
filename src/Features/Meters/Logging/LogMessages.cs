using Microsoft.Extensions.Logging;

namespace Features.Meters.Logging;

/// <summary>
/// Readers belong to locations, so their event id sits in the Locations range (1400 - 1499).
/// Ids only; never a key.
/// </summary>
internal static class LogMessages
{
    private static readonly Action<ILogger, Guid, Guid, Exception?> _readerRegisteredByAdmin =
        LoggerMessage.Define<Guid, Guid>(
            LogLevel.Information,
            new EventId(1406, nameof(ReaderRegisteredByAdmin)),
            "Reader registered at location {LocationId} by administrator {ActingUserId}");

    public static void ReaderRegisteredByAdmin(ILogger logger, Guid locationId, Guid actingUserId)
        => _readerRegisteredByAdmin(logger, locationId, actingUserId, null);
}
